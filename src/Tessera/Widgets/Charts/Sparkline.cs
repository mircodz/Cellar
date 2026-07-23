using System;
using System.Collections.Generic;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Theming;

namespace Tessera.Widgets.Charts;

/// <summary>
/// A single-row chart plotting a rolling series with the eight vertical block glyphs
/// (▁▂▃▄▅▆▇█). New samples push onto a fixed-capacity ring; values auto-scale to min/max.
/// </summary>
public sealed class Sparkline : Widget
{
    private static readonly string[] Bars = { " ", "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

    private readonly List<double> _values = new();
    private int _lastWidth = 1;

    /// <summary>Max samples retained. Null auto-sizes history to the render width; set a value
    /// to cap to a fixed window.</summary>
    public int? Capacity { get; set; }

    /// <summary>Line color. Null uses the theme accent.</summary>
    public Color? Color { get; set; }

    /// <summary>Cell background behind the bars. Null keeps it terminal-transparent. Set this
    /// when drawing over a solid-colored panel, otherwise the unfilled part of a partial block
    /// glyph shows the terminal background instead of the panel's, hiding the bar's height.</summary>
    public Color? Background { get; set; }

    /// <summary>When true, the bottom of the chart is anchored at 0 (bars show absolute
    /// magnitude); when false (default), it auto-fits the visible min so small variations fill
    /// the height. Anchor at zero for metrics like frame time where absolute size matters.</summary>
    public bool BaselineZero { get; set; }

    // The number of samples worth keeping: an explicit cap, or the last render width (plus a
    // little slack so a resize wider doesn't briefly show a gap).
    private int RetainLimit => Capacity ?? Math.Max(1, _lastWidth) + 8;

    /// <summary>Appends a sample, trimming the oldest beyond the retention limit.</summary>
    public void Push(double value)
    {
        _values.Add(value);
        int overflow = _values.Count - RetainLimit;
        if (overflow > 0)
        {
            _values.RemoveRange(0, overflow);
        }
    }

    /// <summary>Replaces the series wholesale.</summary>
    public void SetValues(IEnumerable<double> values)
    {
        _values.Clear();
        _values.AddRange(values);
        int overflow = _values.Count - RetainLimit;
        if (overflow > 0)
        {
            _values.RemoveRange(0, overflow);
        }
    }

    public override Size Measure(Size available) => new(available.Width, available.Height);

    public override void Render(Surface surface, Rect area)
    {
        // Record the width even with no data yet, so Push retains enough history to fill the
        // full row on the next frame.
        if (!area.IsEmpty)
        {
            _lastWidth = area.Width;
        }

        if (area.IsEmpty || _values.Count == 0)
        {
            return;
        }

        var style = new Style(Color ?? Theme.Current.Accent, Background ?? Primitives.Color.Default);
        int width = area.Width;
        int height = area.Height;

        // Show the most recent `width` samples, right-aligned.
        int start = Math.Max(0, _values.Count - width);
        int count = _values.Count - start;

        double min = double.MaxValue, max = double.MinValue;
        for (int i = start; i < _values.Count; i++)
        {
            min = Math.Min(min, _values[i]);
            max = Math.Max(max, _values[i]);
        }
        if (BaselineZero)
        {
            min = 0; // anchor the bottom at zero so bars show absolute magnitude
        }
        double range = max - min;
        if (range <= 0)
        {
            range = 1;
        }

        // Each column is a vertical bar `height` cells tall with 8 sub-levels per cell,
        // giving height*8 total resolution. Bars grow upward from the bottom row.
        int levels = height * 8;
        int drawX = area.X + (width - count); // right-align
        for (int i = start; i < _values.Count; i++)
        {
            double norm = (_values[i] - min) / range;          // 0..1
            int filled = (int)Math.Round(norm * levels);
            filled = Math.Clamp(filled, 0, levels);
            DrawColumn(surface, drawX++, area.Top, height, filled, style);
        }
    }

    // Draws a vertical bar of `filled` sub-levels (8 per cell) upward from the bottom of a
    // column that spans rows [top, top+height).
    private static void DrawColumn(Surface surface, int x, int top, int height, int filled, Style style)
    {
        for (int row = 0; row < height; row++)
        {
            // Row 0 is the top; fill accumulates from the bottom, so invert.
            int y = top + (height - 1 - row);
            int cellLevel = Math.Clamp(filled - row * 8, 0, 8);
            if (cellLevel == 0)
            {
                continue; // leave blank
            }

            surface.Set(x, y, Cell.FromGrapheme(Bars[cellLevel], style));
        }
    }
}
