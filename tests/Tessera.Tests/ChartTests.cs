using System;
using System.Collections.Generic;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Widgets.Charts;

namespace Tessera.Tests;

public class ChartTests
{
    private static string RowText(Surface s, int y)
    {
        var sb = new System.Text.StringBuilder();
        for (int x = 0; x < s.Width; x++)
        {
            var g = s.Get(x, y).Grapheme;
            sb.Append(g.Length == 0 ? " " : g);
        }
        return sb.ToString();
    }

    private static bool AnyNonBlank(Surface s)
    {
        for (int y = 0; y < s.Height; y++)
        {
            for (int x = 0; x < s.Width; x++)
            {
                var g = s.Get(x, y).Grapheme;
                if (g.Length > 0 && g != " ")
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---- Sparkline ----

    [Fact]
    public void Sparkline_HighestSampleUsesTallestGlyph()
    {
        var sp = new Sparkline { Capacity = 3 };
        sp.Push(0);
        sp.Push(0);
        sp.Push(1); // max => full block on the right
        var s = new Surface(3, 1);
        sp.Render(s, s.Bounds);
        Assert.Equal("█", s.Get(2, 0).Grapheme);
    }

    [Fact]
    public void Sparkline_EmptyRendersNothing()
    {
        var s = new Surface(5, 1);
        new Sparkline().Render(s, s.Bounds);
        Assert.False(AnyNonBlank(s));
    }

    [Fact]
    public void Sparkline_AutoFillsFullWidth()
    {
        // With no explicit Capacity, the sparkline retains enough history to fill a wide
        // row after it learns its render width — the series should span the whole width, not
        // just the rightmost ~60% (the original fixed-capacity bug).
        var sp = new Sparkline();
        var s = new Surface(120, 1);
        sp.Render(s, s.Bounds);               // learns width = 120 (nothing to draw yet)
        for (int i = 0; i < 400; i++)
        {
            sp.Push(0.5 + 0.5 * Math.Sin(i * 0.2));
        }

        sp.Render(s, s.Bounds);

        // Count filled cells; a full-width fill covers the large majority (only near-minimum
        // samples map to a blank glyph). The old bug left ~40% of the left blank.
        int filled = 0;
        for (int x = 0; x < s.Width; x++)
        {
            var g = s.Get(x, 0).Grapheme;
            if (g.Length > 0 && g != " ")
            {
                filled++;
            }
        }
        Assert.True(filled > s.Width / 2, $"only {filled}/{s.Width} cells filled");

        // And the fill must reach into the left portion, not cluster on the right.
        int leftFilled = 0;
        for (int x = 0; x < 30; x++)
        {
            var g = s.Get(x, 0).Grapheme;
            if (g.Length > 0 && g != " ")
            {
                leftFilled++;
            }
        }
        Assert.True(leftFilled > 0, "left region is entirely blank — not full width");
    }

    // ---- BarChart ----

    [Fact]
    public void BarChart_DrawsLabelsAndBars()
    {
        var chart = new BarChart { Values = { ("alpha", 10), ("beta", 5) } };
        var s = new Surface(40, 2);
        chart.Render(s, s.Bounds);
        var all = RowText(s, 0) + RowText(s, 1);
        Assert.Contains("alpha", all);
        Assert.Contains("beta", all);
        Assert.Contains("█", all); // bar fill present
    }

    [Fact]
    public void BarChart_LargerValueHasLongerBar()
    {
        var chart = new BarChart { Values = { ("big", 100), ("small", 10) }, LabelWidth = 6 };
        var s = new Surface(40, 2);
        chart.Render(s, s.Bounds);
        int bigBar = CountGlyph(s, 0, "█");
        int smallBar = CountGlyph(s, 1, "█");
        Assert.True(bigBar > smallBar);
    }

    private static int CountGlyph(Surface s, int y, string glyph)
    {
        int n = 0;
        for (int x = 0; x < s.Width; x++)
        {
            if (s.Get(x, y).Grapheme == glyph)
            {
                n++;
            }
        }

        return n;
    }

    // ---- LineChart ----

    [Fact]
    public void LineChart_BrailleRendersDots()
    {
        var chart = new LineChart { UseBraille = true };
        for (int i = 0; i < 40; i++)
        {
            chart.Values.Add(Math.Sin(i * 0.3));
        }

        var s = new Surface(20, 4);
        chart.Render(s, s.Bounds);
        Assert.True(AnyNonBlank(s));
        // Braille glyphs live in U+2800..U+28FF.
        bool foundBraille = false;
        for (int y = 0; y < s.Height && !foundBraille; y++)
        {
            for (int x = 0; x < s.Width; x++)
            {
                var g = s.Get(x, y).Grapheme;
                if (g.Length > 0 && g[0] >= 0x2800 && g[0] <= 0x28FF) { foundBraille = true; break; }
            }
        }

        Assert.True(foundBraille);
    }

    [Fact]
    public void LineChart_BlockModeRenders()
    {
        var chart = new LineChart { UseBraille = false };
        for (int i = 0; i < 20; i++)
        {
            chart.Values.Add(i);
        }

        var s = new Surface(20, 3);
        chart.Render(s, s.Bounds);
        Assert.True(AnyNonBlank(s));
    }

    [Fact]
    public void LineChart_EmptyRendersNothing()
    {
        var s = new Surface(10, 3);
        new LineChart().Render(s, s.Bounds);
        Assert.False(AnyNonBlank(s));
    }

    [Fact]
    public void LineChart_MultiSeriesUseDistinctColors()
    {
        var chart = new LineChart { UseBraille = true };
        var a = new Series("a", Color.Rgb(255, 0, 0));
        var b = new Series("b", Color.Rgb(0, 0, 255));
        for (int i = 0; i <= 10; i++) { a.Add(i, 0.0); b.Add(i, 1.0); } // separate value bands
        chart.Series.Add(a);
        chart.Series.Add(b);

        var s = new Surface(20, 4);
        chart.Render(s, s.Bounds);

        var colors = new HashSet<Color>();
        for (int y = 0; y < s.Height; y++)
        {
            for (int x = 0; x < s.Width; x++)
            {
                var g = s.Get(x, y).Grapheme;
                if (g.Length > 0 && g[0] >= 0x2800 && g[0] <= 0x28FF)
                {
                    colors.Add(s.Get(x, y).Style.Foreground);
                }
            }
        }

        Assert.Contains(Color.Rgb(255, 0, 0), colors);
        Assert.Contains(Color.Rgb(0, 0, 255), colors);
    }

    [Fact]
    public void Series_TrimBeforeDropsOldSamples()
    {
        var s = new Series("s");
        for (int i = 0; i < 10; i++)
        {
            s.Add(i, i);
        }

        s.TrimBefore(5);
        Assert.Equal(5, s.Points[0].Time);
        Assert.Equal(5, s.Points.Count);
    }
}
