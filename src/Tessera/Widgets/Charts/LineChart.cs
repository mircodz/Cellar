using System;
using System.Collections.Generic;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Theming;

namespace Tessera.Widgets.Charts;

/// <summary>One named data series: a color and a list of (time, value) samples.</summary>
public sealed class Series
{
    public string Name { get; set; }
    public Color? Color { get; set; }

    /// <summary>Samples as (time, value). Kept sorted by time by <see cref="Add"/>.</summary>
    public List<(double Time, double Value)> Points { get; } = new();

    public Series(string name, Color? color = null)
    {
        Name = name;
        Color = color;
    }

    /// <summary>Appends a sample at a monotonically increasing time.</summary>
    public void Add(double time, double value) => Points.Add((time, value));

    /// <summary>Drops samples older than <paramref name="minTime"/> to bound memory.</summary>
    public void TrimBefore(double minTime)
    {
        int i = 0;
        while (i < Points.Count && Points[i].Time < minTime)
        {
            i++;
        }

        if (i > 0)
        {
            Points.RemoveRange(0, i);
        }
    }
}

/// <summary>
/// Plots one or more <see cref="Series"/> over time. Braille mode packs a 2×4 dot grid per
/// cell (each series in its own color); block mode fills the bottom row per column. Both axes
/// auto-fit the data across all series.
/// </summary>
public sealed class LineChart : Widget
{
    public List<Series> Series { get; } = new();

    /// <summary>When true, render braille subpixels; otherwise block columns.</summary>
    public bool UseBraille { get; set; } = true;

    // Braille dot bit for [row 0..3][col 0..1] within a 2x4 cell.
    private static readonly byte[,] DotBits =
    {
        { 0x01, 0x08 },
        { 0x02, 0x10 },
        { 0x04, 0x20 },
        { 0x40, 0x80 },
    };

    private static readonly string[] Blocks = { " ", "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

    // Reused braille dot-grid buffer, sized to the plot area (packed 1 byte per cell).
    private byte[] _brailleCells = [];

    /// <summary>
    /// Convenience view of the first series as a plain value list (time = sample index).
    /// Reading returns its values; assigning replaces the default series' points.
    /// </summary>
    public IList<double> Values => new DefaultSeriesValues(this);

    private Series EnsureDefaultSeries()
    {
        if (Series.Count == 0)
        {
            Series.Add(new Series("series"));
        }

        return Series[0];
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty || Series.Count == 0)
        {
            return;
        }

        (double start, double end) = TimeRange();
        (double vMin, double vRange) = ValueRange(start, end);

        var theme = Theme.Current;
        if (UseBraille)
        {
            RenderBraille(surface, area, start, end, vMin, vRange, theme);
        }
        else
        {
            RenderBlocks(surface, area, start, end, vMin, vRange, theme);
        }
    }

    private void RenderBraille(Surface surface, Rect area, double start, double end,
        double vMin, double vRange, Theme theme)
    {
        int subW = area.Width * 2;
        int subH = area.Height * 4;
        if (subW <= 0 || subH <= 0)
        {
            return;
        }

        double span = end - start;
        if (span <= 0)
        {
            span = 1;
        }

        // Each series accumulates its own dot bitfield (reused buffer) so colors don't blend.
        int w = area.Width, hgt = area.Height;
        if (_brailleCells.Length < w * hgt)
        {
            _brailleCells = new byte[w * hgt];
        }
        var cells = _brailleCells;

        foreach (var series in Series)
        {
            if (series.Points.Count == 0)
            {
                continue;
            }

            Array.Clear(cells, 0, w * hgt);
            var style = new Style(series.Color ?? theme.Accent, Color.Default);

            for (int sx = 0; sx < subW; sx++)
            {
                double t = start + span * ((double)sx / (subW - 1 == 0 ? 1 : subW - 1));
                if (!TrySampleAtTime(series, t, out double v))
                {
                    continue;
                }

                double norm = (v - vMin) / vRange;
                int sy = Math.Clamp((int)Math.Round(norm * (subH - 1)), 0, subH - 1);
                int plotY = subH - 1 - sy;
                int cx = sx / 2, cy = plotY / 4;
                if (cx < w && cy < hgt)
                {
                    cells[cy * w + cx] |= DotBits[plotY % 4, sx % 2];
                }
            }

            for (int cy = 0; cy < hgt; cy++)
            {
                for (int cx = 0; cx < w; cx++)
                {
                    byte bits = cells[cy * w + cx];
                    if (bits == 0)
                    {
                        continue;
                    }

                    char glyph = (char)(0x2800 + bits);
                    surface.Set(area.X + cx, area.Y + cy, Cell.FromChar(glyph, 1, style));
                }
            }
        }
    }

