using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Widgets;

namespace Cellar.Tests;

public class NavigatorTests
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

    private static KeyEvent Esc() => new(Key.Escape, default, KeyModifiers.None);

    [Fact]
    public void Push_ShowsNewPage()
    {
        var nav = new Navigator(new Page("Home", new Label("home content")));
        nav.Push("Detail", new Label("detail content"));

        var s = new Surface(40, 5);
        nav.Render(s, s.Bounds);
        var all = string.Join("\n", Enumerable.Range(0, 5).Select(y => RowText(s, y)));
        Assert.Contains("detail content", all);
        Assert.DoesNotContain("home content", all);
    }

    [Fact]
    public void Pop_ReturnsToPreviousPage()
    {
        var nav = new Navigator(new Page("Home", new Label("home")));
        nav.Push("Detail", new Label("detail"));
        Assert.Equal(2, nav.Depth);

        var popped = nav.Pop();
        Assert.Equal("Detail", popped!.Title);
        Assert.Equal("Home", nav.Current!.Title);
        Assert.Equal(1, nav.Depth);
    }

    [Fact]
    public void Pop_CannotPopRoot()
    {
        var nav = new Navigator(new Page("Home", new Label("home")));
        Assert.Null(nav.Pop());     // nothing to pop
        Assert.Equal(1, nav.Depth); // root stays
    }

    [Fact]
    public void BackKey_PopsCurrentPage()
    {
        var nav = new Navigator(new Page("Home", new Label("home")));
        nav.Push("Detail", new Label("detail"));
        Assert.True(nav.OnEvent(Esc()));
        Assert.Equal("Home", nav.Current!.Title);
    }

    [Fact]
    public void BackKey_AtRoot_NotConsumed()
    {
        var nav = new Navigator(new Page("Home", new Label("home")));
        // At the root there's nothing to pop, so the back key should fall through.
        Assert.False(nav.OnEvent(Esc()));
    }

    [Fact]
    public void Breadcrumb_ShowsPageTitles()
    {
        var nav = new Navigator(new Page("Snapshots", new Label("x")));
        nav.Push("heap.snap", new Label("x"));
        nav.Push("System.String", new Label("x"));

        var s = new Surface(60, 3);
        nav.Render(s, s.Bounds);
        var crumb = RowText(s, 0);
        Assert.Contains("Snapshots", crumb);
        Assert.Contains("heap.snap", crumb);
        Assert.Contains("System.String", crumb);
        Assert.Contains("›", crumb); // separator
    }

    [Fact]
    public void PopToRoot_ClearsToRoot()
    {
        var nav = new Navigator(new Page("Home", new Label("x")));
        nav.Push("A", new Label("x"));
        nav.Push("B", new Label("x"));
        nav.PopToRoot();
        Assert.Equal(1, nav.Depth);
        Assert.Equal("Home", nav.Current!.Title);
    }

    [Fact]
    public void Push_FocusesNewPageContent()
    {
        var home = new Input();
        var detail = new Input();
        var nav = new Navigator(new Page("Home", home));
        nav.Push("Detail", detail);
        Assert.True(detail.HasFocus);
        Assert.False(home.HasFocus);
    }

    [Fact]
    public void Input_RoutesToCurrentPage()
    {
        var input = new Input();
        var nav = new Navigator(new Page("Home", new Label("x")));
        nav.Push("Edit", input);
        input.HasFocus = true;

        nav.OnEvent(new KeyEvent(Key.Char, new System.Text.Rune('h'), KeyModifiers.None));
        Assert.Equal("h", input.Text);
    }

    [Fact]
    public void OnNavigate_FiresOnPushAndPop()
    {
        var titles = new List<string?>();
        var nav = new Navigator(new Page("Home", new Label("x")))
        {
            OnNavigate = p => titles.Add(p?.Title),
        };
        nav.Push("A", new Label("x"));
        nav.Pop();
        Assert.Equal(new[] { "A", "Home" }, titles);
    }

    [Fact]
    public void PopTo_JumpsBackToLevel()
    {
        var nav = new Navigator(new Page("Home", new Label("x")));
        nav.Push("A", new Label("x"));
        nav.Push("B", new Label("x"));
        nav.Push("C", new Label("x"));
        Assert.Equal(4, nav.Depth);

        nav.PopTo(1); // back to "A"
        Assert.Equal(2, nav.Depth);
        Assert.Equal("A", nav.Current!.Title);
    }

    [Fact]
    public void PopTo_TopOrInvalid_NoOp()
    {
        var nav = new Navigator(new Page("Home", new Label("x")));
        nav.Push("A", new Label("x"));
        nav.PopTo(1);  // already the top
        Assert.Equal(2, nav.Depth);
        nav.PopTo(99); // out of range
        Assert.Equal(2, nav.Depth);
    }

    [Fact]
    public void Breadcrumb_Click_JumpsToSegment()
    {
        var nav = new Navigator(new Page("Snapshots", new Label("x")));
        nav.Push("heap.snap", new Label("x"));
        nav.Push("System.String", new Label("x"));

        var s = new Surface(60, 4);
        nav.Render(s, s.Bounds); // breadcrumb on row 0, records segment ranges

        // "Snapshots" starts at x=1; click within it to jump back to the root.
        nav.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 3, 0, KeyModifiers.None));
        Assert.Equal(1, nav.Depth);
        Assert.Equal("Snapshots", nav.Current!.Title);
    }

    [Fact]
    public void Breadcrumb_ClickCurrentSegment_NoOp()
    {
        var nav = new Navigator(new Page("Home", new Label("x")));
        nav.Push("Detail", new Label("x"));

        var s = new Surface(60, 4);
        nav.Render(s, s.Bounds);

        // Click the last (current) segment "Detail" — should not change the stack.
        // "Home › Detail": Home is 1..5, " › " 5..8, Detail 8..14.
        nav.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 10, 0, KeyModifiers.None));
        Assert.Equal(2, nav.Depth);
    }

    [Fact]
    public void MountedNavigator_MountsPushedPage_AndUnmountsOnPop()
    {
        using var app = new App(new FakeTerminal());
        var root = new MountProbe();
        var nav = new Navigator(new Page("Home", root));
        nav.Mount(app);

        var s = new Surface(40, 10);
        nav.Render(s, s.Bounds);
        Assert.True(root.IsMounted); // the visible root page becomes live on first render

        var detail = new MountProbe();
        nav.Push("Detail", detail);
        Assert.True(detail.IsMounted); // a pushed page starts its lifecycle immediately

        nav.Pop();
        Assert.False(detail.IsMounted); // popping cancels the page's work
        Assert.True(root.IsMounted);    // the page beneath stays live
    }

    [Fact]
    public void UnmountingNavigator_UnmountsAllPages()
    {
        using var app = new App(new FakeTerminal());
        var root = new MountProbe();
        var detail = new MountProbe();
        var nav = new Navigator(new Page("Home", root));
        nav.Mount(app);
        var s = new Surface(40, 10);
        nav.Render(s, s.Bounds);
        nav.Push("Detail", detail);

        nav.Unmount();
        Assert.False(root.IsMounted);
        Assert.False(detail.IsMounted);
    }
}
