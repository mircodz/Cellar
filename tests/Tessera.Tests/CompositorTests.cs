using Tessera.Primitives;
using Tessera.Rendering;

namespace Tessera.Tests;

public class SurfaceTests
{
    [Fact]
    public void DrawText_WritesCells()
    {
        var s = new Surface(10, 1);
        s.DrawText(0, 0, "hi", Style.Default);
        Assert.Equal("h", s.Get(0, 0).Grapheme);
        Assert.Equal("i", s.Get(1, 0).Grapheme);
    }

    [Fact]
    public void WideGlyph_OccupiesTwoColumns()
    {
        var s = new Surface(10, 1);
        s.DrawText(0, 0, "あ", Style.Default);
        Assert.True(s.Get(0, 0).IsWide);
        Assert.True(s.Get(1, 0).IsContinuation);
    }

    [Fact]
    public void OverwritingWideLeftHalf_ClearsContinuation()
    {
        var s = new Surface(10, 1);
        s.DrawText(0, 0, "あ", Style.Default);
        s.Set(0, 0, Cell.FromGrapheme("x", Style.Default));
        Assert.Equal("x", s.Get(0, 0).Grapheme);
        Assert.Equal(" ", s.Get(1, 0).Grapheme); // orphaned continuation blanked
    }

    [Fact]
    public void OverwritingWideContinuation_ClearsLeftHalf()
    {
        var s = new Surface(4, 1);
        s.DrawText(0, 0, "あ", Style.Default);

        s.Set(1, 0, Cell.FromGrapheme("x", Style.Default));

        Assert.Equal(" ", s.Get(0, 0).Grapheme);
        Assert.Equal("x", s.Get(1, 0).Grapheme);
    }

    [Fact]
    public void Clip_DiscardsOutOfBoundsWrites()
    {
        var s = new Surface(5, 5);
        s.SetClip(new Rect(1, 1, 2, 2));
        s.DrawText(0, 0, "abc", Style.Default);
        Assert.Equal(" ", s.Get(0, 0).Grapheme); // outside clip, untouched
    }

    [Fact]
    public void WideGlyph_AtClipEdge_IsDropped()
    {
        var s = new Surface(5, 1);
        s.SetClip(new Rect(0, 0, 1, 1)); // only column 0 writable
        s.Set(0, 0, Cell.FromGrapheme("あ", Style.Default));
        // No room for the trailing half, so a blank is written instead of a half-glyph.
        Assert.False(s.Get(0, 0).IsWide);
    }
}

public class ScreenTests
{
    [Fact]
    public void FirstDiff_PaintsAndPositionsCursor()
    {
        var screen = new Screen(3, 1, ColorDepth.TrueColor);
        screen.Back.DrawText(0, 0, "abc", Style.Default);
        var ansi = screen.ComputeDiff();
        Assert.Contains("\x1b[1;1H", ansi); // cursor home
        Assert.Contains("abc", ansi);
    }

    [Fact]
    public void SecondDiff_NoChange_IsEmpty()
    {
        var screen = new Screen(3, 1, ColorDepth.TrueColor);
        screen.Back.DrawText(0, 0, "abc", Style.Default);
        screen.ComputeDiff();
        screen.Back.DrawText(0, 0, "abc", Style.Default);
        Assert.Equal(string.Empty, screen.ComputeDiff());
    }

    [Fact]
    public void Diff_OnlyEmitsChangedCell()
    {
        var screen = new Screen(5, 1, ColorDepth.TrueColor);
        screen.Back.DrawText(0, 0, "hello", Style.Default);
        screen.ComputeDiff();

        // Change only the middle cell.
        screen.Back.DrawText(0, 0, "heLlo", Style.Default);
        var ansi = screen.ComputeDiff();

        Assert.Contains("\x1b[1;3H", ansi); // jump to column 3 (1-based)
        Assert.Contains("L", ansi);
        Assert.DoesNotContain("hello", ansi); // did not repaint the whole row
    }

