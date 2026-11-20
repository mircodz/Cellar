using System;
using System.Collections.Generic;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>How careful a <see cref="ConfirmDialog"/> makes the user be.</summary>
public enum ConfirmRisk
{
    /// <summary><c>y</c> or enter confirms, <c>n</c> or esc cancels.</summary>
    Normal,

    /// <summary>The user types <see cref="ConfirmDialog.ConfirmText"/> (the name of what's about to go, say) and presses
    /// enter, so muscle memory can't delete the wrong thing. Drawn in the error color.</summary>
    Destructive,
}

/// <summary>A switch the user can flip before confirming, such as "force" or "skip safety checks".</summary>
/// <param name="Key">The key that flips it (with alt in a <see cref="ConfirmRisk.Destructive"/> dialog, where
/// letters go to the text box).</param>
/// <param name="Label">What it does.</param>
public sealed record ConfirmToggle(char Key, string Label)
{
    /// <summary>Whether it's on.</summary>
    public bool On { get; set; }
}

/// <summary>What the user decided in a <see cref="ConfirmDialog"/>.</summary>
/// <param name="Confirmed">Whether to go ahead.</param>
/// <param name="Toggles">The dialog's toggles, with what the user set them to.</param>
public sealed record ConfirmResult(bool Confirmed, IReadOnlyList<ConfirmToggle> Toggles)
{
    /// <summary>Whether the toggle on <paramref name="key"/> is on.</summary>
    public bool IsOn(char key)
    {
        foreach (var toggle in Toggles)
        {
            if (toggle.Key == key)
            {
                return toggle.On;
            }
        }

        return false;
    }
}

/// <summary>
/// Asks before something runs: a headline, a few lines about what will happen, optional toggles shown as
/// checkboxes, and either <c>y</c>/<c>n</c> or, for a <see cref="ConfirmRisk.Destructive"/> operation,
/// "type <c>web-01</c> to confirm". <see cref="OnResult"/> gets the answer and the toggles; esc always cancels.
/// </summary>
/// <example><code>
/// ConfirmDialog.Show(app, new ConfirmDialog("Delete report.pdf?")
/// {
///     Details = ["It is removed from every synced device."],
///     Risk = ConfirmRisk.Destructive,
///     ConfirmText = "report.pdf",
///     Toggles = [new ConfirmToggle('f', "force: delete even if it's open")],
/// }, result => { if (result.Confirmed) Delete("report.pdf", force: result.IsOn('f')); });
/// </code></example>
public sealed class ConfirmDialog : Widget
{
    private readonly Input _input = new();
    private readonly BodyView _body = new();
    private Panel? _panel;
    private DialogBody? _frame;
    private bool _mismatch;
    private bool _dirty = true;
    private App? _app;
    private Overlay? _overlay;
    private string _headline = string.Empty;

    public ConfirmDialog(string headline)
    {
        Headline = headline;
        _input.OnChange = _ =>
        {
            _mismatch = false;
            _dirty = true;
        };
    }

    /// <summary>The question, drawn bold at the top.</summary>
    public string Headline
    {
        get => _headline;
        set
        {
            _headline = value;
            _dirty = true;
        }
    }

    /// <summary>Lines about what will happen, under the headline.</summary>
    public IReadOnlyList<StyledText> Details { get; init; } = [];

    /// <summary>A warning drawn in the warning color after the details, such as "this can't be undone".</summary>
    public string? Warning { get; init; }

    /// <summary>Whether <c>y</c> is enough or the user has to type <see cref="ConfirmText"/>.</summary>
    public ConfirmRisk Risk { get; init; }

    /// <summary>What a <see cref="ConfirmRisk.Destructive"/> dialog makes the user type, usually the name of what
    /// is about to go. Compared exactly, ignoring surrounding spaces.</summary>
    public string ConfirmText { get; init; } = "yes";

    /// <summary>Switches to set before confirming.</summary>
    public IReadOnlyList<ConfirmToggle> Toggles { get; init; } = [];

    /// <summary>The title in the border; defaults to "confirm", or "confirm · destructive".</summary>
    public string? Title { get; init; }

    /// <summary>Called once with the answer: confirmed (y, or enter with the right text), or not (n, esc).</summary>
    public Action<ConfirmResult>? OnResult { get; set; }

