using System;
using System.Collections.Generic;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// One kind of prompt a <see cref="CommandLine"/> can open: <c>:</c> for commands, <c>/</c> for a filter, or a
/// question. Each keeps its own history, so ↑ in the filter recalls filters, not commands.
/// </summary>
public sealed class CommandMode
{
    public CommandMode(string prompt) => Prompt = prompt;

    /// <summary>What's drawn before the text, e.g. ":" or "/" or "Scale to: ".</summary>
    public string Prompt { get; set; }

    /// <summary>Shown dimmed while the text is empty.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>Earlier entries for ↑/↓; submitted text is added. Null for none.</summary>
    public InputHistory? History { get; set; }

    /// <summary>Candidates for tab completion, the ghost text and the popup. Null for none.</summary>
    public ICompletionSource? Completion { get; set; }

    /// <summary>Checks the text: null when it's fine, else a message shown at the right of the line. Enter does
    /// nothing while there's a message.</summary>
    public Func<string, string?>? Validate { get; set; }

    /// <summary>Runs on every edit, e.g. to filter as the user types.</summary>
    public Action<string>? OnChange { get; set; }

    /// <summary>Runs with the text when Enter is pressed and the text is valid. The line is closed first, so this
    /// may open another prompt.</summary>
    public Action<string>? OnSubmit { get; set; }

    /// <summary>Runs when Esc (or ctrl+c) closes the prompt.</summary>
    public Action? OnCancel { get; set; }
}

/// <summary>
/// The bottom line of a keyboard-driven, vim-style app: a <see cref="StatusLine"/> that turns into a one-line prompt while a
/// <see cref="CommandMode"/> is open. The prompt edits with <see cref="Widgets.Input"/>'s readline keys, offers
/// completions as dim ghost text (→ or tab accepts, tab again cycles) and a popup over the rows above, validates as
/// you type, and returns to the status line on Enter or Esc.
/// </summary>
/// <remarks>
/// Lay it out as the last row of the screen (a one-row <see cref="Stack"/> child): the completion popup is drawn
/// over the rows above it, so it must render after them. While a prompt is open it should get every key before the
/// page does, which a container's focus routing doesn't guarantee (a <see cref="Stack"/> keeps Tab for focus); wire
/// <see cref="Intercept"/> as <see cref="App.OnEvent"/>.
/// </remarks>
public sealed class CommandLine : Widget
{
    private IReadOnlyList<CompletionItem> _candidates = Array.Empty<CompletionItem>();
    private string? _ghost; // the candidate the ghost text completes to
    private int _cycle = -1; // index into _candidates while tab-cycling, else -1
    private bool _cycling;
    private bool _showError;
    private string? _error;
    private Rect _bounds;

    public CommandLine(StatusLine? status = null)
    {
        Status = status ?? new StatusLine();
        Input.OnChange = OnInputChanged;
    }

    /// <summary>What the line shows while no prompt is open.</summary>
    public StatusLine Status { get; }

    /// <summary>The text field of the prompt. Its <see cref="Input.OnChange"/> belongs to the command line; use
    /// <see cref="CommandMode.OnChange"/>.</summary>
    public Input Input { get; } = new();

    /// <summary>The open prompt, or null while the status line shows.</summary>
    public CommandMode? Mode { get; private set; }

    public bool IsOpen => Mode is not null;

    /// <summary>The prompt's validation message, or null.</summary>
    public string? Error => _showError ? _error : null;

    /// <summary>The current completion candidates, best first.</summary>
    public IReadOnlyList<CompletionItem> Candidates => _candidates;

    /// <summary>Whether to list candidates in a popup above the line. The ghost text shows either way.</summary>
    public bool ShowCompletionPopup { get; set; } = true;

    /// <summary>Most candidates the popup lists at once.</summary>
    public int MaxPopupItems { get; set; } = 8;

