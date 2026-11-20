using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Widgets;

namespace Cellar.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("ctrl+r", "ctrl+r")]
    [InlineData("Ctrl+R", "ctrl+r")]
    [InlineData("^r", "ctrl+r")]
    [InlineData("?", "?")]
    [InlineData("G", "G")]
    [InlineData("shift+g", "G")]
    [InlineData("enter", "enter")]
    [InlineData("return", "enter")]
    [InlineData("ctrl+m", "enter")]
    [InlineData("shift+tab", "shift+tab")]
    [InlineData("alt+b", "alt+b")]
    [InlineData("alt+shift+left", "alt+shift+left")]
    [InlineData("space", "space")]
    [InlineData("+", "+")]
    [InlineData("ctrl++", "ctrl++")]
    [InlineData("f5", "f5")]
    [InlineData("pgdn", "pgdn")]
    [InlineData("esc", "esc")]
    public void Parse_And_Print_RoundTrip(string text, string printed)
    {
        var chord = KeyChord.Parse(text);
        Assert.Equal(printed, chord.ToString());
        Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ctrl+")]
    [InlineData("hyper+x")]
    [InlineData("nope")]
    public void Parse_RejectsWhatIsNotAKey(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _));
        Assert.Throws<FormatException>(() => KeyChord.Parse(text));
    }

    [Fact]
    public void Matches_EventsAsTerminalsSendThem()
    {
        Assert.True(KeyChord.Parse("ctrl+r").Matches(new KeyEvent(Key.Char, new Rune('r'), KeyModifiers.Control)));
        Assert.True(KeyChord.Parse("G").Matches(new KeyEvent(Key.Char, new Rune('G'), KeyModifiers.None)));
        Assert.True(KeyChord.Parse("G").Matches(new KeyEvent(Key.Char, new Rune('G'), KeyModifiers.Shift)));
        Assert.False(KeyChord.Parse("g").Matches(new KeyEvent(Key.Char, new Rune('G'), KeyModifiers.None)));
        Assert.True(KeyChord.Parse("shift+tab").Matches(new KeyEvent(Key.Tab, default, KeyModifiers.Shift)));
        Assert.False(KeyChord.Parse("tab").Matches(new KeyEvent(Key.Tab, default, KeyModifiers.Shift)));
        Assert.True(KeyChord.Parse("space").Matches(new KeyEvent(Key.Char, new Rune(' '), KeyModifiers.None)));
    }

    [Fact]
    public void ShortString_IsCompact()
    {
        Assert.Equal("^r", KeyChord.Parse("ctrl+r").ToShortString());
        Assert.Equal("bksp", KeyChord.Parse("backspace").ToShortString());
        Assert.Equal("↑", KeyChord.Parse("up").ToShortString());
    }

    [Fact]
    public void Sequence_ParsesSpaceSeparatedChords()
    {
        var sequence = KeySequence.Parse("g g");
        Assert.Equal(2, sequence.Length);
        Assert.Equal("g g", sequence.ToString());
        Assert.Equal(KeySequence.Parse("ctrl+x ctrl+s"), (KeySequence)"^x ^s");
        Assert.Equal("^x ^s", KeySequence.Parse("ctrl+x ctrl+s").ToShortString());
    }
}

public class KeymapTests
{
    private static KeyEvent Key(string chord)
    {
        var c = KeyChord.Parse(chord);
        return new KeyEvent(c.Key, c.Rune, c.Modifiers);
    }

    [Fact]
    public void Handle_RunsTheBoundAction()
    {
        int runs = 0;
        var keymap = new Keymap().Add(new AppAction("refresh", "Refresh", () => runs++) { Keys = ["r", "f5"] });
        Assert.True(keymap.Handle(Key("r")));
        Assert.True(keymap.Handle(Key("f5")));
        Assert.False(keymap.Handle(Key("x")));
        Assert.Equal(2, runs);
    }