    /// <summary>What has been typed so far in a <see cref="ConfirmRisk.Destructive"/> dialog.</summary>
    public string TypedText => _input.Text;

    private bool Typed => Risk == ConfirmRisk.Destructive;

    public override bool IsFocusable => true;

    public override bool HasFocus
    {
        get => base.HasFocus;
        set
        {
            base.HasFocus = value;
            _input.HasFocus = value && Typed;
        }
    }

    /// <summary>The rows the dialog needs at <paramref name="width"/> columns, border included.</summary>
    public int PreferredHeight(int width)
    {
        // Measured two columns narrower than drawn: word wrapping can need a row more than this estimate.
        int body = TextRenderer.Wrap(BodyText(), Math.Max(10, width - 6)).Count;
        return body + (Typed ? 3 : 0) + 1 + 2;
    }

    /// <summary>Opens <paramref name="dialog"/> as a modal overlay <paramref name="width"/> columns wide and as tall
    /// as it needs. Answering closes it and then calls <paramref name="onResult"/>.</summary>
    public static ConfirmDialog Show(App app, ConfirmDialog dialog, Action<ConfirmResult> onResult, int width = 60)
    {
        dialog.OnResult = result =>
        {
            dialog.Close();
            onResult(result);
        };
        int w = Math.Min(width, Math.Max(20, app.Size.Width - 4));
        var overlay = new Overlay(dialog)
        {
            Width = w,
            Height = Math.Min(dialog.PreferredHeight(w), Math.Max(5, app.Size.Height - 2)),
            Modal = true,
            DismissOnEscape = false,
            ScrimOpacity = 0.4,
        };
        dialog._app = app;
        dialog._overlay = overlay;
        app.PushOverlay(overlay);
        return dialog;
    }

    /// <summary>Removes the dialog's overlay, if <see cref="Show"/> opened it.</summary>
    public void Close()
    {
        if (_app is { } app && _overlay is { } overlay)
        {
            app.RemoveOverlay(overlay);
        }

        _app = null;
        _overlay = null;
    }

    /// <summary>Confirms, if what was typed matches (for a destructive dialog); else says it doesn't.</summary>
    public bool Confirm()
    {
        if (Typed && !string.Equals(_input.Text.Trim(), ConfirmText.Trim(), StringComparison.Ordinal))
        {
            _mismatch = true;
            _dirty = true;
            return false;
        }

        OnResult?.Invoke(new ConfirmResult(true, Toggles));
        return true;
    }

    /// <summary>Answers no.</summary>
    public void Cancel() => OnResult?.Invoke(new ConfirmResult(false, Toggles));

    public override void Render(Surface surface, Rect area)
    {
        EnsureBuilt();
        if (_dirty)
        {
            _dirty = false;
            _body.Content = BodyText();
            _frame!.Hints = HintText();
        }

        _panel!.Render(surface, area);
    }

    public override bool OnEvent(InputEvent e)
    {
        if (e is PasteEvent && Typed)
        {
            return _input.OnEvent(e);
        }

        if (e is MouseEvent { Kind: MouseEventKind.Wheel } wheel)
        {
            _body.Scroll(wheel.Button == MouseButton.WheelUp ? -3 : 3);
            return true;
        }

        if (e is not KeyEvent key)
        {
            return false;
        }

        // Long details can be taller than the dialog: these scroll them, so nothing is confirmed unread.
        switch (key.Key)
        {
            case Key.Up:
                _body.Scroll(-1);
                return true;
            case Key.Down:
                _body.Scroll(1);
                return true;
            case Key.PageUp:
                _body.Scroll(-_body.Page);
                return true;
            case Key.PageDown:
                _body.Scroll(_body.Page);
                return true;
        }

        switch (key.Key)
        {
            case Key.Escape:
                Cancel();
                return true;
            case Key.Enter:
                Confirm();
                return true;
        }

        bool alt = (key.Modifiers & KeyModifiers.Alt) != 0;
        bool plain = key.Modifiers is KeyModifiers.None or KeyModifiers.Shift;
        if (key.Key == Key.Char && (Typed ? alt : plain) && FlipToggle(key.Rune.Value))
        {
            return true;
        }

        if (Typed)
        {
            _input.OnEvent(key);
            return true;
        }

        if (key.Key == Key.Char && plain)
        {
            switch (key.Rune.Value)
            {
                case 'y' or 'Y':
                    Confirm();
                    break;
                case 'n' or 'N':
                    Cancel();
                    break;
            }
        }

        // A modal question: nothing else should act on keys meanwhile.
        return true;
    }

