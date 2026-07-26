using System;
using System.Collections.Generic;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Text;
using Tessera.Theming;

namespace Tessera.Widgets.Charts;

/// <summary>One segment of a <see cref="ProportionBar"/>: a label, a value, and a color.</summary>
public sealed class Segment
{
    public string Label { get; set; }
    public double Value { get; set; }
    public Color? Color { get; set; }

    public Segment(string label, double value, Color? color = null)
    {
        Label = label;
        Value = value;
        Color = color;
    }
}

/// <summary>
/// A segmented ("stacked") proportion bar — <c>|AAAABBBCC|</c> — filling its width with
/// colored segments sized to their share of the total (largest-remainder rounding, so they
/// sum exactly). Pair with a <see cref="Legend"/> to name the colors.
/// </summary>
public sealed class ProportionBar : Widget
{
    private static readonly IReadOnlyList<Color> DefaultPalette =
    [
        Color.Hex("#6a9fb5"), // blue
        Color.Hex("#90a959"), // green
        Color.Hex("#f4bf75"), // yellow
        Color.Hex("#aa759f"), // magenta
        Color.Hex("#ac4142"), // red
        Color.Hex("#75b5aa"), // cyan
        Color.Hex("#d28445"), // orange
        Color.Hex("#8f5536") // brown
    ];

    /// <summary>The segments, in draw order (left to right).</summary>
    public List<Segment> Segments { get; } = new();

    /// <summary>The glyph used to fill each segment. A full block by default.</summary>
    public string Glyph { get; set; } = "█";

    /// <summary>When true, segment labels are drawn inside segments wide enough to fit them.</summary>
    public bool ShowInlineLabels { get; set; }

    /// <summary>Palette used for segments without an explicit color, cycled by index.</summary>
    public IReadOnlyList<Color>? Palette { get; set; }

    public override Size Measure(Size available) => new(available.Width, 1);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty || Segments.Count == 0)
        {
            return;
        }

        int[] widths = ResolveWidths(area.Width);
        var palette = Palette ?? DefaultPalette;

        int x = area.X;
        for (int i = 0; i < Segments.Count; i++)
        {
            int w = widths[i];
            if (w <= 0)
            {
                continue;
            }

            var color = Segments[i].Color ?? palette[i % palette.Count];
            var style = new Style(color, Color.Default);

            for (int col = 0; col < w; col++)
                for (int row = area.Top; row < area.Bottom; row++)
                    surface.Set(x + col, row, Cell.FromGrapheme(Glyph, style));

            if (ShowInlineLabels && w >= Segments[i].Label.Length + 1)
            {
                // Use a contrasting foreground (the bar color as background) for legibility.
                var labelStyle = new Style(Theme.Current.Background, color);
                var text = new StyledText(" " + Segments[i].Label, labelStyle);
                surface.SetClip(new Rect(x, area.Top, w, area.Height));
                TextRenderer.DrawLine(surface, x, area.Top, w, text, Justify.Left);
                surface.ResetClip();
            }

            x += w;
        }
    }

    // Distributes the bar width across segments proportionally to their values, using
    // largest-remainder rounding so the parts sum to exactly `totalWidth` (no gaps/overflow).
    private int[] ResolveWidths(int totalWidth)
    {
        int n = Segments.Count;
        var widths = new int[n];
        if (totalWidth <= 0)
        {
            return widths;
        }

        double total = 0;
        for (int i = 0; i < n; i++) total += Math.Max(0, Segments[i].Value);
        if (total <= 0)
        {
            return widths;
        }

        // Floor each share, then hand out the leftover cells to the largest remainders.
        var remainders = new (int index, double frac)[n];
        int assigned = 0;
        for (int i = 0; i < n; i++)
        {
            double exact = Math.Max(0, Segments[i].Value) / total * totalWidth;
            int floor = (int)Math.Floor(exact);
            widths[i] = floor;
            assigned += floor;
            remainders[i] = (i, exact - floor);
        }

        int leftover = totalWidth - assigned;
        if (leftover > 0)
        {
            Array.Sort(remainders, (a, b) => b.frac.CompareTo(a.frac));
            for (int k = 0; k < leftover; k++)
            {
                widths[remainders[k % n].index]++;
            }
        }

        return widths;
    }
}
