using System;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// A full-width vertical scrollbar drawn with background colors (solid track, accent thumb),
/// using half-block caps (▀/▄) for half-cell precision. Shared by scrolling widgets. Override
/// <see cref="TrackColor"/>/<see cref="ThumbColor"/> or leave null to follow the theme.
/// </summary>
public sealed class Scrollbar
{
    /// <summary>Track background. Null uses a dark tone derived from the theme.</summary>
    public Color? TrackColor { get; init; }

    /// <summary>Thumb color. Null uses the theme accent.</summary>
    public Color? ThumbColor { get; init; }

    /// <summary>The default themed scrollbar: black-ish track, accent thumb, half-block caps.</summary>
    public static Scrollbar Default { get; } = new();

    /// <summary>Renders into a 1-column <paramref name="bar"/>: thumb size ∝ viewport/content,
    /// positioned by <paramref name="offset"/>. Nothing drawn if the content fits.</summary>
    public void Render(Surface surface, Rect bar, int offset, int viewport, int content)
    {
        if (bar.Height <= 0 || content <= viewport)
        {
            return;
        }

        var theme = Theme.Current;
        Color trackBg = TrackColor ?? TrackFromTheme(theme);
        Color thumbColor = ThumbColor ?? theme.Accent;

        int h = bar.Height;
        int units = h * 2; // half-cells for sub-cell precision

        double thumbUnitsF = (double)viewport / content * units;
        int thumbUnits = Math.Max(1, (int)Math.Round(thumbUnitsF));
        int maxOffset = Math.Max(1, content - viewport);
        int travel = units - thumbUnits;
        int start = travel <= 0 ? 0 : (int)Math.Round((double)offset / maxOffset * travel);
        int end = start + thumbUnits; // exclusive, in half-cell units

        // Styles: a filled cell is a space on the given background; half cells use a
        // half-block glyph whose foreground is the thumb over the track background.
        var trackStyle = new Style(Color.Default, trackBg);
        var thumbFull = new Style(Color.Default, thumbColor);
        var capTop = new Style(thumbColor, trackBg);    // ▀ upper half filled
        var capBottom = new Style(thumbColor, trackBg); // ▄ lower half filled

        for (int row = 0; row < h; row++)
        {
            int top = row * 2; // this cell spans half-units [top, top+2)
            bool upper = top >= start && top < end;
            bool lower = (top + 1) >= start && (top + 1) < end;

            if (upper && lower)
            {
                surface.Set(bar.X, bar.Y + row, Cell.Blank(thumbFull));
            }
            else if (upper)
            {
                surface.Set(bar.X, bar.Y + row, Cell.FromGrapheme("▀", capTop));
            }
            else if (lower)
            {
                surface.Set(bar.X, bar.Y + row, Cell.FromGrapheme("▄", capBottom));
            }
            else
            {
                surface.Set(bar.X, bar.Y + row, Cell.Blank(trackStyle));
            }
        }
    }

    // A track a touch darker than the theme background so it reads as a groove.
    private static Color TrackFromTheme(Theme theme)
    {
        if (theme.Background.IsRgb)
        {
            bool dark = theme.Background.R + theme.Background.G + theme.Background.B < 384;
            int d = dark ? -6 : 20; // slightly darker on dark themes, lighter on light
            return Color.Rgb(Color.ClampChannel(theme.Background.R + d), Color.ClampChannel(theme.Background.G + d), Color.ClampChannel(theme.Background.B + d));
        }
        return Color.Black;
    }
}
