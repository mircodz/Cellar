using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cellar.Primitives;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public class TextViewerTests
{
    private static KeyEvent K(Key key) => new(key, default, KeyModifiers.None);
    private static KeyEvent C(char c) => new(Key.Char, new Rune(c), KeyModifiers.None);

    private static string Numbered(int count) => string.Join('\n', Enumerable.Range(1, count).Select(i => $"line {i}"));

    private static string[] Rows(Widget widget, int width, int height)
    {
        var surface = SurfaceText.Render(widget, width, height);
        var rows = new string[height];
        for (int y = 0; y < height; y++)
        {
            rows[y] = SurfaceText.Row(surface, y).TrimEnd();
        }

        return rows;
    }

    private static void Type(Widget widget, string text)
    {
        foreach (char c in text)
        {
            widget.OnEvent(C(c));
        }
    }

    [Fact]
    public void Render_ShowsLinesFromTheTop_WithAScrollbarWhenLonger()
    {
        var viewer = new TextViewer(Numbered(20)) { HasFocus = true };
        var rows = Rows(viewer, 20, 5);
        Assert.StartsWith("line 1", rows[0]);
        Assert.StartsWith("line 5", rows[4]);
        Assert.NotEqual(' ', rows[0][^1]); // scrollbar in the last column

        var fits = Rows(new TextViewer("one\ntwo"), 20, 5);
        Assert.Equal(["one", "two", "", "", ""], fits);
    }

    [Fact]
    public void Text_DropsCarriageReturns_AndExpandsTabs()
    {
        var viewer = new TextViewer("a\tb\r\nab\tc");
        Assert.Equal(2, viewer.LineCount);
        Assert.Equal("a   b", viewer.Line(0));
        Assert.Equal("ab  c", viewer.Line(1));
    }

    [Fact]
    public void Keys_Scroll_Page_AndJump()
    {
        var viewer = new TextViewer(Numbered(50)) { HasFocus = true };
        Rows(viewer, 20, 10);

        viewer.OnEvent(C('j'));
        viewer.OnEvent(K(Key.Down));
        Assert.Equal(2, viewer.TopLine);
        viewer.OnEvent(C('k'));
        Assert.Equal(1, viewer.TopLine);
        viewer.OnEvent(C(' '));
        Assert.Equal(10, viewer.TopLine);
        viewer.OnEvent(C('G'));
        Assert.Equal(40, viewer.TopLine);
        Assert.StartsWith("line 50", Rows(viewer, 20, 10)[9]);
        viewer.OnEvent(C('g'));
        Assert.Equal(0, viewer.TopLine);
        viewer.OnEvent(K(Key.Up));
        Assert.Equal(0, viewer.TopLine);
    }

    [Fact]
    public void Wheel_ScrollsWithoutFocus()
    {
        var viewer = new TextViewer(Numbered(50));
        Rows(viewer, 20, 10);
        Assert.True(viewer.OnEvent(new MouseEvent(MouseEventKind.Wheel, MouseButton.WheelDown, 3, 3, KeyModifiers.None)));
        Assert.Equal(3, viewer.TopLine);
        Assert.False(viewer.OnEvent(C('j')));
    }

    [Fact]
    public void LongLines_ScrollSideways_WithAnEllipsis()
    {
        var viewer = new TextViewer("0123456789abcdefghij") { HasFocus = true };
        Assert.Equal("012345678…", Rows(viewer, 10, 1)[0]);
        viewer.OnEvent(C('l'));
        Assert.Equal(8, viewer.LeftColumn);
        Assert.Equal("89abcdefg…", Rows(viewer, 10, 1)[0]);
        viewer.OnEvent(C('h'));
        Assert.Equal(0, viewer.LeftColumn);
    }

    [Fact]
    public void Wrap_ContinuesLongLinesOnTheNextRow_AndKeepsTheTopLine()
    {
        var viewer = new TextViewer("0123456789abcdefghij\nshort\n" + Numbered(10)) { HasFocus = true };
        Assert.Equal("01234567…", Rows(viewer, 10, 3)[0]); // the last column is the scrollbar

        viewer.OnEvent(C('w'));
        Assert.True(viewer.Wrap);
        var rows = Rows(viewer, 10, 3);
        Assert.Equal("012345678", rows[0][..9]);
        Assert.Equal("9abcdefgh", rows[1][..9]);
        Assert.Equal("ij", rows[2].TrimEnd());

        viewer.OnEvent(C('j'));
        viewer.OnEvent(C('j'));
        viewer.OnEvent(C('j'));
        Assert.Equal(1, viewer.TopLine);
        Assert.StartsWith("short", Rows(viewer, 10, 3)[0]);

        viewer.OnEvent(C('w'));
        Assert.Equal(1, viewer.TopLine);
        Assert.StartsWith("short", Rows(viewer, 10, 3)[0]);
        viewer.Wrap = true;
        Assert.False(viewer.OnEvent(C('l'))); // no sideways scrolling while wrapping
    }

    [Fact]
    public void LineNumbers_ToggleWithHash()
    {
        var viewer = new TextViewer(Numbered(12)) { HasFocus = true };
        viewer.OnEvent(C('#'));
        var rows = Rows(viewer, 20, 12);
        Assert.StartsWith(" 1 line 1", rows[0]);
        Assert.StartsWith("12 line 12", rows[11]);
        viewer.OnEvent(C('#'));
        Assert.StartsWith("line 1", Rows(viewer, 20, 12)[0]);
    }

    [Fact]
    public void Search_HighlightsWhileTyping_CountsMatches_AndSteps()
    {
        var viewer = new TextViewer("alpha\nbeta\nAlpha beta\n" + Numbered(30) + "\nalpha again") { HasFocus = true };
        Rows(viewer, 30, 6);

        viewer.OnEvent(C('/'));
        Assert.True(viewer.IsSearching);
        Type(viewer, "alp");
        Assert.Equal("alp", viewer.Search);
        Assert.Equal(3, viewer.MatchCount); // smart case: lower-case query ignores case
        Assert.Equal(1, viewer.CurrentMatch);

        var surface = SurfaceText.Render(viewer, 30, 6);
        Assert.Equal("/alp", SurfaceText.Row(surface, 5)[..4]);
        Assert.EndsWith("1/3", SurfaceText.Row(surface, 5).TrimEnd());
        Assert.Equal(Theme.Current.Accent, surface.Get(0, 0).Style.Background);  // current match
        Assert.Equal(Theme.Current.Warning, surface.Get(0, 2).Style.Background); // other match
        Assert.NotEqual(Theme.Current.Warning, surface.Get(3, 2).Style.Background);

        viewer.OnEvent(K(Key.Enter));
        Assert.False(viewer.IsSearching);
        viewer.OnEvent(C('n'));
        Assert.Equal(2, viewer.CurrentMatch);
        viewer.OnEvent(C('n'));
        Assert.Equal(3, viewer.CurrentMatch);
        Assert.True(viewer.TopLine > 20); // scrolled to the last match
        viewer.OnEvent(C('n'));
        Assert.Equal(1, viewer.CurrentMatch);
        Assert.Equal(0, viewer.TopLine);
        viewer.OnEvent(C('N'));
        Assert.Equal(3, viewer.CurrentMatch);

        viewer.Find("Alpha");
        Assert.Equal(1, viewer.MatchCount); // an upper-case letter makes it case-sensitive

        viewer.OnEvent(K(Key.Escape));
        Assert.Equal(string.Empty, viewer.Search);
        Assert.Equal(0, viewer.MatchCount);
        Assert.False(viewer.OnEvent(K(Key.Escape))); // nothing left to clear: the container can use it
    }

    [Fact]
    public void Search_NoMatches_IsShownInTheFooter()
    {
        var viewer = new TextViewer("one\ntwo") { HasFocus = true };
        viewer.Find("zzz");
        var surface = SurfaceText.Render(viewer, 20, 3);
        Assert.EndsWith("no matches", SurfaceText.Row(surface, 2).TrimEnd());
        Assert.Equal(Theme.Current.Error, surface.Get(19, 2).Style.Foreground);
    }

    [Fact]
    public void SearchPrompt_EscapeRestoresThePreviousSearchAndPosition()
    {
        var viewer = new TextViewer(Numbered(40)) { HasFocus = true };
        Rows(viewer, 20, 5);
        viewer.Find("line 2");
        int top = viewer.TopLine;
        int count = viewer.MatchCount;

        viewer.OnEvent(C('/'));
        Type(viewer, "line 39");
        Assert.True(viewer.TopLine > top);
        viewer.OnEvent(K(Key.Escape));

        Assert.False(viewer.IsSearching);
        Assert.Equal("line 2", viewer.Search);
        Assert.Equal(count, viewer.MatchCount);
        Assert.Equal(top, viewer.TopLine);
    }

    [Fact]
    public void SearchPrompt_RemembersHistory()
    {
        var history = new InputHistory();
        var viewer = new TextViewer("one\ntwo") { HasFocus = true, SearchHistory = history };
        viewer.OnEvent(C('/'));
        Type(viewer, "two");
        viewer.OnEvent(K(Key.Enter));
        Assert.Equal(["two"], history.Entries);

        viewer.OnEvent(C('/'));
        viewer.OnEvent(K(Key.Up));
        Assert.Equal("two", viewer.Search);
    }

    [Fact]
    public void SearchPrompt_CanBeTurnedOff()
    {
        var viewer = new TextViewer("one") { HasFocus = true, SearchPrompt = false };
        Assert.False(viewer.OnEvent(C('/')));
        Assert.False(viewer.IsSearching);
    }

    [Fact]
    public void Copy_VisibleLinesOrEverything_AndSaysSo()
    {
        var copied = new List<string>();
        var viewer = new TextViewer(Numbered(10)) { HasFocus = true, OnCopy = copied.Add };
        Rows(viewer, 20, 3);
        viewer.OnEvent(C('j'));

        viewer.OnEvent(C('y'));
        Assert.Equal("line 2\nline 3\nline 4", copied[0]);
        Assert.Equal("copied 3 lines", Rows(viewer, 20, 3)[2]);

        viewer.OnEvent(C('Y'));
        Assert.Equal(Numbered(10), copied[1]);
        viewer.OnEvent(C('j'));
        Assert.DoesNotContain("copied", Rows(viewer, 20, 3)[2]);
    }

    [Fact]
    public void Text_Refresh_KeepsThePosition_AndTheSearch()
    {
        var viewer = new TextViewer(Numbered(30)) { HasFocus = true };
        Rows(viewer, 20, 5);
        viewer.ScrollToLine(12);
        viewer.Find("line 1");
        int top = viewer.TopLine;

        viewer.Text = Numbered(40);
        Assert.Equal(top, viewer.TopLine);
        Assert.Equal(11, viewer.MatchCount); // line 1, 10..19
    }

    [Fact]
    public void Highlighter_StylesEachLine_AndSetLinesBypassesIt()
    {
        var red = new Style(Color.Rgb(255, 0, 0), Color.Default);
        var viewer = new TextViewer("x") { Highlighter = line => new StyledText(line, red) };
        Assert.Equal(red, SurfaceText.Render(viewer, 5, 1).Get(0, 0).Style);

        var blue = new Style(Color.Rgb(0, 0, 255), Color.Default);
        viewer.SetLines([new StyledText("y", blue)]);
        Assert.Equal(blue, SurfaceText.Render(viewer, 5, 1).Get(0, 0).Style);
        Assert.Equal("y", viewer.Text);
    }

    [Fact]
    public void JsonHighlighter_ColorsKeysStringsNumbersAndLiterals()
    {
        var theme = Theme.Current;
        var spans = TextHighlighters.Json("  \"name\": \"Ada\", \"age\": 36, \"admin\": true, \"x\": null}").Spans;
        Assert.Equal("  \"name\": \"Ada\", \"age\": 36, \"admin\": true, \"x\": null}", string.Concat(spans.Select(s => s.Text)));
        Assert.Equal(theme.Accent, spans.First(s => s.Text == "\"name\"").Style.Foreground);
        Assert.Equal(theme.Success, spans.First(s => s.Text == "\"Ada\"").Style.Foreground);
        Assert.Equal(theme.Info, spans.First(s => s.Text == "36").Style.Foreground);
        Assert.Equal(theme.Secondary, spans.First(s => s.Text == "true").Style.Foreground);
        Assert.Equal(theme.Secondary, spans.First(s => s.Text == "null").Style.Foreground);
        Assert.Equal(theme.Muted, spans.First(s => s.Text == "}").Style.Foreground);

        var escaped = TextHighlighters.Json("\"say \\\"hi\\\"\": 1").Spans;
        Assert.Equal("\"say \\\"hi\\\"\"", escaped[0].Text);
        Assert.Equal(theme.Accent, escaped[0].Style.Foreground);
    }

    [Fact]
    public void XmlHighlighter_ColorsNamesAttributesValuesAndComments()
    {
        var theme = Theme.Current;
        const string line = "<track id=\"7\" title='Blue'>Song</track> <!-- note -->";
        var spans = TextHighlighters.Xml(line).Spans;
        Assert.Equal(line, string.Concat(spans.Select(s => s.Text)));
        Assert.Equal(theme.Accent, spans.First(s => s.Text == "track").Style.Foreground);
        Assert.Equal(theme.Info, spans.First(s => s.Text == "id").Style.Foreground);
        Assert.Equal(theme.Success, spans.First(s => s.Text == "\"7\"").Style.Foreground);
        Assert.Equal(theme.Success, spans.First(s => s.Text == "'Blue'").Style.Foreground);
        Assert.Equal(theme.Foreground, spans.First(s => s.Text == "Song").Style.Foreground);
        Assert.Equal(theme.Muted, spans.First(s => s.Text == "</").Style.Foreground);
        Assert.Equal(theme.Muted, spans.First(s => s.Text == "<!-- note -->").Style.Foreground);

        var cdata = TextHighlighters.Xml("<a><![CDATA[x < y]]></a>").Spans;
        Assert.Contains(cdata, s => s.Text == "x < y" && s.Style.Foreground == theme.Foreground);
        Assert.Equal("<a><![CDATA[x < y]]></a>", string.Concat(cdata.Select(s => s.Text)));
    }

    [Fact]
    public void PrettyPrint_Json_IndentsValidAndKeepsInvalid()
    {
        Assert.Equal("{\n  \"name\": \"Zoë\",\n  \"tags\": [\n    1,\n    2\n  ]\n}", PrettyPrint.Json("{\"name\":\"Zoë\",\"tags\":[1,2]}"));
        Assert.Equal("{\"broken\":", PrettyPrint.Json("{\"broken\":"));
        Assert.Equal("", PrettyPrint.Json(""));
    }

    [Fact]
    public void PrettyPrint_Xml_IndentsAndKeepsDeclarationAndComments()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><library><!-- favorites --><track id=\"1\">Song</track></library>";
        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<library>\n  <!-- favorites -->\n  <track id=\"1\">Song</track>\n</library>",
            PrettyPrint.Xml(xml));
        Assert.Equal("<a><b></a>", PrettyPrint.Xml("<a><b></a>"));
    }

    [Fact]
    public void PrettyPrint_Auto_PicksByTheFirstCharacter()
    {
        Assert.Equal("[\n  1\n]", PrettyPrint.Auto("  [1]"));
        Assert.StartsWith("<a>\n", PrettyPrint.Auto("<a><b /></a>"));
        Assert.Equal("plain text", PrettyPrint.Auto("plain text"));
        Assert.Equal((System.Func<string, StyledText>)TextHighlighters.Json, PrettyPrint.HighlighterFor("{}"));
        Assert.Equal((System.Func<string, StyledText>)TextHighlighters.Xml, PrettyPrint.HighlighterFor("<a/>"));
        Assert.Null(PrettyPrint.HighlighterFor("plain"));
    }

    [Fact]
    public void PrettyPrint_Xml_KeepsSpacesBetweenInlineElements()
    {
        Assert.Equal("<p><b>Hello</b> <b>world</b></p>", PrettyPrint.Xml("<p><b>Hello</b> <b>world</b></p>"));
        Assert.Equal("<a>\n  <b>x</b>\n</a>", PrettyPrint.Xml("<a>\n      <b>x</b>\n</a>"));
        Assert.Equal("<a>\n  <p><b>x</b> y</p>\n</a>", PrettyPrint.Xml("<a><p><b>x</b> y</p></a>"));
    }

    [Fact]
    public void SuppliedLines_KeepTheirStyles_WhenTheHighlighterOrThemeChanges()
    {
        var red = new Style(Color.Rgb(200, 0, 0), Color.Default);
        var viewer = new TextViewer();
        viewer.SetLines([new StyledText("hot", red)]);
        viewer.Highlighter = TextHighlighters.Json;
        Assert.Equal(red.Foreground, SurfaceText.Render(viewer, 10, 1).Get(0, 0).Style.Foreground);

        var previous = Theme.Current;
        try
        {
            Theme.Current = BuiltIn.Light;
            Assert.Equal(red.Foreground, SurfaceText.Render(viewer, 10, 1).Get(0, 0).Style.Foreground);
        }
        finally
        {
            Theme.Current = previous;
        }
    }

    [Fact]
    public void HighlightedText_IsRestyledWhenTheThemeChanges()
    {
        var previous = Theme.Current;
        try
        {
            Theme.Current = BuiltIn.Dark;
            var viewer = new TextViewer { Highlighter = TextHighlighters.Json, Text = "{\"a\": 1}" };
            var before = SurfaceText.Render(viewer, 20, 1).Get(1, 0).Style.Foreground;
            Theme.Current = BuiltIn.Light;
            var after = SurfaceText.Render(viewer, 20, 1).Get(1, 0).Style.Foreground;
            Assert.NotEqual(before, after);
        }
        finally
        {
            Theme.Current = previous;
        }
    }
}
