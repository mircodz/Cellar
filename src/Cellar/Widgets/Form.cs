using System;
using System.Collections.Generic;
using System.Globalization;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>What a <see cref="FormField"/> holds, which decides how it's edited, shown and checked.</summary>
public enum FormFieldKind
{
    /// <summary>Free text, edited in place.</summary>
    Text,

    /// <summary>A number (invariant culture), optionally whole and within <see cref="FormField.Min"/>/<see cref="FormField.Max"/>.</summary>
    Number,

    /// <summary>A duration such as <c>90s</c>, <c>5m</c>, <c>1h30m</c>, <c>2d</c> or <c>00:05:00</c>.</summary>
    Duration,

    /// <summary>One of <see cref="FormField.Choices"/>: enter, space and ←/→ cycle through them.</summary>
    Choice,

    /// <summary>On or off (<c>true</c>/<c>false</c>): enter, space and ←/→ toggle it.</summary>
    Flag,

    /// <summary>Text that's never shown, such as a password: drawn as dots, also while editing.</summary>
    Secret,
}

/// <summary>
/// One value in a <see cref="Form"/>: what it's called, what it is now, what it was when the form opened, and
/// what's allowed. Values are strings, as they come from and go to a command line or a config file; the
/// <see cref="Kind"/> decides how they're checked and edited.
/// </summary>
public sealed class FormField
{
    private string _value;
    private FormFieldKind? _kind;
    private string? _masked;

    /// <param name="key">What the value is reported under in <see cref="Form.Changes"/>.</param>
    /// <param name="label">What the form shows.</param>
    /// <param name="value">The current value, which is also what the field reverts to.</param>
    public FormField(string key, string label, string value = "")
    {
        Key = key;
        Label = label;
        _value = value ?? string.Empty;
        OriginalValue = _value;
    }

    /// <summary>A flag field holding <paramref name="on"/>.</summary>
    public static FormField Flag(string key, string label, bool on) =>
        new(key, label, on ? "true" : "false") { Kind = FormFieldKind.Flag };

    /// <summary>A choice field holding <paramref name="value"/>, one of <paramref name="choices"/>.</summary>
    public static FormField Choice(string key, string label, string value, IReadOnlyList<string> choices) =>
        new(key, label, value) { Kind = FormFieldKind.Choice, Choices = choices };

    /// <summary>What the value is reported under in <see cref="Form.Changes"/>.</summary>
    public string Key { get; }

    /// <summary>What the form shows in front of the value.</summary>
    public string Label { get; set; }

    /// <summary>What the field holds and how it's edited. Defaults to <see cref="FormFieldKind.Choice"/> when
    /// <see cref="Choices"/> are given, else <see cref="FormFieldKind.Text"/>.</summary>
    public FormFieldKind Kind
    {
        get => _kind ?? (Choices.Count > 0 ? FormFieldKind.Choice : FormFieldKind.Text);
        init => _kind = value;
    }

    /// <summary>The value now, edits included.</summary>
    public string Value
    {
        get => _value;
        set => _value = value ?? string.Empty;
    }

    /// <summary>The value the field started with (or was last <see cref="Accept"/>ed at). <see cref="Revert"/> goes
    /// back to it, and a field whose value differs from it is changed.</summary>
    public string OriginalValue { get; private set; }

    /// <summary>Whether the value differs from <see cref="OriginalValue"/>.</summary>
    public bool IsChanged => !string.Equals(_value, OriginalValue, StringComparison.Ordinal);

    /// <summary>A short remark drawn dimmed after an unchanged value, such as the default or a unit.</summary>
    public string? Note { get; init; }

    /// <summary>A sentence about the field, shown on the form's bottom line while it's selected.</summary>
    public string? Hint { get; init; }

    /// <summary>The heading the field sits under. A heading is drawn wherever this differs from the field
    /// before.</summary>
    public string? Section { get; init; }

    /// <summary>What a <see cref="FormFieldKind.Choice"/> field cycles through.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>The smallest value a <see cref="FormFieldKind.Number"/> field accepts.</summary>
    public double? Min { get; init; }

    /// <summary>The largest value a <see cref="FormFieldKind.Number"/> field accepts.</summary>
    public double? Max { get; init; }

    /// <summary>Whether a <see cref="FormFieldKind.Number"/> field only accepts whole numbers.</summary>
    public bool Integer { get; init; }

