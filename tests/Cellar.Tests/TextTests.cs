using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Text;
using System.Linq;

namespace Cellar.Tests;

public class StyledTextTests
{
    [Fact]
    public void Builder_AppendsAndStylesCurrentSpan()
    {
        var st = StyledText.Of("error: ").Bold().Fg(Color.Red)
            .Append("oops").Fg(Color.White);

        Assert.Equal("error: oops", st.PlainText);
        Assert.Equal(2, st.Spans.Count);
        Assert.True((st.Spans[0].Style.Attributes & TextAttributes.Bold) != 0);
        Assert.Equal(Color.Red, st.Spans[0].Style.Foreground);
        Assert.Equal(Color.White, st.Spans[1].Style.Foreground);
    }

    [Fact]
    public void Width_SumsSpans()
    {
        var st = StyledText.Of("ab").Append("cd");
        Assert.Equal(4, st.Width);
    }

    [Fact]
    public void ImplicitFromString()
    {
        StyledText st = "hello";
        Assert.Equal("hello", st.PlainText);
    }

    [Fact]
    public void StyleCallOnEmpty_DoesNotThrow()
    {
        var st = StyledText.Empty().Bold();
        Assert.Equal(string.Empty, st.PlainText);
    }
}

public class TextRendererTests
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
    public void Wrap_BreaksAtSpaces()
    {
        var lines = TextRenderer.Wrap(StyledText.Of("the quick brown fox"), 10);
        Assert.True(lines.Count >= 2);
        foreach (var line in lines)
        {
            Assert.True(line.Width <= 10, $"line '{line.PlainText}' width {line.Width} exceeds 10");
        }
    }

    [Fact]
    public void Wrap_HardSplitsLongWord()
    {
        var lines = TextRenderer.Wrap(StyledText.Of("supercalifragilistic"), 8);
        Assert.True(lines.Count >= 2);
        foreach (var line in lines)
        {
            Assert.True(line.Width <= 8);
        }
    }

    [Fact]
    public void Wrap_PreservesStyleAcrossLines()
    {
        var text = StyledText.Of("red red red red").Fg(Color.Red);
        var lines = TextRenderer.Wrap(text, 7);
        foreach (var line in lines)
        {
            foreach (var span in line.Spans)
            {
                if (!string.IsNullOrWhiteSpace(span.Text))
                {
                    Assert.Equal(Color.Red, span.Style.Foreground);
                }
            }
        }
    }

    [Fact]
    public void DrawLine_Centered()
    {
        var s = new Surface(11, 1);
        TextRenderer.DrawLine(s, 0, 0, 11, StyledText.Of("abc"), Justify.Center);
        // "abc" width 3 in 11 => left pad (11-3)/2 = 4
        Assert.Equal("    abc    ", RowText(s, 0));
    }

    [Fact]
    public void DrawLine_RightJustified()
    {
        var s = new Surface(10, 1);
        TextRenderer.DrawLine(s, 0, 0, 10, StyledText.Of("hi"), Justify.Right);
        Assert.EndsWith("hi", RowText(s, 0));
    }

    [Fact]
    public void DrawLine_TruncatesWithEllipsis()
    {
        var s = new Surface(6, 1);
        TextRenderer.DrawLine(s, 0, 0, 6, StyledText.Of("abcdefghij"), Justify.Left);
        Assert.Contains("…", RowText(s, 0));
    }

    [Fact]
    public void DrawBlock_MultiLine()
    {
        var s = new Surface(10, 3);
        int drawn = TextRenderer.DrawBlock(s, s.Bounds, StyledText.Of("one two three four five"), Justify.Left);
        Assert.True(drawn >= 2);
        Assert.Contains("one", RowText(s, 0));
    }

    [Fact]
    public void DrawLine_RecordsLinkRects()
    {
        // "ab" + linked "CD" + "e": the link occupies columns [2,4) on row 0.
        var line = StyledText.Of("ab").Append("CD").Link("payload").Append("e");
        var s = new Surface(10, 1);
        var links = new System.Collections.Generic.List<LinkHit>();
        TextRenderer.DrawLine(s, 0, 0, 10, line, Justify.Left, links);

        Assert.Single(links);
        Assert.Equal("payload", links[0].Payload);
        Assert.True(links[0].Contains(2, 0));
        Assert.True(links[0].Contains(3, 0));
        Assert.False(links[0].Contains(1, 0)); // 'b', not linked
        Assert.False(links[0].Contains(4, 0)); // 'e', not linked
    }

    [Fact]
    public void DrawLine_NoLinks_RecordsNothing()
    {
        var s = new Surface(10, 1);
        var links = new System.Collections.Generic.List<LinkHit>();
        TextRenderer.DrawLine(s, 0, 0, 10, StyledText.Of("plain"), Justify.Left, links);
        Assert.Empty(links);
    }
}

public class DrawTextTests
{
    [Fact]
    public void ControlCharacters_AreDrawnNotSent()
    {
        var s = new Surface(10, 1);
        Assert.Equal(6, s.DrawText(0, 0, "a\u001b[2Jb", Style.Default));
        Assert.Equal("a", s.Get(0, 0).Grapheme);
        Assert.Equal("\u241B", s.Get(1, 0).Grapheme); // in a cell of its own, not merged into "a"
        Assert.Equal("[", s.Get(2, 0).Grapheme);
        Assert.Equal("b", s.Get(5, 0).Grapheme);
    }

    [Fact]
    public void BidiControls_AreDropped()
    {
        var s = new Surface(10, 1);
        Assert.Equal(4, s.DrawText(0, 0, "ab\u202Ecd", Style.Default));
        Assert.Equal("b", s.Get(1, 0).Grapheme);
        Assert.Equal("c", s.Get(2, 0).Grapheme);
    }

    [Fact]
    public void UnpairedSurrogate_IsDrawnAsReplacementCharacter()
    {
        var s = new Surface(10, 1);
        Assert.Equal(3, s.DrawText(0, 0, "a\uD800b", Style.Default));
        Assert.Equal("\uFFFD", s.Get(1, 0).Grapheme);
    }

    [Fact]
    public void MarkWithoutABase_TakesNoCell_AsMeasured()
    {
        var s = new Surface(10, 1);
        Assert.Equal(Unicode.StringWidth("\u0301a"), s.DrawText(0, 0, "\u0301a", Style.Default));
        Assert.Equal("a", s.Get(0, 0).Grapheme);
    }

    [Fact]
    public void CombiningMark_StaysWithItsBase()
    {
        var s = new Surface(10, 1);
        Assert.Equal(2, s.DrawText(0, 0, "e\u0301x", Style.Default));
        Assert.Equal("e\u0301", s.Get(0, 0).Grapheme);
    }

    [Fact]
    public void Cells_NeverHoldControlCharacters()
    {
        Assert.Equal("\u241B", Cell.FromGrapheme("\u001b", Style.Default).Grapheme);
        Assert.Equal("\u2407", Cell.FromRune(new System.Text.Rune(7), Style.Default).Grapheme);
        var sb = new System.Text.StringBuilder();
        Cell.FromGrapheme("x\u202E\u0301", Style.Default).AppendTo(sb);
        Assert.Equal("x\u0301", sb.ToString());
    }

    [Fact]
    public void Wrap_BreaksLinesAtLineBreaks()
    {
        var lines = TextRenderer.Wrap(StyledText.Of("one\ntwo\r\n\nthree"), 20);
        Assert.Equal(new[] { "one", "two", "", "three" }, lines.Select(line => line.PlainText));
    }
}
