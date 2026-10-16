using System.Text;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Widgets;
using Cellar.Widgets.Charts.Trees;

namespace Cellar.Tests;

public class MouseRoutingTests
{
    private static MouseEvent Wheel(int x, int y, bool up = true) =>
        new(MouseEventKind.Wheel, up ? MouseButton.WheelUp : MouseButton.WheelDown, x, y, KeyModifiers.None);

    private static MouseEvent Move(int x, int y) =>
        new(MouseEventKind.Move, MouseButton.None, x, y, KeyModifiers.None);

    private static MouseEvent Down(int x, int y) =>
        new(MouseEventKind.Down, MouseButton.Left, x, y, KeyModifiers.None);

    private static MouseEvent Up(int x, int y) =>
        new(MouseEventKind.Up, MouseButton.None, x, y, KeyModifiers.None);

    // ---- Positional mouse routing in Stack ----

    // A widget that records which mouse events reached it.
    private sealed class Probe : Widget
    {
        public int MouseCount;
        public int KeyCount;
        public override bool IsFocusable => true;
        public override void Render(Surface surface, Rect area) { }
        public override bool OnEvent(InputEvent e)
        {
            if (e is MouseEvent) { MouseCount++; return true; }
            if (e is KeyEvent) { KeyCount++; return true; }
            return false;
        }
    }

    [Fact]
    public void Stack_RoutesMouseToChildUnderCursor_NotFocused()
    {
        var a = new Probe();
        var b = new Probe();
        var stack = new Stack(Direction.Vertical)
            .Add(a, Constraint.Length(3))
            .Add(b, Constraint.Length(3));
        stack.HasFocus = true; // focus lands on the first focusable child (a)

        var s = new Surface(10, 6);
        stack.Render(s, s.Bounds);

        // A wheel event at y=4 is over child b (rows 3-5), not the focused child a (rows 0-2).
        stack.OnEvent(Wheel(2, 4));
        Assert.Equal(0, a.MouseCount);
        Assert.Equal(1, b.MouseCount);
    }

    [Fact]
    public void Stack_RoutesKeyboardToFocusedChild_RegardlessOfCursor()
    {
        var a = new Probe();
        var b = new Probe();
        var stack = new Stack(Direction.Vertical)
            .Add(a, Constraint.Length(3))
            .Add(b, Constraint.Length(3));
        stack.HasFocus = true; // focuses a

        var s = new Surface(10, 6);
        stack.Render(s, s.Bounds);

        stack.OnEvent(new KeyEvent(Key.Down, default, KeyModifiers.None));
        Assert.Equal(1, a.KeyCount);
        Assert.Equal(0, b.KeyCount);
    }

    [Fact]
    public void Stack_MouseOutsideAllChildren_NotHandled()
    {
        var a = new Probe();
        var stack = new Stack(Direction.Vertical).Add(a, Constraint.Length(2));
        var s = new Surface(10, 6);
        stack.Render(s, s.Bounds);
        Assert.False(stack.OnEvent(Wheel(2, 5))); // below child a's 2 rows
        Assert.Equal(0, a.MouseCount);
    }

    // ---- Link hover ----

    private static TreeView<string> LinkTree()
    {
        // Root label: leaf marker (x0-1) + "go:" (x2-4) + link "X" at x5.
        var tree = new TreeView<string>
        {
            RenderLabel = _ => StyledText.Of("go:").Append("X").Link("payload"),
        };
        tree.AddRoot("root");
        return tree;
    }

    [Fact]
    public void Hover_FiresOnEnterAndLeave()
    {
        var tree = LinkTree();
        object? hovered = "unset";
        int fires = 0;
        tree.OnLinkHover = p => { hovered = p; fires++; };

        var s = new Surface(20, 3);
        tree.Render(s, s.Bounds);

        tree.OnEvent(Move(5, 0));   // enter the link
        Assert.Equal("payload", hovered);
        tree.OnEvent(Move(3, 0));   // leave onto plain text
        Assert.Null(hovered);
        Assert.Equal(2, fires);
    }

