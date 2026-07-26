using System.Linq;
using Tessera.Layout;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Widgets;
using System.Text;

namespace Tessera.Tests;

public class InteractiveWidgetTests
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

    private static KeyEvent Char(char c) =>
        new(Key.Char, new Rune(c), KeyModifiers.None);

    // ---- ProgressBar ----

    [Fact]
    public void ProgressBar_ClampsValue()
    {
        var p = new ProgressBar(2.0);
        Assert.Equal(1.0, p.Value);
        p.Value = -1;
        Assert.Equal(0.0, p.Value);
    }

    [Fact]
    public void ProgressBar_HalfFilled()
    {
        var s = new Surface(10, 1);
        new ProgressBar(0.5) { FillColor = Color.Rgb(10, 20, 30), TrackColor = Color.Rgb(40, 40, 40) }
            .Render(s, s.Bounds);
        // Filled cells carry the fill background; track cells carry the track background.
        Assert.Equal(Color.Rgb(10, 20, 30), s.Get(0, 0).Style.Background);
        Assert.Equal(Color.Rgb(40, 40, 40), s.Get(9, 0).Style.Background);
    }

    [Fact]
    public void ProgressBar_ShowsPercent()
    {
        var s = new Surface(10, 1);
        new ProgressBar(0.5) { ShowPercent = true }.Render(s, s.Bounds);
        Assert.Contains("50%", RowText(s, 0));
    }

    [Fact]
    public void ProgressBar_LabelTypes()
    {
        var s = new Surface(30, 1);
        // Bytes label, left.
        new ProgressBar { Current = 5_000_000, Total = 10_000_000,
            LabelPlacement = LabelPlacement.Left, LabelType = ProgressLabel.Bytes }
            .Render(s, s.Bounds);
        var row = RowText(s, 0);
        Assert.Contains("MB", row);
    }

    // ---- Input ----

    [Fact]
    public void Input_TypesCharacters()
    {
        var input = new Input { HasFocus = true };
        input.OnEvent(Char('h'));
        input.OnEvent(Char('i'));
        Assert.Equal("hi", input.Text);
    }

    [Fact]
    public void Input_Backspace()
    {
        var input = new Input { HasFocus = true };
        input.Text = "abc";
        input.OnEvent(new KeyEvent(Key.Backspace, default, KeyModifiers.None));
        Assert.Equal("ab", input.Text);
    }

    [Fact]
    public void Input_CursorMovementAndInsert()
    {
        var input = new Input { HasFocus = true };
        input.Text = "ac";
        input.OnEvent(new KeyEvent(Key.Left, default, KeyModifiers.None)); // between a and c
        input.OnEvent(Char('b'));
        Assert.Equal("abc", input.Text);
    }

    [Fact]
    public void Input_IgnoresInputWhenNotFocused()
    {
        var input = new Input { HasFocus = false };
        Assert.False(input.OnEvent(Char('x')));
        Assert.Equal("", input.Text);
    }

    [Fact]
    public void Input_OnChangeAndSubmitFire()
    {
        string? changed = null, submitted = null;
        var input = new Input { HasFocus = true, OnChange = t => changed = t, OnSubmit = t => submitted = t };
        input.OnEvent(Char('x'));
        Assert.Equal("x", changed);
        input.OnEvent(new KeyEvent(Key.Enter, default, KeyModifiers.None));
        Assert.Equal("x", submitted);
    }

    // ---- Tabs ----

    [Fact]
    public void Tabs_ActiveContentRenders()
    {
        var s = new Surface(30, 5);
        var tabs = new Tabs()
            .Add("one", new Label("first-content"))
            .Add("two", new Label("second-content"));
        tabs.Render(s, s.Bounds);
        var all = string.Join("\n", Enumerable.Range(0, 5).Select(y => RowText(s, y)));
        Assert.Contains("first-content", all);
        Assert.DoesNotContain("second-content", all);
    }

    [Fact]
    public void Tabs_ArrowKeySwitches()
    {
        var tabs = new Tabs { HasFocus = true }
            .Add("a", new Label("A"))
            .Add("b", new Label("B"));
        tabs.OnEvent(new KeyEvent(Key.Right, default, KeyModifiers.None));
        Assert.Equal(1, tabs.ActiveIndex);
        tabs.OnEvent(new KeyEvent(Key.Right, default, KeyModifiers.None)); // wraps
        Assert.Equal(0, tabs.ActiveIndex);
    }

    [Fact]
    public void Tabs_NumberKeyJumps()
    {
        var tabs = new Tabs { HasFocus = true }
            .Add("a", new Label("A"))
            .Add("b", new Label("B"))
            .Add("c", new Label("C"));
        tabs.OnEvent(Char('3'));
        Assert.Equal(2, tabs.ActiveIndex);
    }

    [Fact]
    public void Tabs_ClickSelectsHeader()
    {
        var s = new Surface(40, 3);
        var tabs = new Tabs()
            .Add("alpha", new Label("A"))
            .Add("beta", new Label("B"));
        tabs.Render(s, s.Bounds); // populates hit rects

        // Click somewhere in the second tab header. " 1:alpha " is 9 cells, then a
        // separator, so the "beta" header starts around x=10.
        tabs.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 12, 0, KeyModifiers.None));
        Assert.Equal(1, tabs.ActiveIndex);
    }

    [Fact]
    public void Tabs_ClickBelowBarDoesNotSwitch()
    {
        var s = new Surface(40, 10);
        var tabs = new Tabs()
            .Add("alpha", new Label("A"))
            .Add("beta", new Label("B"));
        tabs.Render(s, s.Bounds);

        // A click in the content area (row 5), same columns as the "beta" header, must
        // NOT change the active tab — only clicks on the bar row (0) select tabs.
        tabs.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 12, 5, KeyModifiers.None));
        Assert.Equal(0, tabs.ActiveIndex);
    }

    // ---- ScrollView ----

    [Fact]
    public void ScrollView_ClampsAndScrolls()
    {
        // A vertical stack of 20 one-line labels inside a 5-tall viewport.
        var stack = new Stack(Direction.Vertical);
        for (int i = 0; i < 20; i++)
        {
            stack.Add(new Label($"line{i}"), Constraint.Length(1));
        }

        var sv = new ScrollView(stack) { HasFocus = true };

        var s = new Surface(20, 5);
        sv.Render(s, s.Bounds); // establishes content/viewport heights

        sv.OnEvent(new KeyEvent(Key.End, default, KeyModifiers.None));
        sv.Render(s, s.Bounds);
        Assert.Equal(15, sv.ScrollOffset); // 20 content - 5 viewport

        sv.OnEvent(new KeyEvent(Key.Home, default, KeyModifiers.None));
        Assert.Equal(0, sv.ScrollOffset);
    }

    [Fact]
    public void ScrollView_ShowsScrolledContent()
    {
        var stack = new Stack(Direction.Vertical);
        for (int i = 0; i < 10; i++)
        {
            stack.Add(new Label($"row{i}"), Constraint.Length(1));
        }

        var sv = new ScrollView(stack) { HasFocus = true, ShowScrollbar = false };

        var s = new Surface(20, 3);
        sv.Render(s, s.Bounds);
        Assert.Contains("row0", RowText(s, 0));

        sv.ScrollOffset = 4;
        sv.Render(s, s.Bounds);
        Assert.Contains("row4", RowText(s, 0));
    }

    [Fact]
    public void ScrollView_DrawsScrollbarWhenOverflowing()
    {
        var stack = new Stack(Direction.Vertical);
        for (int i = 0; i < 30; i++)
        {
            stack.Add(new Label($"x{i}"), Constraint.Length(1));
        }

        var sv = new ScrollView(stack);

        var s = new Surface(20, 5);
        sv.Render(s, s.Bounds);
        // The scrollbar is drawn with background colors (thumb = accent, track = dark), not
        // glyphs. The rightmost column should show at least two distinct backgrounds.
        var backgrounds = Enumerable.Range(0, 5).Select(y => s.Get(19, y).Style.Background).ToHashSet();
        Assert.True(backgrounds.Count >= 2, "scrollbar should show distinct thumb and track backgrounds");
    }
}
