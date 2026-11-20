using System;
using System.Collections.Generic;
using System.Threading;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>Which end of a <see cref="StatusLine"/> an item sits at.</summary>
public enum StatusAlignment
{
    Left,
    Right,
}

/// <summary>How a <see cref="StatusLine"/> message is colored, and whether it expires on its own.</summary>
public enum StatusLevel
{
    Info,
    Success,
    Warning,

    /// <summary>Stays until dismissed: an error the user didn't see is an error they can't act on.</summary>
    Error,
}

/// <summary>A message shown by <see cref="StatusLine.Show(StyledText, StatusLevel, TimeSpan?)"/>.</summary>
/// <param name="Expires">When it disappears on its own, or null when it stays until dismissed.</param>
public sealed record StatusMessage(StyledText Text, StatusLevel Level, DateTimeOffset? Expires);

/// <summary>
/// One piece of a <see cref="StatusLine"/>: a count, a mode, a key hint. Items carry a priority so the line knows
/// what to give up first when the terminal is narrow, and an optional short form to fall back to before that.
/// Subclass and override <see cref="GetText"/> for content computed at render time.
/// </summary>
public class StatusItem
{
    public StatusItem(StyledText text, StatusAlignment alignment = StatusAlignment.Left, int priority = 0)
    {
        Text = text;
        Alignment = alignment;
        Priority = priority;
    }

    public StyledText Text { get; set; }

    /// <summary>What to show when space is short, e.g. "^r" for "^r refresh". Null keeps <see cref="Text"/>.</summary>
    public StyledText? ShortText { get; set; }

    public StatusAlignment Alignment { get; set; }

    /// <summary>Higher stays longer. When the line is too narrow, the lowest-priority items are shortened and
    /// then dropped first.</summary>
    public int Priority { get; set; }

    /// <summary>Runs when the item is clicked. Clickable items highlight under the mouse.</summary>
    public Action? OnClick { get; set; }

    public bool Visible { get; set; } = true;

    /// <summary>Whether the line should lay the item out now.</summary>
    public virtual bool IsShown => Visible;

    /// <summary>The text to draw, long or short form. Called on every render, so keep it allocation-free.</summary>
    public virtual StyledText GetText(bool shortForm) => shortForm && ShortText is { } s ? s : Text;

    // Whether the item changes on its own, so the line asks for another frame while it's drawn.
    internal virtual bool Animates => false;

    /// <summary>A mode badge: the text in bold on an accent background, e.g. " INSERT ".</summary>
    public static StatusItem Badge(string text, Color? background = null, StatusAlignment alignment = StatusAlignment.Left, int priority = 1000)
    {
        var theme = Theme.Current;
        var style = new Style(theme.SelectionForeground, background ?? theme.Accent).Bold;
        return new StatusItem(new StyledText(" " + text + " ", style), alignment, priority);
    }
}

/// <summary>
/// A <see cref="StatusItem"/> with an animated spinner glyph, shown only while <see cref="Active"/>. Animates
/// itself the way <see cref="Spinner.AutoAnimate"/> does: only while it's drawn, with no timer.
/// </summary>
public sealed class SpinnerStatusItem : StatusItem
{
    private StyledText[]? _frames;
    private StyledText[]? _shortFrames;
    private string? _builtLabel;
    private SpinnerFrames? _builtFrames;
    private Color _builtColor;
    private int _frame;
    private long _lastStepTicks;

    public SpinnerStatusItem(string label = "", StatusAlignment alignment = StatusAlignment.Left, int priority = 0)
        : base(StyledText.Empty(), alignment, priority) => Label = label;

    /// <summary>Text after the glyph, e.g. "loading…". Dropped in the short form.</summary>
    public string Label { get; set; }

    public SpinnerFrames Frames { get; set; } = SpinnerFrames.Dots;

