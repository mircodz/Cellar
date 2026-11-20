using System.Text;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Widgets;

namespace Cellar.Tests;

public class InputEditingTests
{
    private static KeyEvent Ctrl(char c) => new(Key.Char, new Rune(c), KeyModifiers.Control);
    private static KeyEvent Alt(char c) => new(Key.Char, new Rune(c), KeyModifiers.Alt);
    private static KeyEvent Named(Key key, KeyModifiers mods = KeyModifiers.None) => new(key, default, mods);
    private static KeyEvent Char(char c) => new(Key.Char, new Rune(c), KeyModifiers.None);

    private static Input Field(string text, int? cursor = null)
    {
        var input = new Input { HasFocus = true, Text = text };
        if (cursor is { } c)
        {
            input.CursorPosition = c;
        }
        return input;
    }

    [Fact]
    public void CtrlA_And_CtrlE_MoveToStartAndEnd()
    {
        var input = Field("hello");
        input.OnEvent(Ctrl('a'));
        Assert.Equal(0, input.CursorPosition);
        input.OnEvent(Ctrl('e'));
        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void CtrlW_DeletesBackToWhitespace()
    {
        var input = Field("ls --all /var/log");
        input.OnEvent(Ctrl('w'));
        Assert.Equal("ls --all ", input.Text);
        input.OnEvent(Ctrl('w'));
        Assert.Equal("ls ", input.Text);
    }

    [Fact]
    public void AltBackspace_DeletesOneWordOfLettersAndDigits()
    {
        var input = Field("open /var/log");
        input.OnEvent(Named(Key.Backspace, KeyModifiers.Alt));
        Assert.Equal("open /var/", input.Text);
    }

    [Fact]
    public void CtrlU_And_CtrlK_DeleteToStartAndEnd()
    {
        var input = Field("abc def", cursor: 3);
        input.OnEvent(Ctrl('k'));
        Assert.Equal("abc", input.Text);

        input = Field("abc def", cursor: 3);
        input.OnEvent(Ctrl('u'));
        Assert.Equal(" def", input.Text);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void CtrlY_PastesWhatWasLastDeleted()
    {
        var input = Field("one two");
        input.OnEvent(Ctrl('w'));
        input.OnEvent(Ctrl('a'));
        input.OnEvent(Ctrl('y'));
        Assert.Equal("twoone ", input.Text);
    }

    [Fact]
    public void WordMotions_AltAndCtrlArrows()
    {
        var input = Field("foo bar-baz");
        input.OnEvent(Alt('b'));
        Assert.Equal(8, input.CursorPosition); // start of "baz"
        input.OnEvent(Named(Key.Left, KeyModifiers.Control));
        Assert.Equal(4, input.CursorPosition); // start of "bar"
        input.OnEvent(Alt('f'));
        Assert.Equal(7, input.CursorPosition); // end of "bar"
        input.OnEvent(Named(Key.Right, KeyModifiers.Control));
        Assert.Equal(11, input.CursorPosition);
    }

    [Fact]
    public void AltD_DeletesTheNextWord()
    {
        var input = Field("foo bar baz", cursor: 3);
        input.OnEvent(Alt('d'));
        Assert.Equal("foo baz", input.Text);
        Assert.Equal(3, input.CursorPosition);
    }

    [Fact]
    public void MaskedField_WordMotionsDoNotRevealWords()
    {
        var input = new Input { HasFocus = true, Mask = '*', Text = "pass word" };
        input.OnEvent(Ctrl('w'));
        Assert.Equal("pass ", input.Text); // ctrl+w still works on whitespace like readline
        input.Text = "pass word";
        input.OnEvent(Alt('b'));
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void ReadlineKeys_RaiseOnChange()
    {
        string? changed = null;
        var input = Field("abc def");
        input.OnChange = t => changed = t;
        input.OnEvent(Ctrl('w'));
        Assert.Equal("abc ", changed);
    }

    [Fact]
    public void UnknownControlKeys_AreLeftForTheApp()
    {
        var input = Field("abc");
        Assert.False(input.OnEvent(Ctrl('p')));
        Assert.False(input.OnEvent(Ctrl('r')));
    }

    [Fact]
    public void Paste_InsertsTextOnOneLine()
    {
        var input = Field("a");
        input.OnEvent(new PasteEvent("b\r\nc\td"));
        Assert.Equal("ab c d", input.Text);
    }

    [Fact]
    public void Decoder_EscDel_IsAltBackspace()
    {
        var events = new InputDecoder().Feed([0x1b, 0x7f]);
        var key = Assert.IsType<KeyEvent>(Assert.Single(events));
        Assert.Equal(Key.Backspace, key.Key);
        Assert.Equal(KeyModifiers.Alt, key.Modifiers);
    }

    [Fact]
    public void Decoder_CtrlArrow_And_AltLetter()
    {
        var events = new InputDecoder().Feed(Encoding.ASCII.GetBytes("\x1b[1;5D\x1b" + "b"));
        Assert.Equal(new KeyEvent(Key.Left, default, KeyModifiers.Control), events[0]);
        Assert.Equal(new KeyEvent(Key.Char, new Rune('b'), KeyModifiers.Alt), events[1]);
    }

    // ---- History ----

    [Fact]
    public void History_UpAndDown_BrowseAndKeepTheDraft()
    {
        var history = new InputHistory();
        history.Load(["first", "second"]);
        var input = new Input { HasFocus = true, History = history };
        input.OnEvent(Char('d'));

        input.OnEvent(Named(Key.Up));
        Assert.Equal("second", input.Text);
        input.OnEvent(Named(Key.Up));
        Assert.Equal("first", input.Text);
        input.OnEvent(Named(Key.Up)); // stays on the oldest
        Assert.Equal("first", input.Text);
        input.OnEvent(Named(Key.Down));
        input.OnEvent(Named(Key.Down));
        Assert.Equal("d", input.Text);
    }

    [Fact]
    public void History_EnterRecordsTheEntry()
    {
        var history = new InputHistory();
        string? saved = null;
        history.OnAdd = e => saved = e;
        var input = new Input { HasFocus = true, History = history, Text = "go" };
        input.OnEvent(Named(Key.Enter));
        Assert.Equal(["go"], history.Entries);
        Assert.Equal("go", saved);
    }

    [Fact]
    public void History_WithoutEntries_LeavesArrowsToTheContainer()
    {
        var input = new Input { HasFocus = true };
        Assert.False(input.OnEvent(Named(Key.Up)));
        input.History = new InputHistory();
        Assert.False(input.OnEvent(Named(Key.Up)));
    }

    [Fact]
    public void History_SkipsBlanksAndMovesDuplicatesToTheEnd()
    {
        var history = new InputHistory(capacity: 3);
        history.Add("a");
        history.Add(" ");
        history.Add("b");
        history.Add("a");
        Assert.Equal(["b", "a"], history.Entries);
        history.Add("c");
        history.Add("d");
        Assert.Equal(["a", "c", "d"], history.Entries);
    }

    [Fact]
    public void Suggestion_IsDrawnAfterTheTextOnlyAtTheEnd()
    {
        var surface = new Surface(10, 1);
        var input = Field("ap");
        input.Suggestion = "ps";
        input.Render(surface, surface.Bounds);
        Assert.Equal("a", surface.Get(0, 0).Grapheme);
        Assert.Equal("s", surface.Get(3, 0).Grapheme);
        Assert.Equal(Theming.Theme.Current.Muted, surface.Get(3, 0).Style.Foreground);

        surface.Clear(Primitives.Style.Default);
        input.CursorPosition = 0;
        input.Render(surface, surface.Bounds);
        Assert.NotEqual("s", surface.Get(3, 0).Grapheme);
    }

    [Fact]
    public void MaskedField_KillsWithoutKeepingTheSecret()
    {
        var input = Field("hunter2");
        input.Mask = '*';
        input.OnEvent(Ctrl('u'));
        Assert.Equal(string.Empty, input.Text);
        input.OnEvent(Ctrl('y'));
        Assert.Equal(string.Empty, input.Text);
    }
}
