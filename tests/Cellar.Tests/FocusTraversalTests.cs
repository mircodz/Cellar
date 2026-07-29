using Cellar.Layout;
using Cellar.Terminal;
using Cellar.Widgets;

namespace Cellar.Tests;

public class FocusTraversalTests
{
    private static KeyEvent Char(char c) => new(Key.Char, new System.Text.Rune(c), KeyModifiers.None);
    private static KeyEvent Tab(bool shift = false) =>
        new(Key.Tab, default, shift ? KeyModifiers.Shift : KeyModifiers.None);

    [Fact]
    public void OnlyFocusedInput_ReceivesTyping()
    {
        var a = new Input();
        var b = new Input();
        var stack = new Stack(Direction.Vertical)
            .Add(a, Constraint.Length(1))
            .Add(b, Constraint.Length(1));
        stack.HasFocus = true; // focuses the first focusable child (a)

        stack.OnEvent(Char('x'));
        Assert.Equal("x", a.Text);
        Assert.Equal("", b.Text); // b did NOT receive it
    }

    [Fact]
    public void Tab_MovesFocusToNextInput()
    {
        var a = new Input();
        var b = new Input();
        var stack = new Stack(Direction.Vertical)
            .Add(a, Constraint.Length(1))
            .Add(b, Constraint.Length(1));
        stack.HasFocus = true;

        Assert.True(a.HasFocus);
        stack.OnEvent(Tab());
        Assert.False(a.HasFocus);
        Assert.True(b.HasFocus);

        stack.OnEvent(Char('y'));
        Assert.Equal("y", b.Text);
        Assert.Equal("", a.Text);
    }

    [Fact]
    public void ShiftTab_MovesFocusBackward()
    {
        var a = new Input();
        var b = new Input();
        var stack = new Stack(Direction.Vertical)
            .Add(a, Constraint.Length(1))
            .Add(b, Constraint.Length(1));
        stack.HasFocus = true;

        stack.OnEvent(Tab());          // a -> b
        stack.OnEvent(Tab(shift: true)); // b -> a
        Assert.True(a.HasFocus);
        Assert.False(b.HasFocus);
    }

    [Fact]
    public void Tab_WrapsAround()
    {
        var a = new Input();
        var b = new Input();
        var stack = new Stack(Direction.Vertical)
            .Add(a, Constraint.Length(1))
            .Add(b, Constraint.Length(1));
        stack.HasFocus = true;

        stack.OnEvent(Tab()); // a -> b
        stack.OnEvent(Tab()); // b -> a (wrap)
        Assert.True(a.HasFocus);
    }

    [Fact]
    public void NonFocusableChildren_Skipped()
    {
        var label = new Label("just text");
        var input = new Input();
        var stack = new Stack(Direction.Vertical)
            .Add(label, Constraint.Length(1))
            .Add(input, Constraint.Length(1));
        stack.HasFocus = true;

        Assert.True(input.HasFocus); // focus skips the label, lands on the input
        stack.OnEvent(Char('z'));
        Assert.Equal("z", input.Text);
    }

    [Fact]
    public void SingleFocusable_TabBubblesUp()
    {
        var input = new Input();
        var stack = new Stack(Direction.Vertical)
            .Add(new Label("x"), Constraint.Length(1))
            .Add(input, Constraint.Length(1));
        stack.HasFocus = true;

        // With only one focusable child, Tab should NOT be consumed (returns false so an
        // ancestor like Tabs can use it to switch tabs).
        Assert.False(stack.OnEvent(Tab()));
    }

    [Fact]
    public void ClearingFocus_RemovesChildFocus()
    {
        var input = new Input();
        var stack = new Stack(Direction.Vertical).Add(input, Constraint.Length(1));
        stack.HasFocus = true;
        Assert.True(input.HasFocus);
        stack.HasFocus = false;
        Assert.False(input.HasFocus);
    }
}