    /// <summary>Whether the work is running; the item hides when it isn't.</summary>
    public bool Active { get; set; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(80);

    public override bool IsShown => Visible && Active;

    internal override bool Animates => true;

    public override StyledText GetText(bool shortForm)
    {
        var frames = Frames.Frames;
        if (frames.Length == 0)
        {
            return StyledText.Empty();
        }

        var accent = Theme.Current.Accent;
        if (_frames is null || !ReferenceEquals(_builtLabel, Label) || !ReferenceEquals(_builtFrames, Frames) || !_builtColor.Equals(accent))
        {
            _builtLabel = Label;
            _builtFrames = Frames;
            _builtColor = accent;
            _frames = new StyledText[frames.Length];
            _shortFrames = new StyledText[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                _shortFrames[i] = new StyledText(frames[i], new Style(accent, Color.Default));
                _frames[i] = string.IsNullOrEmpty(Label)
                    ? _shortFrames[i]
                    : new StyledText(frames[i], new Style(accent, Color.Default)).Append(" " + Label);
            }
        }

        long now = Environment.TickCount64;
        if (now - _lastStepTicks >= (long)Interval.TotalMilliseconds)
        {
            _frame = (_frame + 1) % frames.Length;
            _lastStepTicks = now;
        }

        int index = _frame % frames.Length;
        return shortForm ? _shortFrames![index] : _frames[index];
    }
}

/// <summary>
/// The bottom line of a keyboard-driven app: items (counts, mode, key hints) aligned left and right, which give way by
/// priority when the terminal is narrow, and transient messages that take the line over for a moment.
/// Unlike <see cref="StatusBar"/>'s three fixed strings, items are independent, clickable, and drop out
/// gracefully.
/// </summary>
/// <remarks>
/// Fitting: if everything fits, everything is shown in full. Otherwise, if shortening items is enough, the
/// lowest-priority items switch to their <see cref="StatusItem.ShortText"/> one at a time until the line fits;
/// if it isn't enough, the lowest-priority item is dropped and fitting starts over. Ties go to the item added
/// later, so the earlier-declared item survives.
/// </remarks>
public sealed class StatusLine : Widget
{
    private readonly List<StatusItem> _items = new();
    private readonly List<StatusItem> _hintItems = new();
    private readonly List<AppAction> _footer = new();
    private readonly List<KeySequence> _footerKeys = new();
    private readonly List<AppAction> _builtFooter = new();
    private readonly List<KeySequence> _builtKeys = new();
    private int _builtVersion = -1;
    private Theme? _builtTheme;
    private Keymap? _keymap;

    // Per-render layout buffers, reused so a steady frame allocates nothing.
    private readonly List<StatusItem> _shown = new();
    private readonly List<int> _widths = new();
    private readonly List<int> _shortWidths = new();
    private readonly List<byte> _state = new(); // 0 full, 1 short, 2 dropped
    private readonly List<(int Start, int End, StatusItem Item)> _hits = new();

    private StatusMessage? _message;
    private ITimer? _expiryTimer;
    private Rect _bounds;
    private StatusItem? _hovered;
    private StatusItem? _pressed;
    private bool _messagePressed;

    /// <summary>The app's items, in order. Hints from <see cref="Keymap"/> come after these.</summary>
    public IReadOnlyList<StatusItem> Items => _items;

    /// <summary>The line's base style. Null uses the theme foreground on the default background, so the line reads
    /// like part of the screen rather than a bar; set e.g. an accent background for a bar look.</summary>
    public Style? Style { get; set; }

    /// <summary>Blank cells between items.</summary>
    public int Gap { get; set; } = 2;

    /// <summary>How long an info, success or warning message stays when <see cref="Show(StyledText, StatusLevel, TimeSpan?)"/>
    /// isn't given a duration. Pass <see cref="Timeout.InfiniteTimeSpan"/> as a duration to keep a message.</summary>
    public TimeSpan MessageDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Shown at the right end while an error is displayed, e.g. "esc dismiss" or "! details".</summary>
    public StyledText? ErrorHint { get; set; }

    /// <summary>Runs when the user clicks a message. Null dismisses it.</summary>
    public Action<StatusMessage>? OnMessageClick { get; set; }

