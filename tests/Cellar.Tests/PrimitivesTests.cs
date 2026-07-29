using Cellar.Primitives;

namespace Cellar.Tests;

public class ColorQuantizerTests
{
    [Fact]
    public void ToAnsi256_PureBlack_IsCubeOrigin()
    {
        Assert.Equal(16, ColorQuantizer.ToAnsi256(0, 0, 0));
    }

    [Fact]
    public void ToAnsi256_PureWhite_IsCubeMax()
    {
        // 255,255,255 => cube index 16 + 36*5 + 6*5 + 5 = 231
        Assert.Equal(231, ColorQuantizer.ToAnsi256(255, 255, 255));
    }

    [Fact]
    public void ToAnsi256_MidGray_PicksGrayscaleRamp()
    {
        int idx = ColorQuantizer.ToAnsi256(128, 128, 128);
        Assert.InRange(idx, 232, 255);
    }

    [Theory]
    [InlineData(255, 0, 0, 9)]   // bright red
    [InlineData(0, 255, 0, 10)]  // bright green
    [InlineData(0, 0, 255, 12)]  // bright blue
    [InlineData(0, 0, 0, 0)]     // black
    [InlineData(255, 255, 255, 15)] // bright white
    public void ToAnsi16_PrimaryColors(byte r, byte g, byte b, int expected)
    {
        Assert.Equal(expected, ColorQuantizer.ToAnsi16(r, g, b));
    }
}

public class GeometryTests
{
    [Fact]
    public void Rect_Deflate_ShrinksByThickness()
    {
        var r = new Rect(0, 0, 10, 10).Deflate(new Thickness(2, 1, 2, 1));
        Assert.Equal(new Rect(2, 1, 6, 8), r);
    }

    [Fact]
    public void Rect_Deflate_ClampsAtZero()
    {
        var r = new Rect(0, 0, 3, 3).Deflate(new Thickness(5));
        Assert.True(r.IsEmpty);
    }

    [Fact]
    public void Rect_Intersect_Overlap()
    {
        var a = new Rect(0, 0, 5, 5);
        var b = new Rect(3, 3, 5, 5);
        Assert.Equal(new Rect(3, 3, 2, 2), a.Intersect(b));
    }

    [Fact]
    public void Rect_Intersect_Disjoint_IsEmpty()
    {
        var a = new Rect(0, 0, 2, 2);
        var b = new Rect(5, 5, 2, 2);
        Assert.True(a.Intersect(b).IsEmpty);
    }

    [Fact]
    public void Rect_Contains_Point()
    {
        var r = new Rect(1, 1, 3, 3);
        Assert.True(r.Contains(1, 1));
        Assert.True(r.Contains(3, 3));
        Assert.False(r.Contains(4, 4));
    }
}
