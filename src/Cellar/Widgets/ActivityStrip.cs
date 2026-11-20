using System;
using System.Collections.Generic;
using System.Threading;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// One line that shows an <see cref="Activity"/>: each running task with a spinner and its progress, then what
/// just finished (a check mark, or a cross and the error) for a little while. When it doesn't all fit, the rest
/// is counted ("+3"). Clicking it opens the full <see cref="ActivityList"/>. Meant for a status line or a corner
/// of the screen.
/// </summary>
/// <remarks>
/// The strip reads the activity through a snapshot it refreshes only when the activity changed, so drawing it
/// every frame costs no allocations; the spinner animates only while something runs and the strip is drawn.
/// Finished tasks drop off on their own when their time is up (a timer from <see cref="Activity.Clock"/> asks for
/// the repaint).
/// </remarks>
public sealed class ActivityStrip : Widget
{
    private const int SpinnerStepMs = 80;

    private readonly List<ActivitySnapshot> _tasks = new();
    private int _version = -1;
    private ITimer? _expiry;
    private DateTimeOffset _expiryAt = DateTimeOffset.MaxValue;
    private Rect _bounds;
    private App? _app;

    public ActivityStrip(Activity activity) => Activity = activity ?? throw new ArgumentNullException(nameof(activity));

    /// <summary>The activity shown.</summary>
    public Activity Activity { get; }

