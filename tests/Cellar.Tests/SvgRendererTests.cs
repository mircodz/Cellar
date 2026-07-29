using Cellar.Primitives;
using Cellar.Rendering;

namespace Cellar.Tests;

public class SvgRendererTests
{
    [Fact]
    public void EmitsValidSvgEnvelope()
    {
        var s = new Surface(4, 2);
        s.DrawText(0, 0, "hi", Style.Default);
        var svg = new SvgRenderer { Padding = 0 }.Render(s);

        Assert.StartsWith("<svg", svg);
        Assert.Contains("</svg>", svg);
        Assert.Contains("width=\"36\"", svg);  // 4 cells * 9px, no padding
        Assert.Contains("height=\"36\"", svg); // 2 cells * 18px
    }

    [Fact]
    public void PaddingExpandsCanvas()
    {
        var s = new Surface(4, 2);
        var svg = new SvgRenderer { Padding = 10 }.Render(s);
        Assert.Contains("width=\"56\"", svg);  // 36 + 2*10
        Assert.Contains("translate(10,10)", svg);
    }

    [Fact]
    public void RendersGlyphsAndColors()
    {
        var s = new Surface(6, 1);
        s.DrawText(0, 0, "X", new Style(Color.Rgb(255, 0, 0), Color.Rgb(0, 0, 255)));
        var svg = new SvgRenderer().Render(s);

        Assert.Contains(">X</text>", svg);
        Assert.Contains("#ff0000", svg); // foreground
        Assert.Contains("#0000ff", svg); // background rect
    }

    [Fact]
    public void AppliesTextAttributes()
    {
        var s = new Surface(3, 1);
        s.DrawText(0, 0, "B", Style.Default.Bold.Underline);
        var svg = new SvgRenderer().Render(s);

        Assert.Contains("font-weight=\"bold\"", svg);
        Assert.Contains("underline", svg);
    }

    [Fact]
    public void EscapesMarkupChars()
    {
        var s = new Surface(6, 1);
        s.DrawText(0, 0, "<&>", Style.Default);
        var svg = new SvgRenderer().Render(s);

        // Each glyph is its own <text>; markup chars are escaped individually.
        Assert.Contains(">&lt;</text>", svg);
        Assert.Contains(">&amp;</text>", svg);
        Assert.Contains(">&gt;</text>", svg);
    }

    [Fact]
    public void DefaultBackgroundIsPaintedOnce()
    {
        var s = new Surface(10, 3); // all default cells
        var svg = new SvgRenderer { DefaultBackground = Color.Rgb(1, 2, 3) }.Render(s);

        // The canvas rect paints the default bg; individual default cells are NOT re-painted.
        int firstRect = svg.IndexOf("#010203", System.StringComparison.Ordinal);
        int secondRect = svg.IndexOf("#010203", firstRect + 1, System.StringComparison.Ordinal);
        Assert.True(firstRect >= 0);
        Assert.Equal(-1, secondRect); // only the canvas rect uses it
    }
}
