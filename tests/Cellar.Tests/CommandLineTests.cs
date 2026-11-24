using System.Linq;
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
        keymap.Add(new AppAction("cd", "Change directory", arg => ran.Add("cd " + arg)) { Commands = ["cd", "chdir"], ArgumentHint = "<name>" });
        keymap.Add(new AppAction("copy", "Copy files", () => ran.Add("copy")) { Commands = ["copy"] });
        return (keymap, ran);
    }

    [Fact]
    public void Closed_ShowsTheStatusLineAndLetsKeysThrough()
    {
        var line = new CommandLine();
        line.Status.Add("3 files");
        Assert.False(line.Intercept(C('x')));
        Assert.Equal("3 files", SurfaceText.Row(SurfaceText.Render(line, 12), 0).TrimEnd());
    }

    [Fact]
    public void Commands_RunOnEnterAndGoToHistory()
    {
        var (keymap, ran) = Commands();
        var line = new CommandLine();
        var mode = line.CommandsFor(keymap);
        line.Open(mode);
        Type(line, "cd docs");
        Assert.Equal(":cd docs", SurfaceText.Row(SurfaceText.Render(line, 20), 0).TrimEnd());

        line.Intercept(K(Key.Enter));
        Assert.False(line.IsOpen);
        Assert.Equal(["cd docs"], ran);
        Assert.Equal(["cd docs"], mode.History!.Entries);

        line.Open(mode);
        line.Intercept(K(Key.Up));
        Assert.Equal("cd docs", line.Input.Text);
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
        line.Ask("Retries:", "", t => int.TryParse(t, out _) ? null : "not a number", t => got = t);

        // No complaint before the user has typed anything.
        Assert.Null(line.Error);
        Assert.Equal("Retries:", SurfaceText.Row(SurfaceText.Render(line, 30), 0).TrimEnd());

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
        Type(line, "ch");
        Assert.Equal("dir", line.Input.Suggestion);
        Assert.Equal(":chdir", SurfaceText.Row(SurfaceText.Render(line, 20), 0).TrimEnd());

        line.Intercept(K(Key.Right));
        Assert.Equal("chdir", line.Input.Text);
    }

    [Fact]
    public void Tab_AcceptsThenCyclesCandidates()
    {
        var (keymap, _) = Commands();
        var line = new CommandLine();
        line.Open(line.CommandsFor(keymap));
        Type(line, "c");
        Assert.Equal(["cd", "chdir", "copy"], Texts(line.Candidates));

        line.Intercept(K(Key.Tab));
        Assert.Equal("cd", line.Input.Text);
        line.Intercept(K(Key.Tab));
        Assert.Equal("chdir", line.Input.Text);
        line.Intercept(K(Key.Tab, KeyModifiers.Shift));
        Assert.Equal("cd", line.Input.Text);
        line.Intercept(K(Key.Tab, KeyModifiers.Shift));
        Assert.Equal("copy", line.Input.Text);

        // Typing ends the cycle and completes from the new text.
        Type(line, "x");
        Assert.Empty(line.Candidates);
    }

    [Fact]
    public void Tab_OnALoneCandidate_CompletesAgainFromIt()
    {
        var tree = new Dictionary<string, string[]>
        {
            ["~/p"] = ["~/pkg/"],
            ["~/pkg/"] = ["~/pkg/app.sfpkg", "~/pkg/more/"],
        };
        var line = new CommandLine();
        line.Open(new CommandMode(":") { Completion = CompletionSource.From(t => (tree.TryGetValue(t, out var c) ? c : []).Select(x => new CompletionItem(x))) });
        Type(line, "~/p");

        // One candidate: accepted, and the next tab looks inside it, like a shell.
        line.Intercept(K(Key.Tab));
        Assert.Equal("~/pkg/", line.Input.Text);
        Assert.Equal(["~/pkg/app.sfpkg", "~/pkg/more/"], Texts(line.Candidates));

        // Several: tab cycles through them as before.
        line.Intercept(K(Key.Tab));
        Assert.Equal("~/pkg/app.sfpkg", line.Input.Text);
        line.Intercept(K(Key.Tab));
        Assert.Equal("~/pkg/more/", line.Input.Text);
    }

    [Fact]
    public void Completion_CompletesArgumentsAfterASpace()
    {
        var (keymap, ran) = Commands();
        var line = new CommandLine();
        var mode = line.CommandsFor(keymap);
        ((KeymapCompletionSource)mode.Completion!).Arguments = (action, typed) =>
            action.Id == "cd" ? ["docs-old", "docs-new", "music"] : [];
        line.Open(mode);
        Type(line, "cd docs-n");
        Assert.Equal(["cd docs-new"], Texts(line.Candidates));
        line.Intercept(K(Key.Tab));
        line.Intercept(K(Key.Enter));
        Assert.Equal(["cd docs-new"], ran);
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
        Assert.Contains("cd", SurfaceText.Row(surface, 1), StringComparison.Ordinal);
        Assert.Contains("Change directory", SurfaceText.Row(surface, 1), StringComparison.Ordinal);
        Assert.Contains("copy", SurfaceText.Row(surface, 3), StringComparison.Ordinal);
        Assert.StartsWith("╰", SurfaceText.Row(surface, 4).TrimStart());
        Assert.Equal(":cd", SurfaceText.Row(surface, 5).TrimEnd());
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

    [Fact]
    public void EmptyField_ShowsTheModePlaceholder()
    {
        var line = new CommandLine();
        line.Open(new CommandMode("/") { Placeholder = "search" });
        Assert.Equal("/search", SurfaceText.Row(SurfaceText.Render(line, 20), 0).TrimEnd());
        Type(line, "x");
        Assert.Equal("/x", SurfaceText.Row(SurfaceText.Render(line, 20), 0).TrimEnd());
    }

    [Fact]
    public void Popup_ShrinksToTheRoomAboveTheLine()
    {
        var (keymap, _) = Commands();
        var line = new CommandLine();
        line.Open(line.CommandsFor(keymap));
        Type(line, "c");
        var surface = new Surface(40, 4);
        line.Render(surface, new Rect(0, 3, 40, 1));
        Assert.StartsWith("╭", SurfaceText.Row(surface, 0).TrimStart());
        Assert.Contains("cd", SurfaceText.Row(surface, 1), StringComparison.Ordinal);
        Assert.StartsWith("╰", SurfaceText.Row(surface, 2).TrimStart());
        Assert.Equal(":cd", SurfaceText.Row(surface, 3).TrimEnd());
    }

    [Fact]
    public void Popup_DrawsOutsideTheLinesOwnClip()
    {
        var (keymap, _) = Commands();
        var line = new CommandLine();
        line.Open(line.CommandsFor(keymap));
        Type(line, "c");
        var surface = new Surface(40, 6);
        surface.SetClip(new Rect(0, 5, 40, 1));
        line.Render(surface, new Rect(0, 5, 40, 1));
        Assert.Contains("Change directory", SurfaceText.Row(surface, 1), StringComparison.Ordinal);
        Assert.Equal(new Rect(0, 5, 40, 1), surface.Clip);
    }

    [Fact]
    public void RightArrow_AcceptsTheCandidateAsWritten()
    {
        var line = new CommandLine { ShowCompletionPopup = false };
        line.Open(new CommandMode(":") { Completion = CompletionSource.Words(["Beta", "😀bc"]) });
        Type(line, "b");
        line.Intercept(K(Key.Right));
        Assert.Equal("Beta", line.Input.Text);

        line.Input.Text = string.Empty;
        line.Intercept(new KeyEvent(Key.Char, new Rune(0x1F600), KeyModifiers.None));
        line.Intercept(K(Key.Right));
        Assert.Equal("😀bc", line.Input.Text);
    }
}
