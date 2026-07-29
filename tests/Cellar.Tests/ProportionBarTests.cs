using System.Text;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Widgets.Charts;

namespace Cellar.Tests;

public class ProportionBarTests
{
    private static int CountColor(Surface s, int y, Color color)
    {
        int n = 0;
        for (int x = 0; x < s.Width; x++)
            if (s.Get(x, y).Style.Foreground == color)
            {
                n++;
            }

        return n;
    }

    [Fact]
    public void Segments_FillWidthProportionally()
    {
        var bar = new ProportionBar
        {
            Segments =
            {
                new Segment("a", 30, Color.Rgb(255, 0, 0)),
                new Segment("b", 10, Color.Rgb(0, 255, 0)),
                new Segment("c", 60, Color.Rgb(0, 0, 255)),
            },
        };
        var s = new Surface(100, 1);
        bar.Render(s, s.Bounds);

        Assert.Equal(30, CountColor(s, 0, Color.Rgb(255, 0, 0)));
        Assert.Equal(10, CountColor(s, 0, Color.Rgb(0, 255, 0)));
        Assert.Equal(60, CountColor(s, 0, Color.Rgb(0, 0, 255)));
    }

    [Fact]
    public void Widths_SumToExactlyBarWidth()
    {
        // Values that don't divide evenly — largest-remainder must still fill the whole width.
        var bar = new ProportionBar
        {
            Segments = { new Segment("a", 1), new Segment("b", 1), new Segment("c", 1) },
        };
        var s = new Surface(10, 1);
        bar.Render(s, s.Bounds);

        int filled = 0;
        for (int x = 0; x < 10; x++)
            if (!s.Get(x, 0).IsEmpty && s.Get(x, 0).Grapheme != " ")
            {
                filled++;
            }

        Assert.Equal(10, filled);
    }

    [Fact]
    public void EmptySegments_RendersNothing()
    {
        var s = new Surface(10, 1);
        new ProportionBar().Render(s, s.Bounds);
        for (int x = 0; x < 10; x++)
            Assert.True(s.Get(x, 0).IsEmpty || s.Get(x, 0).Grapheme == " ");
    }

    [Fact]
    public void ZeroTotal_RendersNothing()
    {
        var bar = new ProportionBar { Segments = { new Segment("a", 0), new Segment("b", 0) } };
        var s = new Surface(10, 1);
        bar.Render(s, s.Bounds);
        for (int x = 0; x < 10; x++)
            Assert.True(s.Get(x, 0).IsEmpty || s.Get(x, 0).Grapheme == " ");
    }

    [Fact]
    public void MultiRow_FillsEveryRow()
    {
        var bar = new ProportionBar { Segments = { new Segment("a", 1, Color.Rgb(1, 2, 3)) } };
        var s = new Surface(5, 3);
        bar.Render(s, s.Bounds);
        for (int y = 0; y < 3; y++)
            Assert.Equal(5, CountColor(s, y, Color.Rgb(1, 2, 3)));
    }
}

public class LegendTests
{
    private static string RowText(Surface s, int y)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < s.Width; x++)
        {
            var g = s.Get(x, y).Grapheme;
            sb.Append(g.Length == 0 ? " " : g);
        }
        return sb.ToString();
    }

    [Fact]
    public void Horizontal_DrawsAllLabels()
    {
        var legend = new Legend
        {
            Items =
            {
                new LegendItem("strings", Color.Rgb(255, 0, 0), "30%"),
                new LegendItem("arrays", Color.Rgb(0, 255, 0), "10%"),
            },
        };
        var s = new Surface(40, 1);
        legend.Render(s, s.Bounds);
        var row = RowText(s, 0);
        Assert.Contains("strings", row);
        Assert.Contains("30%", row);
        Assert.Contains("arrays", row);
    }

    [Fact]
    public void Vertical_StacksEntries()
    {
        var legend = new Legend
        {
            Horizontal = false,
            Items = { new LegendItem("a", Color.Red), new LegendItem("b", Color.Green) },
        };
        var s = new Surface(20, 2);
        legend.Render(s, s.Bounds);
        Assert.Contains("a", RowText(s, 0));
        Assert.Contains("b", RowText(s, 1));
    }
}
