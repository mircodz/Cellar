using System;
using System.Linq;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using System.Text;
using Cellar.Widgets.Charts.Trees;

namespace Cellar.Tests;

public class TreeViewTests
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

    private static TreeView<string> SimpleTree(out TreeNode<string> root)
    {
        var tree = new TreeView<string> { RenderLabel = n => StyledText.Of(n.Value) };
        root = tree.AddRoot("root");
        var a = root.AddChild("a");
        a.AddChild("a1");
        a.AddChild("a2");
        root.AddChild("b");
        return tree;
    }

    private static KeyEvent K(Key key) => new(key, default, KeyModifiers.None);

    // ---- Lazy evaluation (the headline guarantee) ----

    [Fact]
    public void LazyChildren_NotMaterializedUntilExpanded()
    {
        int factoryCalls = 0;
        var tree = new TreeView<int> { RenderLabel = n => StyledText.Of(n.Value.ToString()) };
        tree.AddRoot(0, parent =>
        {
            factoryCalls++;
            return [parent * 10 + 1, parent * 10 + 2];
        });

        // Rendering a collapsed root must NOT invoke the children factory.
        var s = new Surface(20, 5);
        tree.Render(s, s.Bounds);
        Assert.Equal(0, factoryCalls);

        // Expanding the root materializes exactly one level.
        tree.Roots[0].Expand();
        tree.MarkDirty();
        tree.Render(s, s.Bounds);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void LazyNode_ShowsCaretViaHint_WithoutMaterializing()
    {
        int calls = 0;
        var tree = new TreeView<int> { RenderLabel = n => StyledText.Of("n") };
        var root = tree.AddRoot(0, _ => { calls++; return [1]; });
        root.HasChildrenHint = true;

        Assert.True(root.HasChildren);   // caret shows
        Assert.Equal(0, calls);          // but nothing computed
    }

    [Fact]
    public void DeepLazyTree_OnlyMaterializesOpenedPath()
    {
        int calls = 0;
        var tree = new TreeView<int> { RenderLabel = n => StyledText.Of("n") };
        // Infinite conceptual tree: each node has two children forever.
        tree.AddRoot(1, v => { calls++; return [v * 2, v * 2 + 1]; });

        var s = new Surface(30, 10);
        // Expand a single path 3 levels deep.
        var node = tree.Roots[0];
        for (int i = 0; i < 3; i++)
        {
            node.Expand();
            node = node.Children[0];
        }
        tree.MarkDirty();
        tree.Render(s, s.Bounds);

        // Exactly 3 factory invocations (one per expanded level), not the whole tree.
        Assert.Equal(3, calls);
    }

    // ---- Flattening & visibility ----

    [Fact]
    public void Flattening_RespectsExpansionState()
    {
        // Collapsed: only the root is visible.
        var tree = SimpleTree(out var root);
        var s = new Surface(20, 8);
        tree.Render(s, s.Bounds);
        Assert.Contains("root", RowText(s, 0));
        Assert.DoesNotContain("a", RowText(s, 1).Trim());

        // One level expanded: direct children show, grandchildren stay hidden.
        root.Expand();
        tree.MarkDirty();
        tree.Render(s, s.Bounds);
        var oneLevel = string.Join("\n", Enumerable.Range(0, 8).Select(y => RowText(s, y)));
        Assert.Contains("a", oneLevel);
        Assert.Contains("b", oneLevel);
        Assert.DoesNotContain("a1", oneLevel);

        // Fully expanded: every descendant is visible.
        root.ExpandAll();
        tree.MarkDirty();
        tree.Render(s, s.Bounds);
        var all = string.Join("\n", Enumerable.Range(0, 8).Select(y => RowText(s, y)));
        foreach (var label in new[] { "root", "a", "a1", "a2", "b" })
        {
            Assert.Contains(label, all);
        }
    }

    // ---- Keyboard navigation ----

    [Fact]
    public void ArrowDown_MovesSelection()
    {
        var tree = SimpleTree(out var root);
        root.Expand();
        tree.MarkDirty();
        var s = new Surface(20, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        Assert.Equal("root", tree.SelectedNode!.Value);
        tree.OnEvent(K(Key.Down));
        Assert.Equal("a", tree.SelectedNode!.Value);
    }

    [Fact]
    public void RightArrow_ExpandsSelectedNode()
    {
        var tree = SimpleTree(out var root);
        var s = new Surface(20, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        Assert.False(root.IsExpanded);
        tree.OnEvent(K(Key.Right));
        Assert.True(root.IsExpanded);
    }

    [Fact]
    public void LeftArrow_CollapsesThenAscends()
    {
        var tree = SimpleTree(out var root);
        root.Expand();
        tree.MarkDirty();
        var s = new Surface(20, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        // Move to child 'a', collapse-left should jump back to parent 'root'.
        tree.OnEvent(K(Key.Down)); // select 'a'
        Assert.Equal("a", tree.SelectedNode!.Value);
        tree.OnEvent(K(Key.Left)); // 'a' is collapsed leaf-ish; ascends to root
        Assert.Equal("root", tree.SelectedNode!.Value);
    }

    [Fact]
    public void Enter_TogglesExpansion()
    {
        var tree = SimpleTree(out var root);
        var s = new Surface(20, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        tree.OnEvent(K(Key.Enter));
        Assert.True(root.IsExpanded);
        tree.OnEvent(K(Key.Enter));
        Assert.False(root.IsExpanded);
    }

    [Fact]
    public void EndAndHome_JumpSelection()
    {
        var tree = SimpleTree(out var root);
        root.ExpandAll();
        tree.MarkDirty();
        var s = new Surface(20, 8);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        tree.OnEvent(K(Key.End));
        Assert.Equal("b", tree.SelectedNode!.Value); // last visible row
        tree.OnEvent(K(Key.Home));
        Assert.Equal("root", tree.SelectedNode!.Value);
    }

    // ---- Rendering details ----

    [Fact]
    public void Caret_ReflectsExpansionState()
    {
        var tree = SimpleTree(out var root);
        var s = new Surface(20, 6);

        tree.Render(s, s.Bounds);
        Assert.Contains("▶", RowText(s, 0)); // collapsed caret on root

        root.Expand();
        tree.MarkDirty();
        tree.Render(s, s.Bounds);
        Assert.Contains("▼", RowText(s, 0)); // expanded caret
    }

    [Fact]
    public void Guides_DrawBranchGlyphs()
    {
        var tree = SimpleTree(out var root);
        root.Expand();
        tree.MarkDirty();
        var s = new Surface(20, 6);
        tree.Render(s, s.Bounds);
        var all = string.Join("\n", Enumerable.Range(0, 6).Select(y => RowText(s, y)));
        // Child rows should show branch or last-branch connectors.
        Assert.True(all.Contains("├") || all.Contains("└"));
    }

    [Fact]
    public void AsciiGuides_UseAsciiGlyphs()
    {
        var tree = SimpleTree(out var root);
        tree.Guides = TreeGuides.Ascii;
        root.Expand();
        tree.MarkDirty();
        var s = new Surface(20, 6);
        tree.Render(s, s.Bounds);
        var all = string.Join("\n", Enumerable.Range(0, 6).Select(y => RowText(s, y)));
        Assert.Contains("+", all);
    }

    [Fact]
    public void Render_OnlyDrawsViewportRows()
    {
        // 1000 visible rows but a 5-row viewport: rendering must not touch beyond the surface.
        var tree = new TreeView<int> { RenderLabel = n => StyledText.Of(n.Value.ToString()) };
        var root = tree.AddRoot(0);
        for (int i = 1; i <= 1000; i++)
        {
            root.AddChild(i);
        }

        root.Expand();
        tree.MarkDirty();

        var s = new Surface(20, 5);
        tree.Render(s, s.Bounds); // must not throw and only fills 5 rows
        Assert.Contains("0", RowText(s, 0)); // root value is 0
    }

    [Fact]
    public void OnSelect_FiresOnChange()
    {
        var tree = SimpleTree(out var root);
        root.Expand();
        tree.MarkDirty();
        string? selected = null;
        tree.OnSelect = n => selected = n.Value;
        var s = new Surface(20, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        tree.OnEvent(K(Key.Down));
        Assert.Equal("a", selected);
    }

    // ---- Tree-table columns ----

    [Fact]
    public void Columns_AlignRegardlessOfDepth()
    {
        // Two rows at different depths; a right-aligned metric column must land in the same
        // screen columns for both, proving depth-independent alignment.
        var tree = new TreeView<string> { RenderLabel = n => StyledText.Of(n.Value) };
        tree.Columns.Add(new TreeColumn<string>("val", 6, n => StyledText.Of("XX"),
            TreeColumnAlign.Right));
        var root = tree.AddRoot("root");
        var child = root.AddChild("child");
        child.AddChild("grandchild");
        root.ExpandAll();
        tree.MarkDirty();

        var s = new Surface(40, 6);
        tree.Render(s, s.Bounds);

        int Col(int y)
        {
            var line = RowText(s, y);
            return line.IndexOf("XX", StringComparison.Ordinal);
        }
        // root (row 0), child (row 1), grandchild (row 2) — all "XX" at the same x.
        int c0 = Col(0), c1 = Col(1), c2 = Col(2);
        Assert.True(c0 > 0);
        Assert.Equal(c0, c1);
        Assert.Equal(c0, c2);
    }

    [Fact]
    public void Header_DrawnWhenEnabled()
    {
        var tree = new TreeView<string> { RenderLabel = n => StyledText.Of(n.Value), ShowHeader = true };
        tree.Columns.Add(new TreeColumn<string>("time", 8, n => StyledText.Of("1ms")));
        tree.AddRoot("root");
        tree.MarkDirty();

        var s = new Surface(30, 4);
        tree.Render(s, s.Bounds);
        Assert.Contains("time", RowText(s, 0)); // header row
        Assert.Contains("root", RowText(s, 1)); // data starts below header
    }

    [Fact]
    public void Header_ShiftsRowsDown_ClickStillHits()
    {
        var tree = new TreeView<string> { RenderLabel = n => StyledText.Of(n.Value), ShowHeader = true };
        tree.Columns.Add(new TreeColumn<string>("c", 4, n => StyledText.Of("x")));
        var root = tree.AddRoot("root");
        root.AddChild("a");
        tree.MarkDirty();

        var s = new Surface(20, 5);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        // Row 0 is the header; the root node is on row 1. A click there should hit the root.
        tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 2, 1, KeyModifiers.None));
        Assert.Equal("root", tree.SelectedNode!.Value);
    }

    [Fact]
    public void RowClick_SelectsButDoesNotToggle_ByDefault()
    {
        var tree = SimpleTree(out var root); // root collapsed, has children
        var s = new Surface(30, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        // Click on the label area (well past the caret) — should select but NOT expand.
        tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 10, 0, KeyModifiers.None));
        Assert.Equal("root", tree.SelectedNode!.Value);
        Assert.False(root.IsExpanded);
    }

    [Fact]
    public void CaretClick_TogglesExpansion()
    {
        var tree = SimpleTree(out var root);
        var s = new Surface(30, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        // The root's caret is at the very start of its row (depth 0, x=0..1).
        tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 0, 0, KeyModifiers.None));
        Assert.True(root.IsExpanded);
    }

    [Fact]
    public void ToggleOnCaretOnlyFalse_RowClickToggles()
    {
        var tree = SimpleTree(out var root);
        tree.ToggleOnCaretOnly = false;
        var s = new Surface(30, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 10, 0, KeyModifiers.None));
        Assert.True(root.IsExpanded);
    }

    // ---- Guide connectivity ----

    [Fact]
    public void Guides_VerticalContinuesPastNonLastAncestor()
    {
        // root has children A (non-last) and B (last). A has a child A1. On A1's row, the
        // first guide column must be a vertical bar (│) because A's parent-level sibling B
        // still follows — i.e. the line connects down past A's subtree.
        var tree = new TreeView<string> { RenderLabel = n => StyledText.Of(n.Value) };
        var root = tree.AddRoot("root");
        var a = root.AddChild("A");
        a.AddChild("A1");
        root.AddChild("B");
        root.ExpandAll();
        tree.MarkDirty();

        var s = new Surface(20, 5);
        tree.Render(s, s.Bounds);
        // Rows: 0=root, 1=A, 2=A1, 3=B. On A1's row, the first indentation column is a │.
        Assert.Contains("│", RowText(s, 2));
    }

    // ---- Clickable links ----

    // A root label "caret + 'go:' + link 'X'". The depth-0 leaf marker occupies x=0..1, so the
    // label starts at x=2: 'g'(2) 'o'(3) ':'(4) then the link glyph 'X' at x=5.
    private static TreeView<string> LinkTree(string payload)
    {
        return new TreeView<string>
        {
            RenderLabel = _ => StyledText.Of("go:").Append("X").Link(payload),
        };
    }

    [Fact]
    public void LinkClick_FiresPayload_AndSkipsSelectionToggle()
    {
        var tree = LinkTree("gcroot-42");
        tree.AddRoot("root");
        object? clicked = null;
        tree.OnLinkClick = p => clicked = p;

        var s = new Surface(20, 3);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        // Click the link glyph 'X' at x=5, row 0.
        bool handled = tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 5, 0, KeyModifiers.None));
        Assert.True(handled);
        Assert.Equal("gcroot-42", clicked);
    }

    [Fact]
    public void ClickOnNonLinkText_DoesNotFireLink()
    {
        var tree = LinkTree("gcroot-42");
        tree.AddRoot("root");
        object? clicked = null;
        tree.OnLinkClick = p => clicked = p;

        var s = new Surface(20, 3);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        // Click the 'o' of "go:" at x=3 — not the link.
        tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 3, 0, KeyModifiers.None));
        Assert.Null(clicked);
    }

    [Fact]
    public void LinkSurvivesSelectionRecolor()
    {
        // Select the node first (so the label is drawn via the selection Recolor path), then
        // the link payload must still be clickable.
        var tree = LinkTree("addr");
        tree.AddRoot("root");
        object? clicked = null;
        tree.OnLinkClick = p => clicked = p;

        var s = new Surface(20, 3);
        tree.HasFocus = true;
        tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 3, 0, KeyModifiers.None)); // select row
        tree.Render(s, s.Bounds);
        tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 5, 0, KeyModifiers.None)); // click link
        Assert.Equal("addr", clicked);
    }

    [Fact]
    public void NoLinkHandler_FallsThroughToSelection()
    {
        var tree = LinkTree("addr"); // OnLinkClick left null
        var root = tree.AddRoot("root");
        var s = new Surface(20, 3);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        // Clicking the link with no handler should behave like a normal row click (select).
        tree.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 5, 0, KeyModifiers.None));
        Assert.Equal("root", tree.SelectedNode!.Value);
    }

    [Fact]
    public void Enter_WithOnActivate_ActivatesWithoutToggling()
    {
        var tree = SimpleTree(out var root);
        TreeNode<string>? activated = null;
        tree.OnActivate = node => activated = node;
        var s = new Surface(20, 6);
        tree.HasFocus = true;
        tree.Render(s, s.Bounds);

        Assert.True(tree.OnEvent(K(Key.Enter)));
        Assert.Same(root, activated);
        Assert.False(root.IsExpanded);
    }
}