    /// <summary>The clock messages expire by; replace it in tests.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>When set, the line shows a right-aligned hint for every action in
    /// <see cref="Widgets.Keymap.FooterActions"/>, e.g. "^r refresh", in the actions' priority order. Hints
    /// use the action's <see cref="AppAction.Priority"/>, so give your own items a higher priority to keep them
    /// when space runs out. Clicking a hint runs the action.</summary>
    public Keymap? Keymap
    {
        get => _keymap;
        set
        {
            _keymap = value;
            _builtFooter.Clear();
            _builtKeys.Clear();
            _hintItems.Clear();
            _builtVersion = -1;
        }
    }

    /// <summary>The message on the line, or null. Reading it drops an expired message.</summary>
    public StatusMessage? Message
    {
        get
        {
            if (_message is { Expires: { } at } && Clock.GetUtcNow() >= at)
            {
                Dismiss();
            }

            return _message;
        }
    }

    public StatusItem Add(StatusItem item)
    {
        _items.Add(item);
        return item;
    }

    /// <summary>Adds a plain text item.</summary>
    public StatusItem Add(StyledText text, StatusAlignment alignment = StatusAlignment.Left, int priority = 0) =>
        Add(new StatusItem(text, alignment, priority));

    public bool Remove(StatusItem item) => _items.Remove(item);

    public void Clear() => _items.Clear();

    /// <summary>Shows a message over the whole line. Errors stay until <see cref="Dismiss"/>; anything else
    /// disappears after <paramref name="duration"/> (default <see cref="MessageDuration"/>). A new message
    /// replaces the current one.</summary>
    public void Show(StyledText text, StatusLevel level = StatusLevel.Info, TimeSpan? duration = null)
    {
        _expiryTimer?.Dispose();
        _expiryTimer = null;

        TimeSpan? lifetime = duration ?? (level == StatusLevel.Error ? null : MessageDuration);
        if (lifetime == Timeout.InfiniteTimeSpan)
        {
            lifetime = null;
        }

        _message = new StatusMessage(text, level, lifetime is { } d ? Clock.GetUtcNow() + d : null);
        if (lifetime is { } wait && wait > TimeSpan.Zero)
        {
            // Wake the frame loop when the message is due to go, so it disappears without waiting for input.
            _expiryTimer = Clock.CreateTimer(static _ => Repaint.Request(), null, wait, Timeout.InfiniteTimeSpan);
        }

        Repaint.Request();
    }

    /// <summary>Shows an error until it's dismissed.</summary>
    public void ShowError(StyledText text) => Show(text, StatusLevel.Error);

    /// <summary>Removes the current message, if any.</summary>
    public void Dismiss()
    {
        _expiryTimer?.Dispose();
        _expiryTimer = null;
        _message = null;
    }

    public override Size Measure(Size available) => new(available.Width, Math.Min(1, available.Height));

    public override void Render(Surface surface, Rect area)
    {
        _bounds = area.IsEmpty ? area : new Rect(area.X, area.Y, area.Width, 1);
        _hits.Clear();
        if (area.IsEmpty)
        {
            return;
        }

        var theme = Theme.Current;
        var lineStyle = Style ?? new Style(theme.Foreground, Color.Default);
        surface.FillRect(_bounds, lineStyle);

        var message = Message;
        if (message is not null)
        {
            RenderMessage(surface, message, lineStyle, theme);
            return;
        }

        RefreshHints();
        Layout(area.Width);

        int leftX = area.X;
        int rightWidth = 0;
        int rightCount = 0;
        for (int i = 0; i < _shown.Count; i++)
        {
            if (_state[i] != 2 && _shown[i].Alignment == StatusAlignment.Right)
            {
                rightWidth += Width(i);
                rightCount++;
            }
        }

        int rightX = area.Right - rightWidth - Math.Max(0, rightCount - 1) * Gap;
        for (int i = 0; i < _shown.Count; i++)
        {
            if (_state[i] == 2)
            {
                continue;
            }

            var item = _shown[i];
            var text = item.GetText(_state[i] == 1);
            int w = Width(i);
            if (item.Animates)
            {
                Repaint.Request(); // only for an item that survived layout: a dropped spinner doesn't keep frames coming
            }

            if (item.Alignment == StatusAlignment.Left)
            {
                Draw(surface, leftX, area.Y, area.Right, text, item, lineStyle, theme);
                _hits.Add((leftX, leftX + w, item));
                leftX += w + Gap;
            }
            else
            {
                Draw(surface, Math.Max(rightX, area.X), area.Y, area.Right, text, item, lineStyle, theme);
                _hits.Add((rightX, rightX + w, item));
                rightX += w + Gap;
            }
        }
    }

