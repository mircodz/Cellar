using System;
using System.Collections.Generic;
using System.Text;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public class CommandLineTests
{
    private static KeyEvent K(Key key, KeyModifiers mods = KeyModifiers.None) => new(key, default, mods);
    private static KeyEvent C(char c) => new(Key.Char, new Rune(c), KeyModifiers.None);

    private static void Type(CommandLine line, string text)
    {
        foreach (var c in text)
        {
            Assert.True(line.Intercept(C(c)));
        }
    }

    private static (Keymap Keymap, List<string> Ran) Commands()
    {
        var ran = new List<string>();
        var keymap = new Keymap();
        keymap.Add(new AppAction("quit", "Quit", () => ran.Add("quit")) { Commands = ["quit", "q"] });
        keymap.Add(new AppAction("ctx", "Switch context", arg => ran.Add("ctx " + arg)) { Commands = ["ctx", "context"], ArgumentHint = "<name>" });
        keymap.Add(new AppAction("cordon", "Cordon node", () => ran.Add("cordon")) { Commands = ["cordon"] });
        return (keymap, ran);
    }

    [Fact]
    public void Closed_ShowsTheStatusLineAndLetsKeysThrough()
    {
        var line = new CommandLine();
        line.Status.Add("3 nodes");
        Assert.False(line.Intercept(C('x')));
        Assert.Equal("3 nodes", SurfaceText.Row(SurfaceText.Render(line, 12), 0).TrimEnd());
    }

    [Fact]
    public void Commands_RunOnEnterAndGoToHistory()
    {
        var (keymap, ran) = Commands();
        var line = new CommandLine();
        var mode = line.CommandsFor(keymap);
        line.Open(mode);
        Type(line, "ctx prod");
        Assert.Equal(":ctx prod", SurfaceText.Row(SurfaceText.Render(line, 20), 0).TrimEnd());

        line.Intercept(K(Key.Enter));
        Assert.False(line.IsOpen);
        Assert.Equal(["ctx prod"], ran);
        Assert.Equal(["ctx prod"], mode.History!.Entries);

        line.Open(mode);
        line.Intercept(K(Key.Up));
        Assert.Equal("ctx prod", line.Input.Text);
    }

    [Fact]
    public void UnknownCommand_ShowsAnError()
    {
        var (keymap, _) = Commands();
        var line = new CommandLine();
        line.Open(line.CommandsFor(keymap));
        Type(line, "nope");
        line.Intercept(K(Key.Enter));
        Assert.Equal(StatusLevel.Error, line.Status.Message?.Level);
        Assert.Contains("nope", SurfaceText.Row(SurfaceText.Render(line, 30), 0), StringComparison.Ordinal);
    }

    [Fact]
    public void Escape_CancelsAndRestoresTheStatus()
    {
        bool cancelled = false;
        var line = new CommandLine();
        line.Open(new CommandMode("/") { OnCancel = () => cancelled = true });
        Type(line, "abc");
        Assert.True(line.Intercept(K(Key.Escape)));
        Assert.True(cancelled);
        Assert.False(line.IsOpen);
        Assert.False(line.Intercept(C('x')));
    }

    [Fact]
    public void OpenPrompt_SwallowsEveryKey()
    {
        var line = new CommandLine();
        line.Open(new CommandMode(":"));
        Assert.True(line.Intercept(K(Key.F5)));
        Assert.True(line.Intercept(new KeyEvent(Key.Char, new Rune('r'), KeyModifiers.Control)));
        Assert.True(line.Intercept(new PasteEvent("hi")));
        Assert.Equal("hi", line.Input.Text);
        Assert.False(line.Intercept(new MouseEvent(MouseEventKind.Move, MouseButton.None, 0, 0, KeyModifiers.None)));
    }

    [Fact]
    public void OnChange_FiltersLive()
    {
        var seen = new List<string>();
        var line = new CommandLine();
        line.Open(new CommandMode("/") { OnChange = seen.Add });
        Type(line, "ab");
        line.Intercept(K(Key.Backspace));
        Assert.Equal(["a", "ab", "a"], seen);
    }

    [Fact]
    public void Ask_RefusesInvalidValuesAndShowsTheErrorAtTheRight()
    {
        string? got = null;
        var line = new CommandLine();
        line.Ask("Replicas:", "", t => int.TryParse(t, out _) ? null : "not a number", t => got = t);

        // No complaint before the user has typed anything.
        Assert.Null(line.Error);
        Assert.Equal("Replicas:", SurfaceText.Row(SurfaceText.Render(line, 30), 0).TrimEnd());

        Type(line, "x");
        line.Intercept(K(Key.Enter));
        Assert.True(line.IsOpen);
        Assert.Null(got);
        var surface = SurfaceText.Render(line, 30);
        Assert.EndsWith("not a number", SurfaceText.Row(surface, 0));
        Assert.Equal(Theming.Theme.Current.Error, surface.Get(29, 0).Style.Foreground);

        line.Intercept(K(Key.Backspace));
        Type(line, "3");
        Assert.Null(line.Error);
        line.Intercept(K(Key.Enter));
        Assert.Equal("3", got);
        Assert.False(line.IsOpen);
    }

    [Fact]
    public void Ask_StartsWithTheInitialValue()
    {
        var line = new CommandLine();
        line.Ask("Name", "web", _ => { });
        Assert.Equal("Name web", SurfaceText.Row(SurfaceText.Render(line, 12), 0).TrimEnd());
        Assert.Equal(3, line.Input.CursorPosition);
    }

    [Fact]
    public void Completion_ShowsGhostTextThatRightArrowAccepts()
    {
        var (keymap, _) = Commands();
        var line = new CommandLine { ShowCompletionPopup = false };
        line.Open(line.CommandsFor(keymap));
        Type(line, "co");
        Assert.Equal("ntext", line.Input.Suggestion);
        Assert.Equal(":context", SurfaceText.Row(SurfaceText.Render(line, 20), 0).TrimEnd());

        line.Intercept(K(Key.Right));
        Assert.Equal("context", line.Input.Text);
    }

    [Fact]
    public void Tab_AcceptsThenCyclesCandidates()
    {
        var (keymap, _) = Commands();
        var line = new CommandLine();
        line.Open(line.CommandsFor(keymap));
        Type(line, "c");
        Assert.Equal(["ctx", "context", "cordon"], Texts(line.Candidates));

        line.Intercept(K(Key.Tab));
        Assert.Equal("ctx", line.Input.Text);
        line.Intercept(K(Key.Tab));
        Assert.Equal("context", line.Input.Text);
        line.Intercept(K(Key.Tab, KeyModifiers.Shift));
        Assert.Equal("ctx", line.Input.Text);
        line.Intercept(K(Key.Tab, KeyModifiers.Shift));
        Assert.Equal("cordon", line.Input.Text);

        // Typing ends the cycle and completes from the new text.
        Type(line, "x");
        Assert.Empty(line.Candidates);
    }

    [Fact]
    public void Completion_CompletesArgumentsAfterASpace()
    {
        var (keymap, ran) = Commands();
        var line = new CommandLine();
        var mode = line.CommandsFor(keymap);
        ((KeymapCompletionSource)mode.Completion!).Arguments = (action, typed) =>
            action.Id == "ctx" ? ["prod-east", "prod-west", "staging"] : [];
        line.Open(mode);
        Type(line, "ctx prod-w");
        Assert.Equal(["ctx prod-west"], Texts(line.Candidates));
        line.Intercept(K(Key.Tab));
        line.Intercept(K(Key.Enter));
        Assert.Equal(["ctx prod-west"], ran);
    }

    [Fact]
    public void Popup_ListsCandidatesAboveTheLine()
    {
        var (keymap, _) = Commands();
        var line = new CommandLine();
        line.Open(line.CommandsFor(keymap));
        Type(line, "c");
        var surface = new Surface(40, 6);
        line.Render(surface, new Rect(0, 5, 40, 1));
        Assert.Contains("ctx", SurfaceText.Row(surface, 1), StringComparison.Ordinal);
        Assert.Contains("Switch context", SurfaceText.Row(surface, 1), StringComparison.Ordinal);
        Assert.Contains("cordon", SurfaceText.Row(surface, 3), StringComparison.Ordinal);
        Assert.StartsWith("╰", SurfaceText.Row(surface, 4).TrimStart());
        Assert.Equal(":ctx", SurfaceText.Row(surface, 5).TrimEnd());
    }

    [Fact]
    public void Words_CompletesFromAList()
    {
        var source = CompletionSource.Words(["alpha", "Beta", "alps"]);
        Assert.Equal(["alpha", "alps"], Texts(source.Complete("AL")));
        Assert.Equal(["Beta"], Texts(source.Complete("b")));
    }

    private static List<string> Texts(IReadOnlyList<CompletionItem> items)
    {
        var texts = new List<string>();
        foreach (var item in items)
        {
            texts.Add(item.Text);
        }

        return texts;
    }
}