    [Fact]
    public void When_HidesAndDisablesAnAction()
    {
        bool available = false;
        int runs = 0;
        var keymap = new Keymap().Add(new AppAction("del", "Delete", () => runs++) { Keys = ["ctrl+d"], When = () => available });
        Assert.False(keymap.Handle(Key("ctrl+d")));
        Assert.Empty(keymap.Actions);
        available = true;
        Assert.True(keymap.Handle(Key("ctrl+d")));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void HigherScope_RebindsAKey_AndReplacesTheSameId()
    {
        var ran = new List<string>();
        var keymap = new Keymap()
            .Add(new AppAction("describe", "Describe", () => ran.Add("app d")) { Keys = ["d"] })
            .Add(new AppAction("help", "Help", () => ran.Add("app help")) { Keys = ["?"] });
        bool pageActive = true;
        var page = keymap.AddScope("page", KeymapScope.PagePrecedence, () => pageActive);
        page.Add(new AppAction("delete", "Delete", () => ran.Add("page d")) { Keys = ["d"] });
        page.Add(new AppAction("help", "Page help", () => ran.Add("page help")) { Keys = ["h"] });

        keymap.Handle(Key("d"));
        keymap.Handle(Key("?")); // the app's help is replaced by the page's, keys and all
        keymap.Handle(Key("h"));
        pageActive = false;
        keymap.Handle(Key("d"));
        keymap.Handle(Key("?"));
        Assert.Equal(["page d", "page help", "app d", "app help"], ran);
    }

    [Fact]
    public void KeysFor_LeavesOutKeysAnotherActionWins()
    {
        var describe = new AppAction("describe", "Describe", () => { }) { Keys = ["d", "i"], Footer = "describe" };
        var keymap = new Keymap().Add(describe);
        bool pageActive = true;
        var page = keymap.AddScope("page", KeymapScope.PagePrecedence, () => pageActive);
        page.Add(new AppAction("delete", "Delete", () => { }) { Keys = ["d"], Footer = "delete" });

        Assert.Equal(["i"], keymap.KeysFor(describe).Select(k => k.ToString()));
        Assert.Equal("i", keymap.PrimaryKey(describe)?.ToString());
        pageActive = false;
        Assert.Equal(["d", "i"], keymap.KeysFor(describe).Select(k => k.ToString()));
    }

    [Fact]
    public void ProviderScope_ReadsItsActionsEachTime()
    {
        var actions = new List<AppAction>();
        var keymap = new Keymap();
        keymap.AddScope("page", () => actions);
        Assert.False(keymap.Handle(Key("x")));
        int runs = 0;
        actions.Add(new AppAction("x", "X", () => runs++) { Keys = ["x"] });
        Assert.True(keymap.Handle(Key("x")));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Sequences_WaitForTheirNextKey()
    {
        var ran = new List<string>();
        var keymap = new Keymap()
            .Add(new AppAction("top", "Top", () => ran.Add("gg")) { Keys = ["g g"] })
            .Add(new AppAction("x", "X", () => ran.Add("x")) { Keys = ["x"] });

        Assert.True(keymap.Handle(Key("g")));
        Assert.Single(keymap.Pending);
        Assert.True(keymap.Handle(Key("g")));
        Assert.Empty(keymap.Pending);

        // A key that doesn't continue the sequence is handled on its own.
        keymap.Handle(Key("g"));
        Assert.True(keymap.Handle(Key("x")));
        Assert.Equal(["gg", "x"], ran);
    }

    [Fact]
    public void Sequences_ExpireAfterTheTimeout()
    {
        var clock = new ManualClock();
        int runs = 0;
        var keymap = new Keymap { Clock = clock, SequenceTimeout = TimeSpan.FromSeconds(1) }
            .Add(new AppAction("top", "Top", () => runs++) { Keys = ["g g"] });
        keymap.Handle(Key("g"));
        clock.Advance(TimeSpan.FromSeconds(2));
        keymap.Handle(Key("g")); // starts a new sequence instead of finishing the old one
        Assert.Equal(0, runs);
        keymap.Handle(Key("g"));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Remap_BindsUnbindsAndReportsProblems()
    {
        var ran = new List<string>();
        var keymap = new Keymap()
            .Add(new AppAction("refresh", "Refresh", () => ran.Add("refresh")) { Keys = ["r"] })
            .Add(new AppAction("quit", "Quit", () => ran.Add("quit")) { Keys = ["q"] });

        var problems = keymap.Remap(new Dictionary<string, string?>
        {
            ["ctrl+r"] = "refresh",
            ["q"] = "",
            ["hyper+x"] = "quit",
            ["x"] = "nope",
        });

        Assert.Equal(2, problems.Count);
        Assert.True(keymap.Handle(Key("ctrl+r")));
        Assert.False(keymap.Handle(Key("q")));
        Assert.Equal(["refresh"], ran);
        var refresh = keymap.Actions.First(a => a.Id == "refresh");
        Assert.Equal(["ctrl+r", "r"], keymap.KeysFor(refresh).Select(k => k.ToString()));
        Assert.Empty(keymap.KeysFor(keymap.Actions.First(a => a.Id == "quit")));
    }

    [Fact]
    public void FindCommand_SplitsTheArgument()
    {
        string? argument = "unset";
        var keymap = new Keymap().Add(new AppAction("cd", "Change directory", a => argument = a) { Commands = ["cd", "chdir"] });

        var match = keymap.FindCommand(":Chdir  my docs ");
        Assert.NotNull(match);
        Assert.Equal("cd", match.Value.Action.Id);
        Assert.Equal("my docs", match.Value.Argument);
        Assert.Null(keymap.FindCommand("nope"));

        Assert.True(keymap.RunCommand("cd"));
        Assert.Null(argument);
        Assert.False(keymap.RunCommand("other"));
    }

    [Fact]
    public void Generators_AgreeWithTheDeclarations()
    {
        var keymap = new Keymap()
            .Add(new AppAction("help", "Keys", () => { }) { Keys = ["?"], Commands = ["help"], Footer = "help", Priority = 1 })
            .Add(new AppAction("quit", "Quit", () => { }) { Keys = ["q", "ctrl+c"], Commands = ["quit", "q"], Footer = "quit", Priority = 5 })
            .Add(new AppAction("down", "Down", () => { }) { Group = "Movement", Keys = ["j"], ShowInPalette = false });

        Assert.Equal(["quit", "help"], keymap.FooterActions().Select(a => a.Id));
        Assert.Equal("q/ctrl+c  :quit", keymap.Hint(keymap.Actions.First(a => a.Id == "quit")));
        Assert.Equal(["Keys", "Quit"], keymap.PaletteCommands().Select(c => c.Title));
        Assert.Equal(["help", "quit", "q"], keymap.CommandNames().Select(c => c.Name));
    }

    [Fact]
    public void Help_ListsGroupsKeysAndTitles()
    {
        var keymap = new Keymap()
            .Add(new AppAction("help", "Keys", () => { }) { Keys = ["?"] })
            .Add(new AppAction("down", "Down", () => { }) { Group = "Movement", Keys = ["j"] })
            .Add(new AppAction("secret", "Secret", () => { }) { Keys = ["z"], ShowInHelp = false });
        var help = keymap.CreateHelp();
        var surface = new Surface(40, 6);
        help.Render(surface, surface.Bounds);
        var text = Enumerable.Range(0, 6).Select(y => Row(surface, y)).ToList();

        Assert.StartsWith("General", text[0]);
        Assert.Contains("?", text[1]);
        Assert.Contains("Keys", text[1]);
        Assert.StartsWith("Movement", text[3]);
        Assert.DoesNotContain(text, line => line.Contains("Secret"));
    }

    [Fact]
    public void ShowHelp_OpensAnOverlay_AndQClosesIt()
    {
        var keymap = new Keymap().Add(new AppAction("help", "Keys", () => { }) { Keys = ["?"] });
        using var app = new App(new FakeTerminal());
        var overlay = keymap.ShowHelp(app);
        Assert.NotNull(overlay);
        Assert.Same(overlay, app.TopOverlay);
        overlay.Content.OnEvent(Key("q"));
        Assert.Null(app.TopOverlay);
    }

    [Fact]
    public void ShowPalette_RunsTheActionAndCloses()
    {
        int runs = 0;
        var keymap = new Keymap().Add(new AppAction("refresh", "Refresh", () => runs++) { Keys = ["r"] });
        using var app = new App(new FakeTerminal());
        var overlay = keymap.ShowPalette(app)!;
        overlay.Content.OnEvent(new KeyEvent(Terminal.Key.Enter, default, KeyModifiers.None));
        Assert.Equal(1, runs);
        Assert.Null(app.TopOverlay);
    }

    private static string Row(Surface s, int y)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < s.Width; x++)
        {
            var g = s.Get(x, y).Grapheme;
            sb.Append(g.Length == 0 ? " " : g);
        }
        return sb.ToString();
    }
}

// A clock tests move by hand.
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by)
    {
        _now += by;
        _ticks += by.Ticks;
    }
}