    /// <summary>Whether the field must not be left empty.</summary>
    public bool Required { get; init; }

    /// <summary>Shown but not editable, for context such as a name or an ID.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>A check of its own after the built-in ones for the <see cref="Kind"/>: returns what's wrong with
    /// a value, or null when it's fine.</summary>
    public Func<string, string?>? Validate { get; init; }

    /// <summary>What's wrong with the current value, or null.</summary>
    public string? Error => Check(_value);

    /// <summary>For a flag, whether it's on.</summary>
    public bool IsOn => string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>What's wrong with <paramref name="value"/> for this field, or null if it may be stored.</summary>
    public string? Check(string value)
    {
        if (value.Length == 0)
        {
            return Required ? "required" : null;
        }

        switch (Kind)
        {
            case FormFieldKind.Number:
                if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                {
                    return Integer ? "expected a whole number" : "expected a number";
                }

                if (Integer && number != Math.Floor(number))
                {
                    return "expected a whole number";
                }

                if (number < Min || number > Max)
                {
                    return (Min, Max) switch
                    {
                        ({ } min, { } max) => $"expected {Format(min)} to {Format(max)}",
                        ({ } min, null) => $"expected at least {Format(min)}",
                        _ => $"expected at most {Format(Max!.Value)}",
                    };
                }

                break;
            case FormFieldKind.Duration:
                if (!TableQuery.TryDuration(value, out _))
                {
                    return "expected a duration like 30s, 5m or 1h30m";
                }

                break;
            case FormFieldKind.Choice when Choices.Count > 0:
                if (IndexOfChoice(value) < 0)
                {
                    return "expected one of " + string.Join(", ", Choices);
                }

                break;
        }

        return Validate?.Invoke(value);
    }

    /// <summary>Goes back to <see cref="OriginalValue"/>.</summary>
    public void Revert() => _value = OriginalValue;

    /// <summary>Makes the current value the original, so the field no longer counts as changed (after the
    /// changes were applied, say).</summary>
    public void Accept() => OriginalValue = _value;

