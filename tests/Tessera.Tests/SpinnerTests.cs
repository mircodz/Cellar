using System.Text;
using Tessera.Rendering;
using Tessera.Widgets;

namespace Tessera.Tests;

public class SpinnerTests
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
    public void Advance_CyclesFrames()
    {
        var sp = new Spinner(SpinnerFrames.Line); // | / - \
        Assert.Equal("|", sp.CurrentFrame);
        sp.Advance();
        Assert.Equal("/", sp.CurrentFrame);
        sp.Advance();
        Assert.Equal("-", sp.CurrentFrame);
    }

    [Fact]
    public void Advance_WrapsAround()
    {
        var sp = new Spinner(SpinnerFrames.Line);
        for (int i = 0; i < 4; i++) sp.Advance();
        Assert.Equal("|", sp.CurrentFrame); // back to the start after 4 frames
    }

    [Fact]
    public void Reset_ReturnsToFirstFrame()
    {
        var sp = new Spinner(SpinnerFrames.Dots);
        sp.Advance();
        sp.Advance();
        sp.Reset();
        Assert.Equal("⠋", sp.CurrentFrame);
    }

    [Fact]
    public void Render_DrawsFrameAndLabel()
    {
        var s = new Surface(20, 1);
        new Spinner(SpinnerFrames.Dots, "Scanning").Render(s, s.Bounds);
        var row = RowText(s, 0);
        Assert.StartsWith("⠋", row);
        Assert.Contains("Scanning", row);
    }
}
