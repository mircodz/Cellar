using System;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Text;
using Tessera.Theming;

namespace Tessera.Widgets;

/// <summary>
/// A named set of spinner animation frames (each usually one cell). Built-in sets cover the
/// common terminal spinner looks.
/// </summary>
public sealed class SpinnerFrames
{
    public string[] Frames { get; }

    public SpinnerFrames(params string[] frames) => Frames = frames;

    /// <summary>The classic braille dot spinner (smooth, single-cell).</summary>
    public static SpinnerFrames Dots { get; } =
        new("⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏");

    /// <summary>A rotating ASCII line — works on any terminal.</summary>
    public static SpinnerFrames Line { get; } = new("|", "/", "-", "\\");

    /// <summary>A rotating arc.</summary>
    public static SpinnerFrames Arc { get; } =
        new("◜", "◠", "◝", "◞", "◡", "◟");

    /// <summary>A bouncing braille dot.</summary>
    public static SpinnerFrames Bounce { get; } =
        new("⠁", "⠂", "⠄", "⠂");

    /// <summary>A rotating quarter-filled circle.</summary>
    public static SpinnerFrames Circle { get; } =
        new("◐", "◓", "◑", "◒");
}

/// <summary>
/// An animated spinner for indeterminate work. Cycles a <see cref="SpinnerFrames"/> set; call
/// <see cref="Advance"/> once per tick. An optional label is drawn to the right.
/// </summary>
public sealed class Spinner : Widget
{
    private int _frame;

    // Tick of the last auto-advance, so Interval throttles the glyph step without a timer/thread.
    private long _lastStepTicks;

    // Cached label render text + the inputs it was built from (see Render).
    private StyledText? _labelCache;
    private string? _cachedLabel;
    private Color _cachedLabelFg;

    public SpinnerFrames Frames { get; set; }

    /// <summary>Optional label shown after the spinner glyph (e.g. "Scanning heap…").</summary>
    public string? Label { get; set; }

    /// <summary>Spinner glyph color. Null uses the theme accent.</summary>
    public Color? Color { get; set; }

    /// <summary>When true, the spinner drives its own animation through Repaint, so the caller
    /// doesn't have to call <see cref="Advance"/>. It only advances and requests a repaint from
    /// inside <see cref="Render"/>, so a spinner that's off screen (e.g. on an inactive tab) does
    /// nothing and runs no timer. Off by default, which keeps the manual, caller-pumped behavior.</summary>
    public bool AutoAnimate { get; set; }

    /// <summary>Minimum wall-clock between auto-advance frame steps. The render loop is frame-capped
    /// (~60fps); this throttles the glyph so it spins at a readable rate independent of frame rate.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(80);

    public Spinner(SpinnerFrames? frames = null, string? label = null)
    {
        Frames = frames ?? SpinnerFrames.Dots;
        Label = label;
    }

    /// <summary>Advances to the next animation frame (call once per tick).</summary>
    public void Advance()
    {
        if (Frames.Frames.Length == 0)
        {
            return;
        }

        _frame = (_frame + 1) % Frames.Frames.Length;
    }

    /// <summary>Resets to the first frame.</summary>
    public void Reset() => _frame = 0;

    /// <summary>The current frame glyph.</summary>
    public string CurrentFrame =>
        Frames.Frames.Length == 0 ? " " : Frames.Frames[_frame % Frames.Frames.Length];

    public override Size Measure(Size available) => new(available.Width, 1);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        // Self-driven animation: advance on a wall-clock throttle, then ask the app for the next
        // coalesced frame. No timer/thread — the frame loop re-renders us and we request again.
        // Gated purely on being rendered: a hidden spinner isn't drawn, so it requests nothing and
        // stays completely idle. Repaint.Request is a no-op when no app is running (e.g. offscreen).
        if (AutoAnimate)
        {
            long now = Environment.TickCount64;
            if (now - _lastStepTicks >= (long)Interval.TotalMilliseconds)
            {
                Advance();
                _lastStepTicks = now;
            }
            Repaint.Request();
        }

        var theme = Theme.Current;
        var glyphStyle = new Style(Color ?? theme.Accent, Primitives.Color.Default);

        int x = surface.DrawText(area.X, area.Y, CurrentFrame, glyphStyle);

        if (!string.IsNullOrEmpty(Label))
        {
            x = surface.DrawText(x, area.Y, " ", glyphStyle);
            var remaining = area.Right - x;
            if (remaining > 0)
            {
                // Cache the label's StyledText so the steady state (a spinning glyph) allocates
                // nothing per frame — rebuild only when the label text or theme foreground changes.
                if (_labelCache is null ||
                    !ReferenceEquals(_cachedLabel, Label) ||
                    !_cachedLabelFg.Equals(theme.Foreground))
                {
                    _cachedLabel = Label;
                    _cachedLabelFg = theme.Foreground;
                    _labelCache = new StyledText(Label, new Style(theme.Foreground, Primitives.Color.Default));
                }

                surface.SetClip(new Rect(x, area.Y, remaining, 1));
                TextRenderer.DrawLine(surface, x, area.Y, remaining, _labelCache, Justify.Left);
                surface.ResetClip();
            }
        }
    }
}