    internal int IndexOfChoice(string value)
    {
        for (int i = 0; i < Choices.Count; i++)
        {
            if (string.Equals(Choices[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // The dots a secret shows, cached per length so drawing doesn't allocate.
    internal string Masked(string value)
    {
        int length = Math.Min(value.Length, 12);
        if (_masked is null || _masked.Length != length)
        {
            _masked = new string('•', length);
        }

        return _masked;
    }

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A list of labelled values to review and change: settings, the parameters of an operation, the fields of a
/// resource. Every field is one line, <c>label  value</c>, with a <c>*</c> and the old value next to the ones
/// changed, under optional section headings. The bottom line shows the selected field's hint, or what's wrong.
/// </summary>
/// <remarks>
/// <para>Keys: <c>↑</c>/<c>↓</c> (also <c>j</c>/<c>k</c>, tab) move, <c>enter</c> edits the value in place
/// (a choice or flag cycles instead), <c>←</c>/<c>→</c> and <c>space</c> cycle a choice or toggle a flag,
/// <c>u</c> reverts the field, <c>ctrl+s</c> (or <c>a</c>) submits, and <c>esc</c> cancels — the first one
/// only warns when there are changes, so a stray esc doesn't throw them away. While editing, enter keeps the
/// value (if it's valid), esc drops the edit, and ↑/↓/tab keep it and move on. The editor is an
/// <see cref="Input"/>, so the readline keys work.</para>
/// <para><see cref="Submit"/> checks every field and reports only the changed values, which is what an
/// "apply" wants: "set these three settings". <see cref="FormDialog"/> puts a form in a modal box with a title
/// and key hints.</para>
/// </remarks>
public sealed class Form : Widget
{
    private readonly List<FormField> _fields;
    private readonly List<int> _lines = new(); // field index per line, or ~fieldIndex for the heading above that field
    private readonly Input _editor = new();
    private int _selected;
    private int _scroll;
    private string? _message;
    private bool _messageIsError;
    private bool _confirmDiscard;
    private string? _editError;
    private Rect _bounds;
    private int _viewHeight;

    public Form(IEnumerable<FormField> fields)
    {
        _fields = new List<FormField>(fields);
        _editor.OnChange = _ =>
        {
            if (_editError is not null && Selected is { } field)
            {
                _editError = field.Check(_editor.Text);
            }
        };
        RebuildLines();
        _selected = NextEditable(-1, 1);
        if (_selected < 0)
        {
            _selected = _fields.Count > 0 ? 0 : -1;
        }
    }

    /// <summary>The fields, top to bottom.</summary>
    public IReadOnlyList<FormField> Fields => _fields;

    /// <summary>The selected field, or null when there are none.</summary>
    public FormField? Selected => _selected >= 0 && _selected < _fields.Count ? _fields[_selected] : null;

    /// <summary>The index of the selected field in <see cref="Fields"/>, or -1.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            StopEditing();
            _selected = _fields.Count == 0 ? -1 : Math.Clamp(value, 0, _fields.Count - 1);
            ClearMessage();
        }
    }

    /// <summary>Whether a value is being typed.</summary>
    public bool IsEditing { get; private set; }

    /// <summary>The text being typed while <see cref="IsEditing"/>.</summary>
    public string EditText => _editor.Text;

    /// <summary>How many fields are changed.</summary>
    public int ChangeCount
    {
        get
        {
            int count = 0;
            foreach (var f in _fields)
            {
                if (f.IsChanged)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>The changed fields' values by <see cref="FormField.Key"/>.</summary>
    public IReadOnlyDictionary<string, string> Changes
    {
        get
        {
            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in _fields)
            {
                if (f.IsChanged)
                {
                    changes[f.Key] = f.Value;
                }
            }

            return changes;
        }
    }

    /// <summary>Every f's value by <see cref="FormField.Key"/>.</summary>
    public IReadOnlyDictionary<string, string> Values
    {
        get
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in _fields)
            {
                values[f.Key] = f.Value;
            }

            return values;
        }
    }

    /// <summary>Called by <see cref="Submit"/> with the changed values (<see cref="Changes"/>) once every f
    /// is valid.</summary>
    public Action<IReadOnlyDictionary<string, string>>? OnSubmit { get; set; }

    /// <summary>Called when the form is cancelled with esc (the second esc, if there were changes).</summary>
    public Action? OnCancel { get; set; }

    /// <summary>Called after a field's value changes.</summary>
    public Action<FormField>? OnFieldChanged { get; set; }

    /// <summary>Whether <see cref="Submit"/> goes ahead when nothing changed. Off by default: submitting an
    /// unchanged form says "nothing changed" instead.</summary>
    public bool AllowEmptySubmit { get; set; }

    /// <summary>Whether the last line shows the selected field's hint or what's wrong. On by default.</summary>
    public bool ShowFooter { get; set; } = true;

    /// <summary>The width of the label column; 0 (the default) fits the longest label, up to half the width.</summary>
    public int LabelWidth { get; set; }

    /// <summary>What the bottom line says now (a problem or a warning), or null when it shows the hint.</summary>
    public string? Message => IsEditing && _editError is not null ? _editError : _message;

    /// <summary>The lines the fields and headings take, plus the footer.</summary>
    public int LineCount => _lines.Count + (ShowFooter ? 1 : 0);

    public override bool IsFocusable => true;

    public override Size Measure(Size available) => new(available.Width, Math.Min(available.Height, LineCount));

    /// <summary>Selects <paramref name="field"/>.</summary>
    public void Select(FormField field)
    {
        int index = _fields.IndexOf(field);
        if (index >= 0)
        {
            SelectedIndex = index;
        }
    }

    /// <summary>Checks every field; if one is wrong, selects it and says what's wrong. Otherwise calls
    /// <see cref="OnSubmit"/> with the changed values, unless nothing changed and <see cref="AllowEmptySubmit"/>
    /// is off. Returns whether <see cref="OnSubmit"/> was called.</summary>
    public bool Submit()
    {
        if (IsEditing && !CommitEdit())
        {
            return false;
        }

        for (int i = 0; i < _fields.Count; i++)
        {
            if (_fields[i].Error is { } error)
            {
                _selected = i;
                SetMessage($"{_fields[i].Label}: {error}", isError: true);
                return false;
            }
        }

        var changes = Changes;
        if (changes.Count == 0 && !AllowEmptySubmit)
        {
            SetMessage("nothing changed", isError: false);
            return false;
        }

        ClearMessage();
        OnSubmit?.Invoke(changes);
        return true;
    }

    /// <summary>Reverts every field.</summary>
    public void RevertAll()
    {
        StopEditing();
        foreach (var field in _fields)
        {
            if (field.IsChanged)
            {
                field.Revert();
                OnFieldChanged?.Invoke(field);
            }
        }

        ClearMessage();
    }

    /// <summary>Starts editing the selected field (a choice or flag cycles instead). Does nothing for a
    /// read-only field.</summary>
    public void Edit()
    {
        if (Selected is not { } field || IsEditing)
        {
            return;
        }

        if (field.ReadOnly)
        {
            SetMessage($"{field.Label} can't be changed here", isError: false);
            return;
        }

        switch (field.Kind)
        {
            case FormFieldKind.Choice:
            case FormFieldKind.Flag:
                Cycle(field, 1);
                return;
        }

        ClearMessage();
        _editError = null;
        _editor.Mask = field.Kind == FormFieldKind.Secret ? '•' : null;
        _editor.Text = field.Value;
        _editor.HasFocus = HasFocus;
        IsEditing = true;
    }

    public override bool HasFocus
    {
        get => base.HasFocus;
        set
        {
            base.HasFocus = value;
            _editor.HasFocus = value && IsEditing;
        }
    }

    public override void Render(Surface surface, Rect area)
    {
        _bounds = area;
        if (area.IsEmpty)
        {
            return;
        }

        var theme = Theme.Current;
        int footer = ShowFooter && area.Height > 1 ? 1 : 0;
        _viewHeight = area.Height - footer;
        EnsureSelectedVisible();

        int labelWidth = LabelWidth > 0 ? LabelWidth : AutoLabelWidth(area.Width);
        int valueX = area.X + 2 + labelWidth + 2;
        for (int row = 0; row < _viewHeight; row++)
        {
            int line = _scroll + row;
            if (line >= _lines.Count)
            {
                break;
            }

            int y = area.Y + row;
            int entry = _lines[line];
            if (entry < 0)
            {
                surface.DrawText(area.X, y, _fields[~entry].Section!, theme.HeaderStyle);
                continue;
            }

            DrawField(surface, area, y, entry, labelWidth, valueX);
        }

        if (footer > 0)
        {
            DrawFooter(surface, area.X, area.Bottom - 1, area.Right);
        }
    }

    private void DrawField(Surface surface, Rect area, int y, int index, int labelWidth, int valueX)
    {
        var theme = Theme.Current;
        var field = _fields[index];
        bool selected = index == _selected;
        bool editing = selected && IsEditing;
        var clip = surface.Clip;
        surface.SetClip(new Rect(area.X, y, area.Width, 1).Intersect(clip));

        var lineStyle = selected && !editing && HasFocus ? theme.SelectionStyle : theme.TextStyle;
        if (lineStyle.Background != Color.Default)
        {
            surface.FillRect(new Rect(area.X, y, area.Width, 1), lineStyle);
        }

        if (field.IsChanged)
        {
            surface.DrawText(area.X, y, "*", (selected && !editing && HasFocus ? lineStyle : new Style(theme.Warning, Color.Default)).Bold);
        }

        var labelStyle = editing ? theme.AccentStyle.Bold : field.ReadOnly && !selected ? theme.MutedStyle : lineStyle;
        surface.SetClip(new Rect(area.X + 2, y, labelWidth, 1).Intersect(clip));
        surface.DrawText(area.X + 2, y, field.Label, labelStyle);
        surface.SetClip(new Rect(area.X, y, area.Width, 1).Intersect(clip));

        if (editing)
        {
            _editor.Render(surface, new Rect(valueX, y, Math.Max(0, area.Right - valueX), 1).Intersect(clip));
            surface.SetClip(clip);
            return;
        }

        bool highlighted = selected && HasFocus;
        var valueStyle = highlighted ? lineStyle : field.IsChanged ? new Style(theme.Warning, Color.Default).Bold : theme.TextStyle;
        var mutedStyle = highlighted ? lineStyle : theme.MutedStyle;
        int x = DrawValue(surface, valueX, y, field, field.Value, valueStyle, mutedStyle);
        if (field.IsChanged)
        {
            x = surface.DrawText(x, y, "  was ", mutedStyle);
            DrawValue(surface, x, y, field, field.OriginalValue, mutedStyle, mutedStyle);
        }
        else if (field.Note is { } note)
        {
            x = surface.DrawText(x, y, "  (", mutedStyle);
            x = surface.DrawText(x, y, note, mutedStyle);
            surface.DrawText(x, y, ")", mutedStyle);
        }

        surface.SetClip(clip);
    }

    private static int DrawValue(Surface surface, int x, int y, FormField field, string value, Style style, Style muted)
    {
        switch (field.Kind)
        {
            case FormFieldKind.Flag:
                return surface.DrawText(x, y, string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "[x] on" : "[ ] off", style);
            case FormFieldKind.Secret when value.Length > 0:
                return surface.DrawText(x, y, field.Masked(value), style);
        }

        return value.Length == 0 ? surface.DrawText(x, y, "-", muted) : surface.DrawText(x, y, value, style);
    }

    private void DrawFooter(Surface surface, int x, int y, int right)
    {
        var theme = Theme.Current;
        var clip = surface.Clip;
        surface.SetClip(new Rect(x, y, right - x, 1).Intersect(clip));
        if (Message is { } message)
        {
            bool error = IsEditing && _editError is not null || _messageIsError;
            surface.DrawText(x, y, message, new Style(error ? theme.Error : theme.Warning, Color.Default));
        }
        else if (Selected?.Hint is { } hint)
        {
            surface.DrawText(x, y, hint, theme.MutedStyle);
        }

        surface.SetClip(clip);
    }

    public override bool OnEvent(InputEvent e)
    {
        if (e is MouseEvent mouse)
        {
            return OnMouse(mouse);
        }

        if (e is PasteEvent && IsEditing)
        {
            return _editor.OnEvent(e);
        }

        if (e is not KeyEvent key || !HasFocus)
        {
            return false;
        }

        return IsEditing ? OnEditKey(key) : OnKey(key);
    }

    private bool OnKey(KeyEvent key)
    {
        bool ctrl = (key.Modifiers & KeyModifiers.Control) != 0;
        bool shift = (key.Modifiers & KeyModifiers.Shift) != 0;
        int c = key.Key == Key.Char ? key.Rune.Value : 0;
        if (key.Key != Key.Escape)
        {
            _confirmDiscard = false;
        }

        switch (key.Key)
        {
            case Key.Escape:
                int changes = ChangeCount;
                if (changes > 0 && !_confirmDiscard)
                {
                    _confirmDiscard = true;
                    SetMessage($"esc again to discard {changes} change{(changes == 1 ? "" : "s")}", isError: false);
                    return true;
                }

                _confirmDiscard = false;
                ClearMessage();
                OnCancel?.Invoke();
                return true;
            case Key.Up:
                return Move(-1);
            case Key.Down:
                return Move(1);
            case Key.Tab:
                return Move(shift ? -1 : 1);
            case Key.PageUp:
                return Move(-Math.Max(1, _viewHeight - 1));
            case Key.PageDown:
                return Move(Math.Max(1, _viewHeight - 1));
            case Key.Home:
                return Move(-_fields.Count);
            case Key.End:
                return Move(_fields.Count);
            case Key.Enter:
                Edit();
                return true;
            case Key.Left or Key.Right when Selected is { ReadOnly: false, Kind: FormFieldKind.Choice or FormFieldKind.Flag } field:
                Cycle(field, key.Key == Key.Right ? 1 : -1);
                return true;
            case Key.Char when ctrl && c == 's':
                Submit();
                return true;
            case Key.Char when ctrl:
                return false;
            case Key.Char when c == ' ' && Selected is { ReadOnly: false, Kind: FormFieldKind.Choice or FormFieldKind.Flag } field:
                Cycle(field, 1);
                return true;
            case Key.Char when c == 'j':
                return Move(1);
            case Key.Char when c == 'k':
                return Move(-1);
            case Key.Char when c == 'e':
                Edit();
                return true;
            case Key.Char when c == 'a':
                Submit();
                return true;
            case Key.Char when c == 'u' && Selected is { } selected:
                if (selected.IsChanged)
                {
                    selected.Revert();
                    OnFieldChanged?.Invoke(selected);
                }

                ClearMessage();
                return true;
        }

        return false;
    }

    private bool OnEditKey(KeyEvent key)
    {
        bool shift = (key.Modifiers & KeyModifiers.Shift) != 0;
        switch (key.Key)
        {
            case Key.Escape:
                StopEditing();
                return true;
            case Key.Enter:
                CommitEdit();
                return true;
            case Key.Up or Key.Down or Key.Tab:
                if (CommitEdit())
                {
                    Move(key.Key == Key.Up || key.Key == Key.Tab && shift ? -1 : 1);
                }

                return true;
            case Key.Char when (key.Modifiers & KeyModifiers.Control) != 0 && key.Rune.Value == 's':
                Submit();
                return true;
        }

        _editor.OnEvent(key);
        return true;
    }

    private bool OnMouse(MouseEvent mouse)
    {
        if (!_bounds.Contains(mouse.X, mouse.Y))
        {
            return false;
        }

        if (mouse.Kind == MouseEventKind.Wheel)
        {
            return Move(mouse.Button == MouseButton.WheelUp ? -1 : 1);
        }

        if (mouse.Kind != MouseEventKind.Down || mouse.Button != MouseButton.Left)
        {
            return true;
        }

        int line = _scroll + mouse.Y - _bounds.Y;
        if (mouse.Y - _bounds.Y >= _viewHeight || line >= _lines.Count || _lines[line] < 0)
        {
            return true;
        }

        int index = _lines[line];
        if (IsEditing)
        {
            if (index == _selected || !CommitEdit())
            {
                return true;
            }
        }

        if (index == _selected)
        {
            Edit();
        }
        else
        {
            _selected = index;
            ClearMessage();
        }

        return true;
    }

    private bool Move(int delta)
    {
        if (_fields.Count == 0)
        {
            return true;
        }

        int target = Math.Clamp(_selected + delta, 0, _fields.Count - 1);
        if (target != _selected)
        {
            _selected = target;
            ClearMessage();
        }

        return true;
    }

    private void Cycle(FormField field, int direction)
    {
        if (field.Kind == FormFieldKind.Flag)
        {
            field.Value = field.IsOn ? "false" : "true";
        }
        else if (field.Choices.Count > 0)
        {
            int index = field.IndexOfChoice(field.Value);
            int start = index < 0 ? (direction > 0 ? -1 : 0) : index;
            field.Value = field.Choices[(start + direction + field.Choices.Count) % field.Choices.Count];
        }

        ClearMessage();
        OnFieldChanged?.Invoke(field);
    }

    private bool CommitEdit()
    {
        if (!IsEditing || Selected is not { } field)
        {
            return true;
        }

        string text = _editor.Text;
        if (field.Check(text) is { } error)
        {
            _editError = $"{field.Label}: {error}";
            return false;
        }

        bool changed = !string.Equals(text, field.Value, StringComparison.Ordinal);
        field.Value = text;
        StopEditing();
        if (changed)
        {
            OnFieldChanged?.Invoke(field);
        }

        return true;
    }

    private void StopEditing()
    {
        IsEditing = false;
        _editError = null;
        _editor.HasFocus = false;
    }

    private void SetMessage(string message, bool isError)
    {
        _message = message;
        _messageIsError = isError;
    }

    private void ClearMessage()
    {
        _message = null;
        _confirmDiscard = false;
    }

    private int NextEditable(int from, int direction)
    {
        for (int i = from + direction; i >= 0 && i < _fields.Count; i += direction)
        {
            if (!_fields[i].ReadOnly)
            {
                return i;
            }
        }

        return -1;
    }

    private void RebuildLines()
    {
        _lines.Clear();
        string? section = null;
        for (int i = 0; i < _fields.Count; i++)
        {
            var field = _fields[i];
            if (field.Section is { } heading && !string.Equals(heading, section, StringComparison.Ordinal))
            {
                _lines.Add(~i);
            }

            section = field.Section;
            _lines.Add(i);
        }
    }

    private void EnsureSelectedVisible()
    {
        if (_selected < 0 || _viewHeight <= 0)
        {
            _scroll = 0;
            return;
        }

        int line = _lines.IndexOf(_selected);
        // Keep the heading above the first field of a section in view along with it.
        int top = line > 0 && _lines[line - 1] < 0 ? line - 1 : line;
        if (top < _scroll)
        {
            _scroll = top;
        }
        else if (line >= _scroll + _viewHeight)
        {
            _scroll = line - _viewHeight + 1;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _lines.Count - _viewHeight));
    }

    private int AutoLabelWidth(int width)
    {
        int longest = 0;
        foreach (var field in _fields)
        {
            longest = Math.Max(longest, Unicode.StringWidth(field.Label));
        }

        return Math.Max(1, Math.Min(longest, width / 2 - 2));
    }
}
