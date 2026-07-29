using System.Linq;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Text;
using Cellar.Widgets;

namespace Cellar.Tests;

public class PolishWidgetTests
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

    [Fact]
    public void Padding_InsetsChild()
    {
        var s = new Surface(10, 5);
        new Padding(new Label("x"), new Thickness(2, 1, 0, 0)).Render(s, s.Bounds);
        Assert.Equal("x", s.Get(2, 1).Grapheme); // shifted right 2, down 1
    }

    [Fact]
    public void Rule_DrawsFullLine()
    {
        var s = new Surface(10, 1);
        new Rule().Render(s, s.Bounds);
        Assert.Equal("──────────", RowText(s, 0));
    }

    [Fact]
    public void Rule_WithCenteredLabel()
    {
        var s = new Surface(20, 1);
        new Rule(StyledText.Of("hi")).Render(s, s.Bounds);
        var row = RowText(s, 0);
        Assert.Contains("hi", row);
        Assert.StartsWith("─", row);
        Assert.EndsWith("─", row);
    }

    [Fact]
    public void Panel_DrawsBorderAndChild()
    {
        var s = new Surface(12, 4);
        new Panel(new Label("body"), "Title").Render(s, s.Bounds);
        Assert.Equal("╭", s.Get(0, 0).Grapheme);
        var all = string.Join("\n", Enumerable.Range(0, 4).Select(y => RowText(s, y)));
        Assert.Contains("Title", all);
        Assert.Contains("body", all);
    }

    [Fact]
    public void Panel_BackgroundFills()
    {
        var s = new Surface(10, 4);
        new Panel(null, null) { Background = Color.Rgb(10, 20, 30) }.Render(s, s.Bounds);
        Assert.Equal(Color.Rgb(10, 20, 30), s.Get(5, 2).Style.Background);
    }
}
