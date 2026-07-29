using System;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// A single-line bar (top or bottom) with left/center/right <see cref="StyledText"/> segments
/// over a filled background. Defaults to a themed accent background.
/// </summary>
public sealed class StatusBar : Widget
{
    public StyledText Left { get; set; } = StyledText.Empty();
    public StyledText Center { get; set; } = StyledText.Empty();
    public StyledText Right { get; set; } = StyledText.Empty();

    /// <summary>Bar style. Null uses the theme accent background with selection foreground.</summary>
    public Style? Style { get; set; }

    private Style EffectiveStyle =>
        Style ?? new Style(Theme.Current.SelectionForeground, Theme.Current.Accent);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        var style = EffectiveStyle;
        surface.FillRect(new Rect(area.X, area.Y, area.Width, 1), style);

        // Spans that don't set their own background inherit the bar's background, so the
        // bar reads as one continuous strip.
        DrawSegment(surface, area, Left, style, Justify.Left);
        DrawSegment(surface, area, Center, style, Justify.Center);
        DrawSegment(surface, area, Right, style, Justify.Right);
    }

    private static void DrawSegment(Surface surface, Rect area, StyledText text, Style barStyle, Justify justify)
    {
        if (text.Width == 0)
        {
            return;
        }

        var withBg = ApplyDefaultBackground(text, barStyle.Background);
        int w = withBg.Width;
        int x = justify switch
        {
            Justify.Right => area.X + Math.Max(0, area.Width - w),
            Justify.Center => area.X + Math.Max(0, (area.Width - w) / 2),
            _ => area.X,
        };
        surface.SetClip(new Rect(x, area.Y, Math.Min(w, area.Right - x), 1));
        TextRenderer.DrawLine(surface, x, area.Y, area.Width, withBg, Justify.Left);
        surface.ResetClip();
    }

    // Gives spans using the default background the bar's background, so plain segments pick up
    // the bar color while explicitly-styled shortcuts keep their own.
    private static StyledText ApplyDefaultBackground(StyledText text, Color barBg)
    {
        var result = StyledText.Empty();
        foreach (var span in text.Spans)
        {
            var s = span.Style;
            if (s.Background.IsDefault)
            {
                s = s.WithBackground(barBg);
            }

            result.Append(span.Text, s);
        }
        return result;
    }
}