    private int Width(int i) => _state[i] == 1 ? _shortWidths[i] : _widths[i];

    private void RenderMessage(Surface surface, StatusMessage message, Style lineStyle, Theme theme)
    {
        var (prefix, color) = message.Level switch
        {
            StatusLevel.Error => ("✗ ", theme.Error),
            StatusLevel.Warning => ("! ", theme.Warning),
            StatusLevel.Success => ("✓ ", theme.Success),
            _ => ("", theme.Info),
        };

        var style = lineStyle.WithForeground(color);
        int right = _bounds.Right;
        if (message.Level == StatusLevel.Error && ErrorHint is { } hint)
        {
            int hw = hint.Width;
            if (hw + 4 < _bounds.Width)
            {
                right -= hw + Gap;
                DrawSpans(surface, right + Gap, _bounds.Y, _bounds.Right, hint, lineStyle.WithForeground(theme.Muted));
            }
        }

        surface.SetClip(new Rect(_bounds.X, _bounds.Y, Math.Max(0, right - _bounds.X), 1));
        int x = surface.DrawText(_bounds.X, _bounds.Y, prefix, style.Bold);
        DrawSpans(surface, x, _bounds.Y, right, message.Text, style);
        surface.ResetClip();
    }

    private void Draw(Surface surface, int x, int y, int right, StyledText text, StatusItem item, Style lineStyle, Theme theme)
    {
        if (item.OnClick is not null && ReferenceEquals(item, _hovered))
        {
            int w = Math.Min(text.Width, right - x);
            if (w > 0)
            {
                lineStyle = lineStyle.WithBackground(theme.SelectionBackground).WithForeground(theme.SelectionForeground);
                surface.FillRect(new Rect(x, y, w, 1), lineStyle);
            }
        }

        DrawSpans(surface, x, y, right, text, lineStyle);
    }

    // Draws spans over the line, giving spans without a color of their own the line's, without building a new
    // StyledText per frame.
    private static void DrawSpans(Surface surface, int x, int y, int right, StyledText text, Style lineStyle)
    {
        var clip = surface.Clip;
        surface.SetClip(new Rect(x, y, Math.Max(0, Math.Min(right, clip.Right) - x), 1).Intersect(clip));
        var spans = text.Spans;
        for (int i = 0; i < spans.Count && x < right; i++)
        {
            var style = spans[i].Style;
            if (style.Foreground.IsDefault)
            {
                style = style.WithForeground(lineStyle.Foreground);
            }

            if (style.Background.IsDefault)
            {
                style = style.WithBackground(lineStyle.Background);
            }

            x = surface.DrawText(x, y, spans[i].Text, style);
        }

        surface.SetClip(clip);
    }

    private void RefreshHints()
    {
        if (_keymap is null)
        {
            if (_hintItems.Count > 0)
            {
                _hintItems.Clear();
            }

            return;
        }

        // The hints bake in the keys that run each action now (which depend on the active scopes) and the theme's
        // colors, so either changing rebuilds them.
        _keymap.CollectFooter(_footer, _footerKeys);
        var theme = Theme.Current;
        if (_builtVersion == _keymap.Version && ReferenceEquals(_builtTheme, theme)
            && Same(_footer, _builtFooter) && Same(_footerKeys, _builtKeys))
        {
            return;
        }

        _builtVersion = _keymap.Version;
        _builtTheme = theme;
        _builtFooter.Clear();
        _builtFooter.AddRange(_footer);
        _builtKeys.Clear();
        _builtKeys.AddRange(_footerKeys);
        _hintItems.Clear();
        var keyStyle = new Style(theme.Accent, Color.Default).Bold;
        var labelStyle = new Style(theme.Muted, Color.Default);
        for (int i = 0; i < _footer.Count; i++)
        {
            var action = _footer[i];
            var key = _footerKeys[i];
            string keyText = key.ToShortString();
            var captured = action;
            _hintItems.Add(new StatusItem(
                new StyledText(keyText, keyStyle).Append(" " + action.Footer, labelStyle),
                StatusAlignment.Right,
                action.Priority)
            {
                ShortText = new StyledText(keyText, keyStyle),
                OnClick = () => captured.Run(null),
            });
        }
    }

