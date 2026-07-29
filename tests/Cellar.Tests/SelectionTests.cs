using System;
using System.Linq;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;

namespace Cellar.Tests;

public class SelectionTests
{
    private static Surface TextSurface(params string[] lines)
    {
        int w = lines.Max(l => l.Length);
        var s = new Surface(w, lines.Length);
        s.Clear(Style.Default);
        for (int y = 0; y < lines.Length; y++)
        {
            s.DrawText(0, y, lines[y], Style.Default);
        }

        return s;
    }

    [Fact]
    public void Linear_SingleLine_ExtractsRange()
    {
        var s = TextSurface("hello world");
        var sel = new Selection(new Point(0, 0), new Point(4, 0)); // "hello"
        Assert.Equal("hello", s.ExtractText(sel));
    }

    [Fact]
    public void Linear_ReversedDrag_SameResult()
    {
        var s = TextSurface("hello world");
        var forward = new Selection(new Point(0, 0), new Point(4, 0));
        var backward = new Selection(new Point(4, 0), new Point(0, 0));
        Assert.Equal(s.ExtractText(forward), s.ExtractText(backward));
    }

    [Fact]
    public void Linear_MultiLine_FlowsByRows()
    {
        var s = TextSurface("abcde", "fghij", "klmno");
        // From (2,0) to (2,2): "cde" + "fghij" + "klm"
        var sel = new Selection(new Point(2, 0), new Point(2, 2));
        Assert.Equal("cde\nfghij\nklm", s.ExtractText(sel));
    }

    [Fact]
    public void Linear_TrimsTrailingWhitespace()
    {
        var s = TextSurface("hi        ", "yo");
        var sel = new Selection(new Point(0, 0), new Point(1, 1));
        Assert.Equal("hi\nyo", s.ExtractText(sel));
    }

    [Fact]
    public void Block_SelectsRectangle()
    {
        var s = TextSurface("abcde", "fghij", "klmno");
        // Block from (1,0) to (2,2): columns 1-2 of each row => "bc","gh","lm"
        var sel = new Selection(new Point(1, 0), new Point(2, 2), SelectionMode.Block);
        Assert.Equal("bc\ngh\nlm", s.ExtractText(sel));
    }

    [Fact]
    public void EmptySelection_YieldsEmptyString()
    {
        var s = TextSurface("hello");
        var sel = new Selection(new Point(2, 0), new Point(2, 0));
        Assert.True(sel.IsEmpty);
        Assert.Equal(string.Empty, s.ExtractText(sel));
    }

    [Fact]
    public void Contains_LinearMiddleLine_WholeRow()
    {
        var sel = new Selection(new Point(3, 0), new Point(2, 2));
        Assert.True(sel.Contains(0, 1));   // middle line, any x
        Assert.True(sel.Contains(99, 1));
        Assert.False(sel.Contains(0, 0));  // first line before start.X
        Assert.True(sel.Contains(3, 0));
    }

    [Fact]
    public void Contains_Block_OnlyWithinColumns()
    {
        var sel = new Selection(new Point(1, 0), new Point(2, 2), SelectionMode.Block);
        Assert.True(sel.Contains(1, 1));
        Assert.True(sel.Contains(2, 1));
        Assert.False(sel.Contains(0, 1));
        Assert.False(sel.Contains(3, 1));
    }

    [Fact]
    public void WideGlyph_ExtractedOnce()
    {
        var s = new Surface(6, 1);
        s.Clear(Style.Default);
        s.DrawText(0, 0, "a你b", Style.Default); // 你 is width-2
        // Select all 4 columns (a, 你 + continuation, b).
        var sel = new Selection(new Point(0, 0), new Point(3, 0));
        Assert.Equal("a你b", s.ExtractText(sel));
    }

    [Fact]
    public void Highlight_AppliesSelectionColors()
    {
        var s = TextSurface("hello");
        var sel = new Selection(new Point(0, 0), new Point(1, 0));
        s.HighlightSelection(sel, Color.Rgb(255, 255, 255), Color.Rgb(0, 0, 128));
        Assert.Equal(Color.Rgb(0, 0, 128), s.Get(0, 0).Style.Background);
        Assert.Equal("h", s.Get(0, 0).Grapheme); // glyph preserved
        Assert.NotEqual(Color.Rgb(0, 0, 128), s.Get(2, 0).Style.Background); // outside selection
    }
}

public class ClipboardTests
{
    [Fact]
    public void SetClipboardSequence_EncodesBase64Osc52()
    {
        // "hi" => base64 "aGk="
        var seq = Clipboard.SetClipboardSequence("hi");
        Assert.Equal("\x1b]52;c;aGk=\x07", seq);
    }

    [Fact]
    public void SetClipboardSequence_Empty_IsEmpty()
    {
        Assert.Equal(string.Empty, Clipboard.SetClipboardSequence(""));
    }

    [Fact]
    public void SetClipboardSequence_Utf8RoundTrips()
    {
        var seq = Clipboard.SetClipboardSequence("héllo 你");
        // Decode the base64 payload back and confirm it matches.
        string b64 = seq.Substring("\x1b]52;c;".Length).TrimEnd('\x07');
        var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        Assert.Equal("héllo 你", text);
    }
}
