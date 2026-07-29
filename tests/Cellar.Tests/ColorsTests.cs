using Cellar.Primitives;

namespace Cellar.Tests;

public class ColorsTests
{
    [Theory]
    [InlineData("#ffffff", 255, 255, 255)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("ff8800", 255, 136, 0)]
    [InlineData("#f80", 255, 136, 0)] // shorthand expands via *17
    [InlineData("#abc", 170, 187, 204)]
    public void Hex_Parses(string hex, int r, int g, int b)
    {
        var c = Color.Hex(hex);
        Assert.True(c.IsRgb);
        Assert.Equal(r, c.R);
        Assert.Equal(g, c.G);
        Assert.Equal(b, c.B);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#gg0000")]
    [InlineData("12345")]
    [InlineData("#12")]
    public void Hex_InvalidReturnsFalse(string hex)
    {
        Assert.False(Color.TryHex(hex, out _));
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]     // red at 0°
    [InlineData(120, 0, 255, 0)]   // green at 120°
    public void Hsl_PrimaryHues(double h, int r, int g, int b)
    {
        var c = Color.Hsl(h, 1, 0.5);
        Assert.Equal(r, c.R);
        Assert.Equal(g, c.G);
        Assert.Equal(b, c.B);
    }

    [Fact]
    public void Lerp_Midpoint()
    {
        var c = Color.Lerp(Color.Rgb(0, 0, 0), Color.Rgb(100, 200, 40), 0.5);
        Assert.Equal(50, c.R);
        Assert.Equal(100, c.G);
        Assert.Equal(20, c.B);
    }

    [Fact]
    public void Lerp_ClampsEndpoints()
    {
        var a = Color.Rgb(10, 10, 10);
        var b = Color.Rgb(250, 250, 250);
        Assert.Equal(a, Color.Lerp(a, b, -1));
        Assert.Equal(b, Color.Lerp(a, b, 2));
    }

    [Fact]
    public void Gradient_EndpointsMatchStops()
    {
        var g = Color.Gradient(5, Color.Rgb(0, 0, 0), Color.Rgb(255, 255, 255));
        Assert.Equal(5, g.Length);
        Assert.Equal(Color.Rgb(0, 0, 0), g[0]);
        Assert.Equal(Color.Rgb(255, 255, 255), g[4]);
        // Monotonic increase in red across the ramp.
        for (int i = 1; i < g.Length; i++)
        {
            Assert.True(g[i].R >= g[i - 1].R);
        }
    }

    [Fact]
    public void Gradient_MultiStop()
    {
        var g = Color.Gradient(3, Color.Rgb(0, 0, 0), Color.Rgb(255, 0, 0), Color.Rgb(0, 0, 255));
        Assert.Equal(Color.Rgb(0, 0, 0), g[0]);
        Assert.Equal(Color.Rgb(255, 0, 0), g[1]); // middle stop hit exactly
        Assert.Equal(Color.Rgb(0, 0, 255), g[2]);
    }

    [Fact]
    public void Named_KnownColors()
    {
        Assert.True(Color.TryNamed("orange", out var orange));
        Assert.Equal(Color.Rgb(255, 165, 0), orange);
        Assert.True(Color.TryNamed("SKYBLUE", out _)); // case-insensitive
        Assert.False(Color.TryNamed("notacolor", out _));
    }
}
