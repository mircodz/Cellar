using System.Text;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Widgets.Charts;

namespace Tessera.Tests;

public class FlameGraphTests
{
    private static FlameGraph<string> BuildGraph(out FlameNode<string> root)
    {
        var fg = new FlameGraph<string> { LabelSelector = n => n.Label };
        root = new FlameNode<string>("root", "main", 100);
        var a = root.Add("a", "parse", 60);
        a.Add("a1", "lex", 30);
        a.Add("a2", "build", 30);
        root.Add("b", "emit", 40);
        fg.Root = root;
        return fg;
    }

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

    [Fact]
    public void Root_FillsFullWidth()
    {
        var fg = BuildGraph(out var root);
        var s = new Surface(100, 4);
        fg.Render(s, s.Bounds);
        // Root label on row 0, its two children proportional (60/40) on row 1.
        Assert.Contains("main", RowText(s, 0));
        var row1 = RowText(s, 1);
        Assert.Contains("parse", row1);
        Assert.Contains("emit", row1);
    }

    [Fact]
    public void ChildrenWidthsAreProportional()
    {
        var fg = BuildGraph(out var root);
        var s = new Surface(100, 4);
        fg.Render(s, s.Bounds);
        // "parse" (60) should start at x=0; "emit" (40) should start near x=60.
        var row1 = RowText(s, 1);
        int parseAt = row1.IndexOf("parse", System.StringComparison.Ordinal);
        int emitAt = row1.IndexOf("emit", System.StringComparison.Ordinal);
        Assert.Equal(1, parseAt); // 1-cell inset
        Assert.True(emitAt >= 58 && emitAt <= 62, $"emit at {emitAt}, expected ~60");
    }

    [Fact]
    public void ZoomTo_MakesFrameFullWidth()
    {
        var fg = BuildGraph(out var root);
        var parse = root.Children[0];
        fg.ZoomTo(parse);
        Assert.Same(parse, fg.ZoomedFrame);

        var s = new Surface(100, 4);
        fg.Render(s, s.Bounds);
        // Now "parse" is the full-width root; its children lex/build fill row 1.
        Assert.Contains("parse", RowText(s, 0));
        var row1 = RowText(s, 1);
        Assert.Contains("lex", row1);
        Assert.Contains("build", row1);
    }

    [Fact]
    public void Click_SelectsThenZooms()
    {
        var fg = BuildGraph(out var root);
        fg.HasFocus = true;
        var s = new Surface(100, 4);
        fg.Render(s, s.Bounds);

        // Click "emit" on row 1 (around x=60) — first click selects it.
        fg.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 65, 1, KeyModifiers.None));
        Assert.Equal("emit", fg.SelectedFrame!.Label);

        // Re-render (selection changed layout is same), click again — zooms.
        fg.Render(s, s.Bounds);
        fg.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 65, 1, KeyModifiers.None));
        Assert.Equal("emit", fg.ZoomedFrame!.Label);
    }

    [Fact]
    public void Keyboard_NavigatesSiblingsAndDepth()
    {
        var fg = BuildGraph(out var root);
        fg.HasFocus = true;
        var s = new Surface(100, 4);
        fg.Render(s, s.Bounds);

        // Down into first child (parse), right to sibling (emit), down into its (none), up to root.
        fg.OnEvent(new KeyEvent(Key.Down, default, KeyModifiers.None));
        Assert.Equal("parse", fg.SelectedFrame!.Label);
        fg.OnEvent(new KeyEvent(Key.Right, default, KeyModifiers.None));
        Assert.Equal("emit", fg.SelectedFrame!.Label);
        fg.OnEvent(new KeyEvent(Key.Left, default, KeyModifiers.None));
        Assert.Equal("parse", fg.SelectedFrame!.Label);
    }

    [Fact]
    public void EnterZooms_BackspaceZoomsOut()
    {
        var fg = BuildGraph(out var root);
        fg.HasFocus = true;
        var s = new Surface(100, 4);
        fg.Render(s, s.Bounds);

        fg.OnEvent(new KeyEvent(Key.Down, default, KeyModifiers.None)); // select parse
        fg.OnEvent(new KeyEvent(Key.Enter, default, KeyModifiers.None)); // zoom into parse
        Assert.Equal("parse", fg.ZoomedFrame!.Label);
        fg.OnEvent(new KeyEvent(Key.Backspace, default, KeyModifiers.None)); // zoom out
        Assert.Same(root, fg.ZoomedFrame);
    }

    [Fact]
    public void Empty_RendersNothing()
    {
        var fg = new FlameGraph<string>();
        var s = new Surface(20, 4);
        fg.Render(s, s.Bounds); // no root -> no crash, nothing drawn
    }
}