    /// <summary>Opens <paramref name="mode"/> with <paramref name="initial"/> text (caret at the end), replacing
    /// any open prompt without cancelling it.</summary>
    public void Open(CommandMode mode, string initial = "")
    {
        Mode = mode;
        Input.History = mode.History;
        Input.Placeholder = mode.Placeholder;
        Input.Text = initial;
        Input.HasFocus = true;
        _showError = false;
        Refresh();
    }

    /// <summary>Asks for a single value: shows <paramref name="label"/> with <paramref name="initial"/> text, and
    /// calls <paramref name="onSubmit"/> once the user presses Enter on a value <paramref name="validate"/>
    /// accepts (null accepts anything).</summary>
    public void Ask(string label, string initial, Func<string, string?>? validate, Action<string> onSubmit,
        Action? onCancel = null, ICompletionSource? completion = null)
    {
        string prompt = label.Length > 0 && !char.IsWhiteSpace(label[^1]) ? label + " " : label;
        Open(new CommandMode(prompt) { Validate = validate, OnSubmit = onSubmit, OnCancel = onCancel, Completion = completion }, initial);
    }

    /// <summary>Asks for a single value with no validation.</summary>
    public void Ask(string label, string initial, Action<string> onSubmit) => Ask(label, initial, null, onSubmit);

    /// <summary>A <c>:</c> mode that runs <paramref name="keymap"/>'s commands, completes their names (and
    /// arguments, through the returned mode's <see cref="KeymapCompletionSource"/>), and reports an unknown command
    /// on the status line.</summary>
    public CommandMode CommandsFor(Keymap keymap, string prompt = ":") => new(prompt)
    {
        History = new InputHistory(),
        Completion = new KeymapCompletionSource(keymap),
        OnSubmit = text =>
        {
            if (!string.IsNullOrWhiteSpace(text) && !keymap.RunCommand(text))
            {
                Status.ShowError($"unknown command: {text.Trim()}");
            }
        },
    };

    /// <summary>Closes the prompt as if Esc were pressed: runs <see cref="CommandMode.OnCancel"/>.</summary>
    public void Cancel()
    {
        var mode = Mode;
        Close();
        mode?.OnCancel?.Invoke();
    }

    /// <summary>Closes the prompt without running any callback.</summary>
    public void Close()
    {
        Mode = null;
        Input.HasFocus = false;
        Input.History = null;
        Input.Suggestion = null;
        _ghost = null;
        _candidates = Array.Empty<CompletionItem>();
        _cycle = -1;
        _cycling = false;
        _error = null;
    }

    /// <summary>Takes every key and paste while a prompt is open, so the page underneath doesn't react to typing;
    /// returns false (letting the event through) otherwise. Wire it as <see cref="App.OnEvent"/>, or call it first
    /// from your own handler.</summary>
    public bool Intercept(InputEvent e) => IsOpen && e is KeyEvent or PasteEvent && Handle(e);

    public override Size Measure(Size available) => new(available.Width, Math.Min(1, available.Height));

    public override void Render(Surface surface, Rect area)
    {
        _bounds = area.IsEmpty ? area : new Rect(area.X, area.Y, area.Width, 1);
        if (Mode is not { } mode)
        {
            Status.Render(surface, area);
            return;
        }

        if (area.IsEmpty)
        {
            return;
        }

        var theme = Theme.Current;
        surface.FillRect(_bounds, new Style(theme.Foreground, Color.Default));
        int x = surface.DrawText(area.X, area.Y, mode.Prompt, new Style(theme.Accent, Color.Default).Bold);
        int right = area.Right;
        if (Error is { } error)
        {
            int w = Unicode.StringWidth(error);
            int start = Math.Max(x + 4, right - w);
            if (start < right)
            {
                var clip = surface.Clip;
                surface.SetClip(new Rect(start, area.Y, right - start, 1).Intersect(clip));
                surface.DrawText(start, area.Y, error, new Style(theme.Error, Color.Default));
                surface.SetClip(clip);
                right = start - 1;
            }
        }

        if (right > x)
        {
            var field = new Rect(x, area.Y, right - x, 1);
            if (mode.Placeholder.Length > 0 && Input.RuneCount == 0)
            {
                // The focused input hides its own placeholder, so draw it here, under the caret.
                var clip = surface.Clip;
                surface.SetClip(field.Intersect(clip));
                surface.DrawText(x, area.Y, mode.Placeholder, Input.PlaceholderStyle ?? new Style(theme.Muted, Color.Default));
                surface.SetClip(clip);
            }

            Input.Render(surface, field);
        }

        if (ShowCompletionPopup && (_cycling || Input.Text.Length > 0) && _candidates.Count > 0)
        {
            RenderPopup(surface, x, area, theme);
        }
    }