    [Fact]
    public void Hover_NoRefireWithinSameLink()
    {
        // The link is a single cell here, so widen it: "go:" + "XY" link.
        var tree = new TreeView<string>
        {
            RenderLabel = _ => StyledText.Of("go:").Append("XY").Link("p"),
        };
        tree.AddRoot("root");
        int fires = 0;
        tree.OnLinkHover = _ => fires++;

        var s = new Surface(20, 3);
        tree.Render(s, s.Bounds);

        tree.OnEvent(Move(5, 0)); // enter link (x5)
        tree.OnEvent(Move(6, 0)); // still within link (x6) — no refire
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Hover_MatchesReboxedValuePayload()
    {
        // Regression: a value-type payload (long) is re-boxed to a fresh object every render, so
        // hover matching must use value equality, not reference equality.
        long addr = 0xABCD;
        var tree = new TreeView<long>
        {
            RenderLabel = n => StyledText.Of("go:").Append("LINK").Link(n.Value),
            Striped = true,
        };
        tree.AddRoot(addr);
        var s = new Surface(20, 3);
        tree.Render(s, s.Bounds);            // render 1: boxes addr -> A
        tree.OnEvent(Move(6, 0));            // hover stores box A
        tree.Render(s, s.Bounds);            // render 2: boxes addr -> B; emphasis must still match
        bool bold = (s.Get(6, 0).Style.Attributes & TextAttributes.Bold) != 0;
        Assert.True(bold, "hovered link must stay emphasized across re-renders with a value payload");
    }

    [Fact]
    public void Hover_UsesPerLinkStyleWhenProvided()
    {
        var custom = new Style(Color.Rgb(255, 0, 0), Color.Rgb(0, 0, 255));
        var tree = new TreeView<int>
        {
            RenderLabel = _ => StyledText.Of("go:").Append("LINK").Link(1, custom),
        };
        tree.AddRoot(0);
        var s = new Surface(20, 3);
        tree.Render(s, s.Bounds);
        tree.OnEvent(Move(6, 0));
        tree.Render(s, s.Bounds);
        var c = s.Get(6, 0);
        Assert.Equal(Color.Rgb(255, 0, 0), c.Style.Foreground);
        Assert.Equal(Color.Rgb(0, 0, 255), c.Style.Background);
    }

    [Fact]
    public void Label_LinkHoverAndClick()
    {
        object? clicked = null;
        var lbl = new Label(StyledText.Of("go ").Append("X").Link("docs",
            new Style(Color.Rgb(255, 0, 0), Color.Rgb(0, 0, 255))));
        lbl.OnLinkClick = p => clicked = p;
        var s = new Surface(20, 1);
        lbl.Render(s, s.Bounds);
        lbl.OnEvent(Move(3, 0));         // hover the link "X" at x3
        lbl.Render(s, s.Bounds);
        Assert.Equal(Color.Rgb(0, 0, 255), s.Get(3, 0).Style.Background);
        lbl.OnEvent(Down(3, 0));
        Assert.Equal("docs", clicked);
    }
}

public class ButtonTests
{
    private static MouseEvent Down(int x, int y) =>
        new(MouseEventKind.Down, MouseButton.Left, x, y, KeyModifiers.None);
    private static MouseEvent Up(int x, int y) =>
        new(MouseEventKind.Up, MouseButton.None, x, y, KeyModifiers.None);
    private static MouseEvent Move(int x, int y) =>
        new(MouseEventKind.Move, MouseButton.None, x, y, KeyModifiers.None);

    private static Button Rendered(out Surface s, System.Action onClick)
    {
        var b = new Button("Quit", onClick);
        s = new Surface(10, 1);
        b.Render(s, new Rect(0, 0, 8, 1));
        return b;
    }

    [Fact]
    public void Click_InsideBounds_FiresOnClick()
    {
        int clicks = 0;
        var b = Rendered(out _, () => clicks++);
        b.OnEvent(Down(2, 0));
        b.OnEvent(Up(2, 0));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Release_OutsideBounds_DoesNotFire()
    {
        int clicks = 0;
        var b = Rendered(out _, () => clicks++);
        b.OnEvent(Down(2, 0));
        b.OnEvent(Up(9, 0)); // released off the button (bounds width 8)
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void EnterAndSpace_WhenFocused_FireOnClick()
    {
        int clicks = 0;
        var b = Rendered(out _, () => clicks++);
        b.HasFocus = true;
        b.OnEvent(new KeyEvent(Key.Enter, default, KeyModifiers.None));
        b.OnEvent(new KeyEvent(Key.Char, new Rune(' '), KeyModifiers.None));
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void Move_TogglesHover_AndRequestsRepaint()
    {
        var b = Rendered(out _, () => { });
        Assert.True(b.OnEvent(Move(2, 0)));   // enter → hovered, repaint
        Assert.False(b.OnEvent(Move(3, 0)));  // still inside, no change
        Assert.True(b.OnEvent(Move(20, 0)));  // leave → repaint
    }

    [Fact]
    public void Hover_OnTheSelectedRow_KeepsTheSelectionColors()
    {
        var tree = new TreeView<int> { RenderLabel = _ => StyledText.Of("go:").Append("LINK").Link(1) };
        tree.AddRoot(0); // the only row, so it's selected
        var s = new Surface(20, 3);
        tree.Render(s, s.Bounds);
        var selected = s.Get(6, 0).Style;
        Assert.Equal(Theming.Theme.Current.SelectionBackground, selected.Background);

        tree.OnEvent(Move(6, 0));
        tree.Render(s, s.Bounds);
        var hovered = s.Get(6, 0).Style;
        Assert.Equal(selected.Foreground, hovered.Foreground);
        Assert.Equal(selected.Background, hovered.Background);
        Assert.True(hovered.Attributes.HasFlag(TextAttributes.Bold | TextAttributes.Underline));
    }
}