    private void RenderBlocks(Surface surface, Rect area, double start, double end,
        double vMin, double vRange, Theme theme)
    {
        double span = end - start;
        if (span <= 0)
        {
            span = 1;
        }

        foreach (var series in Series)
        {
            if (series.Points.Count == 0)
            {
                continue;
            }

            var style = new Style(series.Color ?? theme.Accent, Color.Default);
            for (int col = 0; col < area.Width; col++)
            {
                double t = start + span * (area.Width == 1 ? 0 : (double)col / (area.Width - 1));
                if (!TrySampleAtTime(series, t, out double v))
                {
                    continue;
                }

                double norm = (v - vMin) / vRange;
                int level = Math.Clamp((int)Math.Round(norm * (Blocks.Length - 1)), 0, Blocks.Length - 1);
                surface.Set(area.X + col, area.Bottom - 1, Cell.FromGrapheme(Blocks[level], style));
            }
        }
    }

    // Linear interpolation of a series at time t; false if t is outside its sample range.
    private static bool TrySampleAtTime(Series series, double t, out double value)
    {
        var pts = series.Points;
        value = 0;
        if (pts.Count == 0)
        {
            return false;
        }

        if (t < pts[0].Time || t > pts[^1].Time)
        {
            return false;
        }

        int lo = 0, hi = pts.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (pts[mid].Time <= t)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        var (t0, v0) = pts[lo];
        var (t1, v1) = pts[hi];
        if (t1 <= t0) { value = v0; return true; }
        double frac = (t - t0) / (t1 - t0);
        value = v0 + (v1 - v0) * frac;
        return true;
    }

    private (double min, double max) TimeRange()
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in Series)
        {
            if (s.Points.Count == 0)
            {
                continue;
            }

            min = Math.Min(min, s.Points[0].Time);
            max = Math.Max(max, s.Points[^1].Time);
        }
        if (min > max) { min = 0; max = 1; }
        return (min, max);
    }

    private (double min, double range) ValueRange(double start, double end)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in Series)
        {
            foreach (var (t, v) in s.Points)
            {
                if (t >= start && t <= end) { min = Math.Min(min, v); max = Math.Max(max, v); }
            }
        }

        if (min > max) { min = 0; max = 1; }
        double range = max - min;
        if (range <= 0)
        {
            range = 1;
        }

        return (min, range);
    }

    private sealed class DefaultSeriesValues(LineChart chart) : IList<double>
    {
        private Series S => chart.EnsureDefaultSeries();

        public double this[int index]
        {
            get => S.Points[index].Value;
            set => S.Points[index] = (index, value);
        }

        public int Count => chart.Series.Count == 0 ? 0 : chart.Series[0].Points.Count;
        public bool IsReadOnly => false;

        public void Add(double item)
        {
            var s = S;
            s.Points.Add((s.Points.Count, item)); // time = running index
        }

        public void Clear() => S.Points.Clear();

        public bool Contains(double item)
        {
            foreach (var p in S.Points)
            {
                if (p.Value == item)
                {
                    return true;
                }
            }

            return false;
        }

        public void CopyTo(double[] array, int arrayIndex)
        {
            foreach (var p in S.Points)
            {
                array[arrayIndex++] = p.Value;
            }
        }

        public IEnumerator<double> GetEnumerator()
        {
            foreach (var p in S.Points)
            {
                yield return p.Value;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public int IndexOf(double item)
        {
            for (int i = 0; i < S.Points.Count; i++)
            {
                if (S.Points[i].Value == item)
                {
                    return i;
                }
            }

            return -1;
        }

        public void Insert(int index, double item) => S.Points.Insert(index, (index, item));

        public bool Remove(double item)
        {
            int i = IndexOf(item);
            if (i < 0)
            {
                return false;
            }

            RemoveAt(i);
            return true;
        }

        public void RemoveAt(int index) => S.Points.RemoveAt(index);
    }
}