    // A box of candidates sitting on top of the line, starting under the prompt text.
    private void RenderPopup(Surface surface, int x, Rect area, Theme theme)
    {
        // As many rows as fit above the line, border included.
        int rows = Math.Min(Math.Min(_candidates.Count, Math.Max(1, MaxPopupItems)), area.Y - surface.Bounds.Y - 2);
        if (rows < 1)
        {
            return;
        }

        int textWidth = 0, noteWidth = 0;
        for (int i = 0; i < rows; i++)
        {
            textWidth = Math.Max(textWidth, Unicode.StringWidth(_candidates[i].Text));
            noteWidth = Math.Max(noteWidth, _candidates[i].Description is { } d ? Unicode.StringWidth(d) : 0);
        }

        bool more = _candidates.Count > rows;
        int inner = textWidth + (noteWidth > 0 ? 2 + noteWidth : 0);
        int width = Math.Min(inner + 4, area.Width);
        int height = rows + 2;
        int left = Math.Max(area.X, Math.Min(x - 2, area.Right - width));
        int top = area.Y - height;
        // Clipped to the screen, not to the caller's clip: that usually covers just this line, not the rows above.
        var clip = surface.Clip;
        var box = new Rect(left, top, width, height).Intersect(surface.Bounds);
        if (box.IsEmpty || width < 6)
        {
            return;
        }

        var bg = theme.Background;
        var border = new Style(theme.Border, bg);
        surface.SetClip(box);
        surface.FillRect(new Rect(left, top, width, height), new Style(theme.Foreground, bg));
        DrawFrame(surface, left, top, width, height, border);
        if (more)
        {
            surface.DrawText(left + width - 2 - Unicode.StringWidth("…"), top + height - 1, "…", border);
        }

        // Keep the highlighted candidate in view while cycling past the visible rows.
        int highlight = Math.Max(0, _cycle);
        int first = Math.Clamp(highlight - rows + 1, 0, Math.Max(0, _candidates.Count - rows));
        for (int row = 0; row < rows; row++)
        {
            int index = first + row;
            var item = _candidates[index];
            int y = top + 1 + row;
            bool selected = index == highlight;
            var style = selected ? theme.SelectionStyle : new Style(theme.Foreground, bg);
            if (selected)
            {
                surface.FillRect(new Rect(left + 1, y, width - 2, 1), style);
            }

            surface.SetClip(new Rect(left + 2, y, width - 4, 1).Intersect(box));
            surface.DrawText(left + 2, y, item.Text, selected ? style.Bold : style);
            if (item.Description is { } note)
            {
                surface.DrawText(left + 2 + textWidth + 2, y, note, selected ? style : new Style(theme.Muted, bg));
            }

            surface.SetClip(box);
        }

        surface.SetClip(clip);
    }