    private bool FlipToggle(int c)
    {
        foreach (var toggle in Toggles)
        {
            if (toggle.Key == c)
            {
                toggle.On = !toggle.On;
                _dirty = true;
                return true;
            }
        }

        return false;
    }

    private void EnsureBuilt()
    {
        if (_panel is not null)
        {
            return;
        }

        var theme = Theme.Current;
        var layout = new Stack(Direction.Vertical).Add(_body, Constraint.Fill());
        if (Typed)
        {
            layout.Add(new Panel(_input) { BorderStyle = BorderStyle.Rounded }, Constraint.Length(3));
        }

        _frame = new DialogBody(layout);
        _panel = new Panel(_frame, Title ?? (Typed ? "confirm · destructive" : "confirm"))
        {
            BorderStyle = BorderStyle.Rounded,
            Background = theme.Background,
            BorderLineStyle = new Style(Typed ? theme.Error : theme.Warning, theme.Background),
        };
    }

    private StyledText BodyText()
    {
        var theme = Theme.Current;
        var text = new StyledText(Headline, new Style(theme.Foreground, Color.Default).Bold);
        foreach (var detail in Details)
        {
            text.Append("\n").Append(detail);
        }

        if (Warning is { } warning)
        {
            text.Append("\n\n" + warning, new Style(theme.Warning, Color.Default));
        }

        if (Toggles.Count > 0)
        {
            text.Append("\n");
        }

        foreach (var toggle in Toggles)
        {
            text.Append("\n" + (toggle.On ? "[x] " : "[ ] "), new Style(toggle.On ? theme.Warning : theme.Muted, Color.Default).Bold)
                .Append(Typed ? $"alt+{toggle.Key}" : toggle.Key.ToString(), theme.AccentStyle.Bold)
                .Append("  " + toggle.Label, theme.TextStyle);
        }

        if (Typed)
        {
            text.Append("\n\nType ", theme.MutedStyle)
                .Append(ConfirmText, theme.AccentStyle.Bold)
                .Append(" to confirm.", theme.MutedStyle);
        }

        return text;
    }

    // The dialog's text, wrapped once per size, with a scrollbar when it's taller than the room it has.
    private sealed class BodyView : Widget
    {
        private StyledText _content = StyledText.Empty();
        private List<StyledText>? _lines;
        private Size _size;
        private bool _overflow;
        private int _offset;

        public StyledText Content
        {
            get => _content;
            set
            {
                _content = value;
                _lines = null;
            }
        }

        public int Page => Math.Max(1, _size.Height - 1);

        // Clamped to the content when drawn.
        public void Scroll(int delta) => _offset = Math.Max(0, _offset + delta);

        public override void Render(Surface surface, Rect area)
        {
            if (area.IsEmpty)
            {
                return;
            }

            if (_lines is null || _size != area.Size)
            {
                _size = area.Size;
                _lines = TextRenderer.Wrap(_content, area.Width);
                _overflow = _lines.Count > area.Height && area.Width > 1;
                if (_overflow)
                {
                    _lines = TextRenderer.Wrap(_content, area.Width - 1);
                }
            }

            var lines = _lines;
            _offset = Math.Min(_offset, Math.Max(0, lines.Count - area.Height));
            int width = _overflow ? area.Width - 1 : area.Width;
            for (int row = 0; row < area.Height && _offset + row < lines.Count; row++)
            {
                TextRenderer.DrawLine(surface, area.X, area.Y + row, width, lines[_offset + row], Justify.Left);
            }

            if (_overflow)
            {
                Scrollbar.Default.Render(surface, new Rect(area.Right - 1, area.Y, 1, area.Height), _offset, area.Height, lines.Count);
            }
        }
    }

    private StyledText HintText()
    {
        if (_mismatch)
        {
            return new StyledText("that doesn't match; esc cancels", new Style(Theme.Current.Error, Color.Default));
        }

        return Typed
            ? DialogBody.KeyHints(("enter", "confirm"), ("esc", "cancel"))
            : DialogBody.KeyHints(("y/enter", "confirm"), ("n/esc", "cancel"));
    }
}
