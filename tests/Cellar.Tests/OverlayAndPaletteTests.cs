using System.Linq;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Widgets;
using System.Text;

namespace Cellar.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void EmptyQuery_MatchesEverything()
    {
        var m = FuzzyMatcher.Match("", "anything");
        Assert.True(m.Matched);
        Assert.Equal(0, m.Score);
    }

    [Fact]
    public void Subsequence_Matches()
    {
        var m = FuzzyMatcher.Match("gca", "git commit all");
        Assert.True(m.Matched);
        Assert.Equal(3, m.Indices.Count);
    }

    [Fact]
    public void NonSubsequence_DoesNotMatch()
    {
        Assert.False(FuzzyMatcher.Match("xyz", "git commit").Matched);
    }

    [Fact]
    public void CaseInsensitive()
    {
        Assert.True(FuzzyMatcher.Match("GIT", "git status").Matched);
    }

    [Fact]
    public void ConsecutiveScoresHigherThanScattered()
    {
        int consec = FuzzyMatcher.Match("comm", "commit").Score;
        int scattered = FuzzyMatcher.Match("comm", "c-o-m-m-a-nd").Score;
        Assert.True(consec > scattered, $"consecutive {consec} should beat scattered {scattered}");
    }

    [Fact]
    public void BoundaryMatchScoresWell()
    {
        // "oc" should prefer "Open Command" (word boundary) over an inline occurrence.
        int boundary = FuzzyMatcher.Match("oc", "Open Command").Score;
        int inline = FuzzyMatcher.Match("oc", "processclock").Score;
        Assert.True(boundary > inline);
    }

    [Fact]
    public void Indices_PointToMatchedChars()
    {
        var m = FuzzyMatcher.Match("ab", "xaxb");
        Assert.Equal([1, 3], m.Indices);
    }
}

public class OverlayTests
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
    public void Resolve_CentersByDefault()
    {
        var o = new Overlay(new Label("x")) { Width = 20, Height = 10 };
        var rect = o.Resolve(new Size(80, 24));
        Assert.Equal((80 - 20) / 2, rect.X);
        Assert.Equal((24 - 10) / 2, rect.Y);
    }

    [Fact]
    public void Resolve_TopPlacement()
    {
        var o = new Overlay(new Label("x")) { Placement = OverlayPlacement.Top, Width = 20, Height = 5, Margin = 2 };
        var rect = o.Resolve(new Size(80, 24));
        Assert.Equal(2, rect.Y);
    }

    [Fact]
    public void Resolve_PercentSizing()
    {
        var o = new Overlay(new Label("x")) { WidthPercent = 50, HeightPercent = 50 };
        var rect = o.Resolve(new Size(80, 24));
        Assert.Equal(40, rect.Width);
        Assert.Equal(12, rect.Height);
    }

    [Fact]
    public void Render_DrawsContentInResolvedRect()
    {
        var s = new Surface(40, 12);
        s.Clear(Style.Default);
        var o = new Overlay(new Label("HELLO")) { Width = 10, Height = 3, ScrimOpacity = 0 };
        o.Render(s, new Size(40, 12));
        var rect = o.Resolve(new Size(40, 12));
        Assert.Contains("HELLO", RowText(s, rect.Y));
    }

    [Fact]
    public void Dim_DarkensTruecolorCells()
    {
        var s = new Surface(4, 1);
        s.DrawText(0, 0, "ab", new Style(Color.Rgb(200, 200, 200), Color.Rgb(100, 100, 100)));
        s.Dim(new Rect(0, 0, 4, 1), 0.5);
        var cell = s.Get(0, 0);
        Assert.Equal(100, cell.Style.Foreground.R); // 200 * 0.5
        Assert.Equal(50, cell.Style.Background.R);   // 100 * 0.5
    }
}

public class CommandPaletteTests
{
    private static KeyEvent Char(char c) => new(Key.Char, new System.Text.Rune(c), KeyModifiers.None);
    private static KeyEvent K(Key k) => new(k, default, KeyModifiers.None);

    [Fact]
    public void FiltersAndRanksByQuery()
    {
        var palette = new CommandPalette()
            .Add("Open File", () => { })
            .Add("Open Folder", () => { })
            .Add("Close Window", () => { });
        palette.HasFocus = true;

        // Type "of" — should match the two "Open F..." commands, not "Close Window".
        palette.OnEvent(Char('o'));
        palette.OnEvent(Char('f'));

        Assert.NotNull(palette.Selected);
        Assert.StartsWith("Open", palette.Selected!.Title);
    }

    [Fact]
    public void EnterRunsSelectedCommand()
    {
        int ran = 0;
        var palette = new CommandPalette().Add("Do Thing", () => ran = 42);
        palette.HasFocus = true;
        palette.OnEvent(K(Key.Enter));
        Assert.Equal(42, ran);
    }

    [Fact]
    public void OnRunFiresAfterCommand()
    {
        bool onRun = false;
        var palette = new CommandPalette { OnAfterRun = () => onRun = true };
        palette.Add("X", () => { });
        palette.HasFocus = true;
        palette.OnEvent(K(Key.Enter));
        Assert.True(onRun);
    }

    [Fact]
    public void ArrowKeysMoveSelection()
    {
        var palette = new CommandPalette()
            .Add("A", () => { })
            .Add("B", () => { })
            .Add("C", () => { });
        palette.HasFocus = true;
        Assert.Equal("A", palette.Selected!.Title);
        palette.OnEvent(K(Key.Down));
        Assert.Equal("B", palette.Selected!.Title);
    }

    [Fact]
    public void NoMatch_SelectedIsNull()
    {
        var palette = new CommandPalette().Add("Alpha", () => { });
        palette.HasFocus = true;
        palette.OnEvent(Char('z'));
        palette.OnEvent(Char('z'));
        palette.OnEvent(Char('z'));
        Assert.Null(palette.Selected);
    }

    [Fact]
    public void Render_ShowsCommandsAndPrompt()
    {
        var palette = new CommandPalette()
            .Add("First Command", () => { })
            .Add("Second Command", () => { });
        palette.HasFocus = true;
        var s = new Surface(40, 10);
        s.Clear(Style.Default);
        palette.Render(s, s.Bounds);
        var all = string.Join("\n", Enumerable.Range(0, 10).Select(y =>
        {
            var sb = new StringBuilder();
            for (int x = 0; x < s.Width; x++) { var g = s.Get(x, y).Grapheme; sb.Append(g.Length == 0 ? " " : g); }
            return sb.ToString();
        }));
        Assert.Contains("First Command", all);
        Assert.Contains("Second Command", all);
    }

    [Fact]
    public void Render_TitleWithSupplementaryUnicode_DoesNotThrow()
    {
        var palette = new CommandPalette().Add("Open 🚀", () => { });
        var surface = new Surface(40, 6);

        palette.Render(surface, surface.Bounds);

        Assert.Contains(Enumerable.Range(0, surface.Height)
            .SelectMany(y => Enumerable.Range(0, surface.Width).Select(x => surface.Get(x, y))),
            cell => cell.Grapheme == "🚀");
    }
}