    [Fact]
    public void Diff_RepositionsCursorAcrossGaps()
    {
        var screen = new Screen(5, 1, ColorDepth.TrueColor);
        screen.Back.DrawText(0, 0, "aaaaa", Style.Default);
        screen.ComputeDiff();

        // Change cols 0 and 4 only — cursor should jump, not stream through the middle.
        screen.Back.DrawText(0, 0, "baaab", Style.Default);
        var ansi = screen.ComputeDiff();
        Assert.Contains("\x1b[1;1H", ansi);
        Assert.Contains("\x1b[1;5H", ansi);
    }
}

public class AnsiStyleWriterTests
{
    private static string Transition(Style to, ColorDepth depth)
    {
        var w = new AnsiStyleWriter(depth);
        var sb = new System.Text.StringBuilder();
        w.WriteTransition(sb, to);
        return sb.ToString();
    }

    [Fact]
    public void DefaultBackground_Substituted_ForInheritBackground()
    {
        var w = new AnsiStyleWriter(ColorDepth.TrueColor) { DefaultBackground = Color.Rgb(21, 21, 21) };
        var sb = new System.Text.StringBuilder();
        w.WriteTransition(sb, Style.Default.WithForeground(Color.Rgb(200, 200, 200)));
        // Foreground truecolor, then the substituted background.
        Assert.Contains(";38;2;200;200;200", sb.ToString());
        Assert.Contains(";48;2;21;21;21", sb.ToString());
    }

    [Fact]
    public void DefaultBackground_Null_LeavesTransparent()
    {
        var w = new AnsiStyleWriter(ColorDepth.TrueColor) { DefaultBackground = null };
        var sb = new System.Text.StringBuilder();
        w.WriteTransition(sb, Style.Default.WithForeground(Color.Rgb(200, 200, 200)));
        Assert.DoesNotContain(";48;", sb.ToString()); // no background emitted
    }

    [Fact]
    public void ExplicitBackground_NotOverriddenByDefault()
    {
        var w = new AnsiStyleWriter(ColorDepth.TrueColor) { DefaultBackground = Color.Rgb(21, 21, 21) };
        var sb = new System.Text.StringBuilder();
        w.WriteTransition(sb, new Style(Color.Default, Color.Rgb(50, 60, 70)));
        Assert.Contains(";48;2;50;60;70", sb.ToString()); // explicit bg wins
        Assert.DoesNotContain(";48;2;21;21;21", sb.ToString());
    }

    [Fact]
    public void TrueColor_Rgb_Foreground()
    {
        var style = Style.Default.WithForeground(Color.Rgb(10, 20, 30));
        Assert.Equal("\x1b[0;38;2;10;20;30m", Transition(style, ColorDepth.TrueColor));
    }

    [Fact]
    public void Ansi16_NamedColor()
    {
        var style = Style.Default.WithForeground(Color.Red); // ansi index 1 => 31
        Assert.Equal("\x1b[0;31m", Transition(style, ColorDepth.TrueColor));
    }

    [Fact]
    public void BrightColor_UsesHighCodes()
    {
        var style = Style.Default.WithForeground(Color.BrightRed); // index 9 => 91
        Assert.Equal("\x1b[0;91m", Transition(style, ColorDepth.TrueColor));
    }

    [Fact]
    public void Attributes_AndBackground()
    {
        var style = new Style(Color.Default, Color.Blue, TextAttributes.Bold | TextAttributes.Underline);
        // reset;bold;underline;bg-blue(44)
        Assert.Equal("\x1b[0;1;4;44m", Transition(style, ColorDepth.TrueColor));
    }

    [Fact]
    public void Rgb_DowngradedTo256()
    {
        var style = Style.Default.WithForeground(Color.Rgb(255, 255, 255));
        Assert.Equal("\x1b[0;38;5;231m", Transition(style, ColorDepth.Ansi256));
    }
}