    private static bool Same<T>(List<T> a, List<T> b)
        where T : class
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void Layout(int width)
    {
        _shown.Clear();
        _widths.Clear();
        _shortWidths.Clear();
        _state.Clear();
        AddShown(_items);
        AddShown(_hintItems);

        while (true)
        {
            int full = 0, shortest = 0, count = 0;
            for (int i = 0; i < _shown.Count; i++)
            {
                if (_state[i] == 2)
                {
                    continue;
                }

                full += Width(i);
                shortest += _shortWidths[i];
                count++;
            }

            int gaps = Math.Max(0, count - 1) * Gap;
            if (full + gaps <= width || count == 0)
            {
                return;
            }

            if (shortest + gaps <= width || count == 1)
            {
                // Shortening is enough (or nothing is left to drop): shorten from the least important up.
                while (full + gaps > width && Lowest(shortenable: true) is var i and >= 0)
                {
                    full -= _widths[i] - _shortWidths[i];
                    _state[i] = 1;
                }

                return;
            }

            _state[Lowest(shortenable: false)] = 2;
        }
    }

    private void AddShown(List<StatusItem> items)
    {
        foreach (var item in items)
        {
            if (!item.IsShown)
            {
                continue;
            }

            _shown.Add(item);
            int full = item.GetText(false).Width;
            _widths.Add(full);
            _shortWidths.Add(Math.Min(full, item.GetText(true).Width));
            _state.Add(0);
        }
    }

    // The lowest-priority item still in the line (latest wins ties), or one that can still be shortened.
    private int Lowest(bool shortenable)
    {
        int best = -1;
        for (int i = 0; i < _shown.Count; i++)
        {
            bool candidate = shortenable ? _state[i] == 0 && _shortWidths[i] < _widths[i] : _state[i] != 2;
            if (candidate && (best < 0 || _shown[i].Priority <= _shown[best].Priority))
            {
                best = i;
            }
        }

        return best;
    }

    private StatusItem? HitTest(int x, int y)
    {
        if (y != _bounds.Y)
        {
            return null;
        }

        foreach (var (start, end, item) in _hits)
        {
            if (x >= start && x < end)
            {
                return item;
            }
        }

        return null;
    }

    public override bool OnEvent(InputEvent e)
    {
        if (e is not MouseEvent m)
        {
            return false;
        }

        bool inside = _bounds.Contains(m.X, m.Y);
        if (_message is not null)
        {
            switch (m)
            {
                case { Kind: MouseEventKind.Down, Button: MouseButton.Left } when inside:
                    _messagePressed = true;
                    return true;
                case { Kind: MouseEventKind.Up }:
                    bool fire = _messagePressed && inside;
                    _messagePressed = false;
                    if (fire && _message is { } msg)
                    {
                        if (OnMessageClick is { } click)
                        {
                            click(msg);
                        }
                        else
                        {
                            Dismiss();
                        }
                    }

                    return fire;
                default:
                    return false;
            }
        }

        switch (m.Kind)
        {
            case MouseEventKind.Move:
            {
                var over = HitTest(m.X, m.Y);
                if (over?.OnClick is null)
                {
                    over = null;
                }

                if (!ReferenceEquals(over, _hovered))
                {
                    _hovered = over;
                    return true;
                }

                return false;
            }

            case MouseEventKind.Down when m.Button == MouseButton.Left && inside:
                _pressed = HitTest(m.X, m.Y);
                return _pressed?.OnClick is not null;

            case MouseEventKind.Up:
            {
                var pressed = _pressed;
                _pressed = null;
                if (pressed?.OnClick is { } onClick && ReferenceEquals(HitTest(m.X, m.Y), pressed))
                {
                    onClick();
                    return true;
                }

                return false;
            }
        }

        return false;
    }
}