    /// <summary>How long a task that succeeded or was canceled stays on the strip. 5 seconds by default.</summary>
    public TimeSpan ShowFinishedFor { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a task that failed stays on the strip. 30 seconds by default; the list keeps it longer.</summary>
    public TimeSpan ShowFailedFor { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Drawn (dimmed) when there's nothing to show; null draws nothing.</summary>
    public string? IdleText { get; set; }

    /// <summary>Called when the strip is clicked. Null (the default) opens an <see cref="ActivityList"/> overlay
    /// in the app the strip is mounted in.</summary>
    public Action? OnClick { get; set; }

    public override Size Measure(Size available) => new(available.Width, 1);

    protected override void OnMount(App app) => _app = app;

    protected override void OnUnmount()
    {
        _app = null;
        _expiry?.Dispose();
        _expiry = null;
        _expiryAt = DateTimeOffset.MaxValue;
    }

    public override bool OnEvent(InputEvent e)
    {
        if (e is MouseEvent { Kind: MouseEventKind.Down, Button: MouseButton.Left } click && _bounds.Contains(click.X, click.Y))
        {
            if (OnClick is { } onClick)
            {
                onClick();
                return true;
            }

            if (_app is { } app)
            {
                ActivityList.Show(app, Activity);
                return true;
            }
        }

        return false;
    }

    public override void Render(Surface surface, Rect area)
    {
        _bounds = area;
        if (area.IsEmpty)
        {
            return;
        }

        if (_version != Activity.Version)
        {
            _version = Activity.CopyTo(_tasks);
        }

        var theme = Theme.Current;
        var now = Activity.Clock.GetUtcNow();
        int x = area.X;
        int shown = 0;
        int hidden = 0;
        bool running = false;
        var nextExpiry = DateTimeOffset.MaxValue;

        // Running tasks first, oldest first; then finished ones, newest first.
        for (int i = 0; i < _tasks.Count; i++)
        {
            var task = _tasks[i];
            if (!task.IsRunning)
            {
                continue;
            }

            running = true;
            if (hidden > 0 || !Draw(surface, area, ref x, task, theme, shown))
            {
                hidden++;
            }
            else
            {
                shown++;
            }
        }

        for (int i = _tasks.Count - 1; i >= 0; i--)
        {
            var task = _tasks[i];
            if (task.IsRunning || task.Ended is not { } ended)
            {
                continue;
            }

            var until = ended + (task.State == ActivityState.Failed ? ShowFailedFor : ShowFinishedFor);
            if (until <= now)
            {
                continue;
            }

            if (until < nextExpiry)
            {
                nextExpiry = until;
            }

            if (hidden > 0 || !Draw(surface, area, ref x, task, theme, shown))
            {
                hidden++;
            }
            else
            {
                shown++;
            }
        }

        if (hidden > 0)
        {
            DrawMore(surface, area, x, hidden, theme);
        }
        else if (shown == 0 && IdleText is { } idle)
        {
            surface.DrawText(area.X, area.Y, idle, theme.MutedStyle);
        }

        if (running)
        {
            Repaint.Request(); // keep the spinner turning while it's on screen
        }

        ScheduleExpiry(nextExpiry, now);
    }

    // Draws one task at x if it fits along with room for a "+N" after it; returns false (drawing nothing) if not.
    private static bool Draw(Surface surface, Rect area, ref int x, in ActivitySnapshot task, Theme theme, int shown)
    {
        Span<char> percent = stackalloc char[5];
        int percentLength = 0;
        if (task.IsRunning && task.Progress is { } progress && ((int)(progress * 100)).TryFormat(percent, out int digits))
        {
            percent[digits] = '%';
            percentLength = digits + 1;
        }

        string? detail = task.State == ActivityState.Failed ? task.Message : null;
        int gap = shown > 0 ? 3 : 0;
        int width = gap + 2 + Unicode.StringWidth(task.Title) + (percentLength > 0 ? percentLength + 1 : 0)
            + (detail is null ? 0 : 2 + Unicode.StringWidth(detail));
        const int moreRoom = 4; // "  +9"
        if (x + width > area.Right - (shown > 0 ? moreRoom : 0) && shown > 0)
        {
            return false;
        }

        var clip = surface.Clip;
        surface.SetClip(area.Intersect(clip));
        if (gap > 0)
        {
            x = surface.DrawText(x, area.Y, " · ", theme.MutedStyle);
        }

        x = surface.DrawText(x, area.Y, Glyph(task.State), GlyphStyle(task.State, theme));
        x = surface.DrawText(x, area.Y, " ", theme.TextStyle);
        x = surface.DrawText(x, area.Y, task.Title, task.State == ActivityState.Running ? theme.TextStyle : theme.MutedStyle);
        if (percentLength > 0)
        {
            x = surface.DrawText(x + 1, area.Y, percent[..percentLength], theme.AccentStyle);
        }

        if (detail is not null)
        {
            x = surface.DrawText(x, area.Y, ": ", theme.MutedStyle);
            x = surface.DrawText(x, area.Y, detail, new Style(theme.Error, Color.Default));
        }

        surface.SetClip(clip);
        x = Math.Min(x, area.Right);
        return true;
    }

    private static void DrawMore(Surface surface, Rect area, int x, int hidden, Theme theme)
    {
        Span<char> more = stackalloc char[16];
        more[0] = ' ';
        more[1] = ' ';
        more[2] = '+';
        if (hidden.TryFormat(more[3..], out int digits))
        {
            int length = 3 + digits;
            surface.DrawText(Math.Max(area.X, Math.Min(x, area.Right - length)), area.Y, more[..length], theme.MutedStyle);
        }
    }

    internal static string Glyph(ActivityState state) => state switch
    {
        ActivityState.Running => SpinnerFrames.Dots.Frames[(int)(Environment.TickCount64 / SpinnerStepMs % SpinnerFrames.Dots.Frames.Length)],
        ActivityState.Succeeded => "✓",
        ActivityState.Failed => "✗",
        _ => "-",
    };

    internal static Style GlyphStyle(ActivityState state, Theme theme) => state switch
    {
        ActivityState.Running => theme.AccentStyle,
        ActivityState.Succeeded => new Style(theme.Success, Color.Default),
        ActivityState.Failed => new Style(theme.Error, Color.Default),
        _ => theme.MutedStyle,
    };

    // One timer, moved to the next time a finished task should drop off, so the strip redraws then even if
    // nothing else happens.
    private void ScheduleExpiry(DateTimeOffset at, DateTimeOffset now)
    {
        if (at == DateTimeOffset.MaxValue || at == _expiryAt || _app is null)
        {
            return;
        }

        _expiryAt = at;
        var due = at - now + TimeSpan.FromMilliseconds(20);
        if (_expiry is null)
        {
            _expiry = Activity.Clock.CreateTimer(static _ => Repaint.Request(), null, due, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _expiry.Change(due, Timeout.InfiniteTimeSpan);
        }
    }
}

/// <summary>
/// Every task of an <see cref="Activity"/>, one per row: running ones first with their progress, then finished
/// ones, newest first, with their outcome, message and how long they took. <c>↑</c>/<c>↓</c> move, <c>c</c>
/// clears the finished ones. <see cref="Show"/> opens it as an overlay that esc closes.
/// </summary>
public sealed class ActivityList : Widget
{
    private readonly List<ActivitySnapshot> _tasks = new();
    private readonly List<int> _order = new(); // indices into _tasks in display order
    private int _version = -1;
    private int _selected;
    private int _top;
    private int _height = 1;

    public ActivityList(Activity activity) => Activity = activity ?? throw new ArgumentNullException(nameof(activity));

    /// <summary>The activity shown.</summary>
    public Activity Activity { get; }

    /// <summary>Drawn when there are no tasks.</summary>
    public string EmptyText { get; set; } = "Nothing has run yet.";

    /// <summary>The selected row.</summary>
    public int SelectedIndex => _selected;

    public override bool IsFocusable => true;

    /// <summary>Opens the list of <paramref name="activity"/> in a modal overlay; esc closes it.</summary>
    public static ActivityList Show(App app, Activity activity, int width = 80, int height = 16)
    {
        ArgumentNullException.ThrowIfNull(app);
        var list = new ActivityList(activity);
        var body = new DialogBody(list)
        {
            Hints = DialogBody.KeyHints(("↑/↓", "move"), ("c", "clear finished"), ("esc", "close")),
        };
        app.PushOverlay(new Overlay(new Panel(body, "activity"))
        {
            Width = Math.Min(width, Math.Max(20, app.Size.Width - 4)),
            Height = Math.Min(height, Math.Max(5, app.Size.Height - 2)),
            ScrimOpacity = 0.4,
        });
        return list;
    }

    public override bool OnEvent(InputEvent e)
    {
        if (!HasFocus || e is not KeyEvent key || (key.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0)
        {
            return false;
        }

        Refresh();
        switch (key.Key)
        {
            case Key.Up: Move(-1); return true;
            case Key.Down: Move(1); return true;
            case Key.PageUp: Move(-_height); return true;
            case Key.PageDown: Move(_height); return true;
            case Key.Home: Move(int.MinValue / 2); return true;
            case Key.End: Move(int.MaxValue / 2); return true;
            case Key.Char when key.Rune.Value is 'k': Move(-1); return true;
            case Key.Char when key.Rune.Value is 'j': Move(1); return true;
            case Key.Char when key.Rune.Value is 'c':
                Activity.ClearFinished();
                Refresh();
                Move(0);
                return true;
        }

        return false;
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        Refresh();
        _height = area.Height;
        var theme = Theme.Current;
        if (_order.Count == 0)
        {
            surface.DrawText(area.X, area.Y, EmptyText, theme.MutedStyle);
            return;
        }

        Move(0);
        var now = Activity.Clock.GetUtcNow();
        bool running = false;
        Span<char> right = stackalloc char[24];
        var clip = surface.Clip;
        for (int row = 0; row < area.Height && _top + row < _order.Count; row++)
        {
            int index = _top + row;
            var task = _tasks[_order[index]];
            running |= task.IsRunning;
            int y = area.Y + row;
            bool selected = index == _selected && HasFocus;
            if (selected)
            {
                surface.FillRect(new Rect(area.X, y, area.Width, 1), theme.SelectionStyle);
            }

            int length = FormatRight(right, task, now);
            int rightX = Math.Max(area.X, area.Right - length);
            surface.SetClip(new Rect(area.X, y, Math.Max(0, rightX - 1 - area.X), 1).Intersect(clip));
            int x = surface.DrawText(area.X, y, ActivityStrip.Glyph(task.State), Selected(ActivityStrip.GlyphStyle(task.State, theme), selected, theme));
            x = surface.DrawText(x + 1, y, task.Title, Selected(theme.TextStyle, selected, theme));
            if (task.Message is { } message)
            {
                var messageStyle = task.State == ActivityState.Failed ? new Style(theme.Error, Color.Default) : theme.MutedStyle;
                surface.DrawText(x + 2, y, message, Selected(messageStyle, selected, theme));
            }

            surface.SetClip(clip);
            surface.DrawText(rightX, y, right[..length], Selected(theme.MutedStyle, selected, theme));
        }

        if (_order.Count > area.Height)
        {
            Scrollbar.Default.Render(surface, new Rect(area.Right - 1, area.Y, 1, area.Height), _top, area.Height, _order.Count);
        }

        if (running)
        {
            Repaint.Request();
        }
    }

    private static Style Selected(Style style, bool selected, Theme theme) =>
        selected ? style.WithBackground(theme.SelectionBackground) : style;

    // "42%  1m05s" for a running task with progress, "1m05s" otherwise.
    private static int FormatRight(Span<char> buffer, in ActivitySnapshot task, DateTimeOffset now)
    {
        int length = 0;
        if (task.IsRunning && task.Progress is { } progress && ((int)(progress * 100)).TryFormat(buffer, out int digits))
        {
            buffer[digits] = '%';
            buffer[digits + 1] = ' ';
            buffer[digits + 2] = ' ';
            length = digits + 3;
        }

        return length + FormatElapsed(buffer[length..], task.Elapsed(now));
    }

    /// <summary>"850ms", "12s", "3m05s", "1h02m": short and readable at a glance.</summary>
    private static int FormatElapsed(Span<char> buffer, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        int written;
        if (elapsed.TotalSeconds < 1)
        {
            ((int)elapsed.TotalMilliseconds).TryFormat(buffer, out written);
            buffer[written++] = 'm';
            buffer[written++] = 's';
        }
        else if (elapsed.TotalMinutes < 1)
        {
            elapsed.Seconds.TryFormat(buffer, out written);
            buffer[written++] = 's';
        }
        else if (elapsed.TotalHours < 1)
        {
            elapsed.Minutes.TryFormat(buffer, out written);
            buffer[written++] = 'm';
            elapsed.Seconds.TryFormat(buffer[written..], out int seconds, "00");
            written += seconds;
            buffer[written++] = 's';
        }
        else
        {
            ((int)elapsed.TotalHours).TryFormat(buffer, out written);
            buffer[written++] = 'h';
            elapsed.Minutes.TryFormat(buffer[written..], out int minutes, "00");
            written += minutes;
            buffer[written++] = 'm';
        }

        return written;
    }

    private void Refresh()
    {
        if (_version == Activity.Version)
        {
            return;
        }

        _version = Activity.CopyTo(_tasks);
        _order.Clear();
        for (int i = 0; i < _tasks.Count; i++)
        {
            if (_tasks[i].IsRunning)
            {
                _order.Add(i);
            }
        }

        int firstFinished = _order.Count;
        for (int i = 0; i < _tasks.Count; i++)
        {
            if (!_tasks[i].IsRunning)
            {
                _order.Add(i);
            }
        }

        // Finished ones newest first: sort that tail by end time, descending.
        _order.Sort(firstFinished, _order.Count - firstFinished, Comparer<int>.Create(NewestFirst));
    }

    private int NewestFirst(int a, int b) => Nullable.Compare(_tasks[b].Ended, _tasks[a].Ended) is var c and not 0 ? c : b.CompareTo(a);

    private void Move(int delta)
    {
        _selected = _order.Count == 0 ? 0 : (int)Math.Clamp((long)_selected + delta, 0, _order.Count - 1);
        if (_selected < _top)
        {
            _top = _selected;
        }
        else if (_selected >= _top + _height)
        {
            _top = _selected - _height + 1;
        }

        _top = Math.Clamp(_top, 0, Math.Max(0, _order.Count - _height));
    }
}