    private static void DrawFrame(Surface surface, int x, int y, int w, int h, Style style)
    {
        for (int i = 1; i < w - 1; i++)
        {
            surface.DrawText(x + i, y, "─", style);
            surface.DrawText(x + i, y + h - 1, "─", style);
        }

        for (int j = 1; j < h - 1; j++)
        {
            surface.DrawText(x, y + j, "│", style);
            surface.DrawText(x + w - 1, y + j, "│", style);
        }

        surface.DrawText(x, y, "╭", style);
        surface.DrawText(x + w - 1, y, "╮", style);
        surface.DrawText(x, y + h - 1, "╰", style);
        surface.DrawText(x + w - 1, y + h - 1, "╯", style);
    }

    public override bool OnEvent(InputEvent e)
    {
        if (IsOpen)
        {
            return e is KeyEvent or PasteEvent && Handle(e);
        }

        return Status.OnEvent(e);
    }

    private bool Handle(InputEvent e)
    {
        if (Mode is not { } mode)
        {
            return false;
        }

        if (e is KeyEvent key)
        {
            switch (key)
            {
                case { Key: Key.Escape }:
                case { Key: Key.Char, Modifiers: KeyModifiers.Control } when key.Rune.Value == 'c':
                    Cancel();
                    return true;

                case { Key: Key.Enter }:
                    Submit(mode);
                    return true;

                case { Key: Key.Tab }:
                    Cycle((key.Modifiers & KeyModifiers.Shift) != 0 ? -1 : 1);
                    return true;

                case { Key: Key.Right, Modifiers: KeyModifiers.None } when Input.CursorPosition == Input.RuneCount && Input.Suggestion is { Length: > 0 } && _ghost is { } ghost:
                    Input.Text = ghost; // the candidate as written, whatever case was typed
                    OnInputChanged(ghost);
                    return true;
            }
        }

        Input.OnEvent(e);
        return true; // an open prompt owns the keyboard: nothing typed here should reach the page
    }

    private void Submit(CommandMode mode)
    {
        string text = Input.Text;
        _error = mode.Validate?.Invoke(text);
        if (_error is not null)
        {
            _showError = true;
            return;
        }

        mode.History?.Add(text);
        Close();
        mode.OnSubmit?.Invoke(text);
    }

    // Tab accepts the best candidate; tab again moves to the next one (shift+tab back), wrapping. A lone candidate is
    // accepted and completion starts again from it, as in a shell: tab on "~/pk" gives "~/pkg/", tab again lists
    // what's in it.
    private void Cycle(int step)
    {
        if (_candidates.Count == 0)
        {
            return;
        }

        if (_candidates.Count == 1 && !_cycling)
        {
            Input.Text = _candidates[0].Text;
            Refresh();
            Mode?.OnChange?.Invoke(Input.Text);
            return;
        }

        if (!_cycling)
        {
            _cycling = true;
            _cycle = step > 0 ? 0 : _candidates.Count - 1;
        }
        else
        {
            _cycle = (_cycle + step + _candidates.Count) % _candidates.Count;
        }

        Input.Text = _candidates[_cycle].Text;
        Input.Suggestion = null;
        Validate();
        Mode?.OnChange?.Invoke(Input.Text);
    }

    private void OnInputChanged(string text)
    {
        _showError = true;
        Refresh();
        Mode?.OnChange?.Invoke(text);
    }

    // Recomputes candidates, the ghost text and the validation message after the text changed (not tab cycling).
    private void Refresh()
    {
        _cycling = false;
        _cycle = -1;
        string text = Input.Text;
        _candidates = Mode?.Completion?.Complete(text) ?? (IReadOnlyList<CompletionItem>)Array.Empty<CompletionItem>();
        Input.Suggestion = null;
        _ghost = null;
        if (text.Length > 0)
        {
            foreach (var candidate in _candidates)
            {
                if (candidate.Text.Length > text.Length && candidate.Text.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                {
                    _ghost = candidate.Text;
                    Input.Suggestion = candidate.Text[text.Length..];
                    break;
                }
            }
        }

        Validate();
    }

    private void Validate() => _error = Mode?.Validate?.Invoke(Input.Text);
}
