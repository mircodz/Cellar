using System;
using System.Collections.Generic;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets.Charts;

/// <summary>
/// A horizontal bar chart: one row per labeled value, bar length proportional to the largest,
/// with sub-cell block precision. Label on the left, value on the right.
/// </summary>
public sealed class BarChart : Widget
{
    /// <summary>The (label, value) pairs to plot, top to bottom.</summary>
    public List<(string Label, double Value)> Values { get; } = new();

    /// <summary>Optional per-bar colors, matched by index. Falls back to the theme accent.</summary>
    public List<Color> BarColors { get; } = new();

    /// <summary>Width reserved for the label column. 0 auto-sizes to the longest label.</summary>
    public int LabelWidth { get; set; }

    /// <summary>When set, the axis scales to this maximum instead of the largest value.</summary>
    public double? MaxValue { get; set; }

    private static readonly string[] Partial = { "", "▏", "▎", "▍", "▌", "▋", "▊", "▉" };
    private const string Full = "█";

    public override Size Measure(Size available) => new(available.Width, Values.Count);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty || Values.Count == 0)
        {
            return;
        }

        var theme = Theme.Current;
        int labelW = LabelWidth > 0 ? LabelWidth : Math.Min(20, MaxLabelWidth());
        double max = MaxValue ?? MaxOf();
        if (max <= 0)
        {
            max = 1;
        }

        int valueW = 7;
        int barW = Math.Max(0, area.Width - labelW - valueW - 2);

        int rows = Math.Min(Values.Count, area.Height);
        for (int i = 0; i < rows; i++)
        {
            int y = area.Y + i;
            var (label, value) = Values[i];

            var labelText = new StyledText(label, theme.MutedStyle);
            TextRenderer.DrawLine(surface, area.X, y, labelW, labelText, Justify.Left);

            var barColor = i < BarColors.Count ? BarColors[i] : theme.Accent;
            var barStyle = new Style(barColor, Color.Default);
            int barX = area.X + labelW + 1;
            DrawBar(surface, barX, y, barW, value / max, barStyle);

            string valueStr = value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            var valueText = new StyledText(valueStr, theme.TextStyle);
            TextRenderer.DrawLine(surface, area.X + labelW + 1 + barW + 1, y, valueW, valueText, Justify.Right);
        }
    }

    private static void DrawBar(Surface surface, int x, int y, int width, double fraction, Style style)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        double exact = fraction * width;
        int full = (int)Math.Floor(exact);
        int rem = (int)Math.Round((exact - full) * 8);
        if (rem >= 8) { full++; rem = 0; }

        for (int i = 0; i < full && i < width; i++)
        {
            surface.Set(x + i, y, Cell.FromGrapheme(Full, style));
        }

        if (full < width && rem > 0)
        {
            surface.Set(x + full, y, Cell.FromGrapheme(Partial[rem], style));
        }
    }

    private int MaxLabelWidth()
    {
        int w = 0;
        foreach (var (label, _) in Values)
        {
            w = Math.Max(w, Unicode.StringWidth(label));
        }

        return w;
    }

    private double MaxOf()
    {
        double m = 0;
        foreach (var (_, v) in Values)
        {
            m = Math.Max(m, v);
        }

        return m;
    }
}
