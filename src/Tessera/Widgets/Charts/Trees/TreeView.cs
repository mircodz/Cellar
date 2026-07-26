using System;
using System.Collections.Generic;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Text;
using Tessera.Theming;

namespace Tessera.Widgets.Charts.Trees;

/// <summary>
/// A collapsible tree over an arbitrary value type <typeparamref name="T"/>. The caller
/// supplies <see cref="RenderLabel"/>, so the widget is use-case-agnostic. Cost scales with
/// visible rows, not tree size: children are lazily materialized, the visible-row list is
/// cached and rebuilt only on expansion/structure change, and only viewport rows are drawn.
/// </summary>
public sealed class TreeView<T> : Widget
{
    private readonly List<TreeNode<T>> _roots = new();

    // Cached flattened view of currently-visible nodes (root + descendants of expanded
    // nodes). Rebuilt lazily when _dirty is set by an expand/collapse/structure change.
    private readonly List<Row> _flat = new();
    private bool _dirty = true;

    private int _selected;
    private int _scroll;
    private int _viewportRows;
    private Rect _lastArea;

    // Per visible row, the caret's screen x-range [start, end), for precise click hit-testing.
    private readonly List<(int y, int caretStart, int caretEnd, int flatIndex)> _rowHits = new();

    /// <summary>When true (default), only the caret toggles a node; elsewhere just selects.
    /// False toggles on any row click.</summary>
    public bool ToggleOnCaretOnly { get; set; } = true;

    private readonly record struct Row(TreeNode<T> Node, int Depth, bool IsLastChild);

    /// <summary>Turns a node into the styled text shown on its row. Required.</summary>
    public required Func<TreeNode<T>, StyledText> RenderLabel { get; set; }

    /// <summary>Optional fixed-width metric columns on the right (turns the tree into a
    /// tree-table): the label column flexes on the left, these stay aligned on the right.</summary>
    public List<TreeColumn<T>> Columns { get; } = new();

    /// <summary>When true (and columns exist), a header row is drawn at the top.</summary>
    public bool ShowHeader { get; set; }

    /// <summary>Gap in cells between adjacent right-hand columns.</summary>
    public int ColumnSpacing { get; set; } = 1;

    /// <summary>Glyph set for guides and carets. Defaults to Unicode box-drawing.</summary>
    public TreeGuides Guides { get; set; } = TreeGuides.Unicode;

    /// <summary>Whether to draw the indentation guide lines (│ ├─ └─).</summary>
    public bool ShowGuides { get; set; } = true;

    /// <summary>Style for guide lines and carets. Null uses the theme's muted tone.</summary>
    public Style? GuideStyle { get; set; }

    /// <summary>Style for the selected row. Null uses the theme selection style.</summary>
    public Style? SelectionStyle { get; set; }

    /// <summary>When true, alternating rows get a subtly different background (zebra striping).</summary>
    public bool Striped { get; set; }

    /// <summary>Style for striped (odd) rows. Null derives a subtle stripe from the theme.</summary>
    public Style? StripeStyle { get; set; }

    /// <summary>Raised when the selected node changes.</summary>
    public Action<TreeNode<T>>? OnSelect { get; set; }

    /// <summary>Raised when a node is activated (Enter or double-context use).</summary>
    public Action<TreeNode<T>>? OnActivate { get; set; }

    public override bool IsFocusable => true;

    public IReadOnlyList<TreeNode<T>> Roots => _roots;

    /// <summary>The currently selected node, or null if the tree is empty.</summary>
    public TreeNode<T>? SelectedNode
    {
        get
        {
            EnsureFlattened();
            return _selected >= 0 && _selected < _flat.Count ? _flat[_selected].Node : null;
        }
    }

    public TreeNode<T> AddRoot(T value, Func<T, IEnumerable<T>>? childrenFactory = null)
    {
        var node = new TreeNode<T>(value, childrenFactory);
        _roots.Add(node);
        _dirty = true;
        return node;
    }

    public TreeView<T> AddRoot(TreeNode<T> node)
    {
        _roots.Add(node);
        _dirty = true;
        return this;
    }

    /// <summary>Removes all roots (and resets selection), so the tree can be rebuilt from fresh data.</summary>
    public void Clear()
    {
        _roots.Clear();
        _selected = 0;
        _scroll = 0;
        _dirty = true;
    }

    /// <summary>Marks the flattened view stale (call after mutating expansion externally).</summary>
    public void MarkDirty() => _dirty = true;

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        EnsureFlattened();

        var theme = Theme.Current;
        var guideStyle = GuideStyle ?? theme.MutedStyle;
        var selStyle = SelectionStyle ?? theme.SelectionStyle;

        // Reserve a fixed band on the right for the metric columns; the label column gets
        // whatever remains on the left. Total column band = sum of widths + inter-column gaps.
        int columnsWidth = ColumnsBandWidth();
        int labelRight = area.Right - columnsWidth;

        int rowsTop = area.Y;
        int rowsHeight = area.Height;
        if (ShowHeader && Columns.Count > 0 && area.Height > 0)
        {
            DrawHeader(surface, area, labelRight, theme);
            rowsTop += 1;
            rowsHeight -= 1;
        }

        _viewportRows = rowsHeight;
        _lastArea = new Rect(area.X, rowsTop, area.Width, rowsHeight);
        ClampSelectionAndScroll();
        _rowHits.Clear();
        ClearLinks();

        int rows = Math.Min(rowsHeight, _flat.Count - _scroll);
        for (int i = 0; i < rows; i++)
        {
            int flatIndex = _scroll + i;
            var row = _flat[flatIndex];
            int y = rowsTop + i;
            bool selected = flatIndex == _selected;
            bool striped = !selected && Striped && (flatIndex & 1) == 1;

            Style? rowBg = null;
            if (selected)
            {
                rowBg = selStyle;
            }
            else if (striped)
            {
                rowBg = StripeStyle ?? new Style(theme.Foreground, theme.StripeBackground);
            }

            if (rowBg is { } bg)
            {
                surface.FillRect(new Rect(area.X, y, area.Width, 1), bg);
            }

            var effGuideStyle = rowBg is { } g1 ? guideStyle.WithBackground(g1.Background) : guideStyle;

            int x = area.X;

            if (ShowGuides && row.Depth > 0)
            {
                x = DrawGuides(surface, x, y, row, effGuideStyle, rowBg);
            }

            var node = row.Node;
            string caret = node.HasChildren
                ? (node.IsExpanded ? Guides.ExpandedCaret : Guides.CollapsedCaret)
                : Guides.LeafMarker;
            var caretStyle = selected ? selStyle : effGuideStyle;
            int caretStart = x;
            x = surface.DrawText(x, y, caret, caretStyle);
            _rowHits.Add((y, caretStart, node.HasChildren ? x : caretStart, flatIndex));

            var label = RenderLabel(node);
            int labelWidth = labelRight - x;
            if (labelWidth > 0)
            {
                surface.SetClip(new Rect(x, y, labelWidth, 1));
                // When a row background is active, tint the label's spans onto it.
                var drawn = selected ? Recolor(label, selStyle)
                    : rowBg is { } lbg ? RecolorBackground(label, lbg.Background)
                    : label;
                // Collect link hit-rects only when the label actually has links.
                TextRenderer.DrawLine(surface, x, y, labelWidth, drawn, Justify.Left,
                    label.HasLinks ? Links : null);
                surface.ResetClip();
            }

            if (Columns.Count > 0)
            {
                DrawColumns(surface, labelRight, y, node, selected ? selStyle : null,
                    stripeBg: !selected && rowBg is { } rb ? rb.Background : (Color?)null);
            }
        }

        DrawHoverEmphasis(surface);
    }

    // Total width reserved by the metric column band (widths + inter-column spacing).
    private int ColumnsBandWidth()
    {
        if (Columns.Count == 0)
        {
            return 0;
        }

        int w = 0;
        foreach (var c in Columns)
        {
            w += c.Width;
        }

        w += ColumnSpacing * (Columns.Count + 1); // spacing between and a lead gap before the band
        return w;
    }

    private void DrawColumns(Surface surface, int bandStart, int y, TreeNode<T> node, Style? forceStyle, Color? stripeBg = null)
    {
        int x = bandStart + ColumnSpacing; // lead gap separating label from metrics
        foreach (var col in Columns)
        {
            var content = col.Render(node);
            if (forceStyle is { } fs)
            {
                content = Recolor(content, fs);
            }
            else if (stripeBg is { } sb)
            {
                content = RecolorBackground(content, sb);
            }

            var justify = col.Align == TreeColumnAlign.Right ? Justify.Right : Justify.Left;
            surface.SetClip(new Rect(x, y, col.Width, 1));
            TextRenderer.DrawLine(surface, x, y, col.Width, content, justify);
            surface.ResetClip();
            x += col.Width + ColumnSpacing;
        }
    }

    private void DrawHeader(Surface surface, Rect area, int labelRight, Theme theme)
    {
        var headerStyle = theme.HeaderStyle;
        surface.FillRect(new Rect(area.X, area.Y, area.Width, 1), new Style(theme.Foreground, Color.Default));

        int x = labelRight + ColumnSpacing;
        foreach (var col in Columns)
        {
            var head = new StyledText(col.Header, headerStyle);
            var justify = col.Align == TreeColumnAlign.Right ? Justify.Right : Justify.Left;
            surface.SetClip(new Rect(x, area.Y, col.Width, 1));
            TextRenderer.DrawLine(surface, x, area.Y, col.Width, head, justify);
            surface.ResetClip();
            x += col.Width + ColumnSpacing;
        }
    }

    // Draws the vertical guide lines for a row's ancestor chain, returning the new x.
    private int DrawGuides(Surface surface, int x, int y, Row row, Style guideStyle, Style? selBg)
    {
        // For each indentation column d (0-based) we are crossing from depth d to d+1, so the
        // vertical bar continues iff the ancestor at depth d+1 has a following sibling. The
        // node's own connector (├─ or └─) is drawn for the last level.
        var ancestors = AncestorHasNextSibling(row.Node);
        for (int d = 0; d < row.Depth - 1; d++)
        {
            string g = ancestors[d + 1] ? Guides.Vertical : Guides.Indent;
            x = surface.DrawText(x, y, g, guideStyle);
        }
        string connector = row.IsLastChild ? Guides.LastBranch : Guides.Branch;
        x = surface.DrawText(x, y, connector, guideStyle);
        return x;
    }

    // For each ancestor depth (0..depth-2), whether that ancestor has a following sibling
    // (so a vertical guide should continue through this row's indentation there).
    private bool[] AncestorHasNextSibling(TreeNode<T> node)
    {
        int depth = node.Depth;
        var result = new bool[Math.Max(0, depth)];
        var cur = node.Parent;
        int level = depth - 1;
        while (cur is not null && level >= 0)
        {
            result[level] = HasNextSibling(cur);
            cur = cur.Parent;
            level--;
        }
        return result;
    }

    private bool HasNextSibling(TreeNode<T> node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            int idx = _roots.IndexOf(node);
            return idx >= 0 && idx < _roots.Count - 1;
        }
        var siblings = parent.Children;
        int i = IndexOfChild(siblings, node);
        return i >= 0 && i < siblings.Count - 1;
    }

    private static int IndexOfChild(IReadOnlyList<TreeNode<T>> list, TreeNode<T> node)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    private static StyledText Recolor(StyledText label, Style style)
    {
        var result = StyledText.Empty();
        foreach (var span in label.Spans)
        {
            result.Append(span.WithStyle(style)); // keeps Link + LinkHoverStyle
        }

        return result;
    }

    // Keeps each span's own foreground/attributes but sets a shared background (for stripes).
    private static StyledText RecolorBackground(StyledText label, Color background)
    {
        var result = StyledText.Empty();
        foreach (var span in label.Spans)
        {
            result.Append(span.WithStyle(span.Style.WithBackground(background)));
        }

        return result;
    }

    private void EnsureFlattened()
    {
        if (!_dirty)
        {
            return;
        }

        _flat.Clear();
        for (int i = 0; i < _roots.Count; i++)
        {
            Flatten(_roots[i], 0, isLast: i == _roots.Count - 1);
        }

        _dirty = false;
    }

    private void Flatten(TreeNode<T> node, int depth, bool isLast)
    {
        _flat.Add(new Row(node, depth, isLast));
        if (!node.IsExpanded)
        {
            return;
        }

        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
        {
            Flatten(children[i], depth + 1, i == children.Count - 1);
        }
    }

    public override bool OnEvent(InputEvent e)
    {
        switch (e)
        {
            case KeyEvent key when HasFocus:
                return HandleKey(key);
            case MouseEvent { Kind: MouseEventKind.Wheel } wheel when _lastArea.Contains(wheel.X, wheel.Y):
                MoveSelection(wheel.Button == MouseButton.WheelUp ? -1 : 1);
                return true;
            case MouseEvent { Kind: MouseEventKind.Move } move:
                return UpdateHover(move.X, move.Y);
            case MouseEvent { Kind: MouseEventKind.Down } click:
                return HandleClick(click);
        }
        return false;
    }

    private bool HandleKey(KeyEvent key)
    {
        EnsureFlattened();
        switch (key.Key)
        {
            case Key.Up: MoveSelection(-1); return true;
            case Key.Down: MoveSelection(1); return true;
            case Key.PageUp: MoveSelection(-Math.Max(1, _viewportRows - 1)); return true;
            case Key.PageDown: MoveSelection(Math.Max(1, _viewportRows - 1)); return true;
            case Key.Home: SetSelection(0); return true;
            case Key.End: SetSelection(_flat.Count - 1); return true;

            case Key.Right: ExpandOrDescend(); return true;
            case Key.Left: CollapseOrAscend(); return true;

            case Key.Enter:
                var node = SelectedNode;
                if (node is not null)
                {
                    if (node.HasChildren) { node.Toggle(); _dirty = true; }
                    OnActivate?.Invoke(node);
                }
                return true;
        }
        if (key.IsChar && key.Rune.Value == ' ')
        {
            var n = SelectedNode;
            if (n is { HasChildren: true }) { n.Toggle(); _dirty = true; }
            return true;
        }
        return false;
    }

    private void ExpandOrDescend()
    {
        var node = SelectedNode;
        if (node is null)
        {
            return;
        }

        if (node.HasChildren && !node.IsExpanded)
        {
            node.Expand();
            _dirty = true;
        }
        else if (node.IsExpanded)
        {
            MoveSelection(1);
        }
    }

    private void CollapseOrAscend()
    {
        var node = SelectedNode;
        if (node is null)
        {
            return;
        }

        if (node.IsExpanded)
        {
            node.Collapse();
            _dirty = true;
        }
        else if (node.Parent is not null)
        {
            EnsureFlattened();
            for (int i = 0; i < _flat.Count; i++)
            {
                if (ReferenceEquals(_flat[i].Node, node.Parent)) { SetSelection(i); break; }
            }
        }
    }

    private bool HandleClick(MouseEvent m)
    {
        EnsureFlattened();
        if (!_lastArea.Contains(m.X, m.Y))
        {
            return false;
        }

        // A click on a rendered link jumps via OnLinkClick, without also selecting/toggling.
        if (DispatchLinkClick(m.X, m.Y))
        {
            return true;
        }

        int rowIndex = _scroll + (m.Y - _lastArea.Y);
        if (rowIndex < 0 || rowIndex >= _flat.Count)
        {
            return false;
        }

        SetSelection(rowIndex);
        var node = _flat[rowIndex].Node;

        // Toggle expansion only when the click landed on the caret (or when the caller opted
        // into whole-row toggling via ToggleOnCaretOnly = false).
        bool toggle = !ToggleOnCaretOnly || ClickedCaret(m.X, m.Y, rowIndex);
        if (toggle && node.HasChildren) { node.Toggle(); _dirty = true; }
        return true;
    }

    // Whether an absolute click fell within the caret's recorded x-range for a given row.
    private bool ClickedCaret(int x, int y, int flatIndex)
    {
        foreach (var (hy, start, end, idx) in _rowHits)
        {
            if (idx == flatIndex && hy == y)
            {
                return end > start && x >= start && x < end;
            }
        }

        return false;
    }

    private void MoveSelection(int delta)
    {
        EnsureFlattened();
        SetSelection(_selected + delta);
    }

    private void SetSelection(int index)
    {
        EnsureFlattened();
        if (_flat.Count == 0) { _selected = 0; return; }
        int clamped = Math.Clamp(index, 0, _flat.Count - 1);
        if (clamped != _selected)
        {
            _selected = clamped;
            OnSelect?.Invoke(_flat[_selected].Node);
        }
        EnsureVisible();
    }

    private void ClampSelectionAndScroll()
    {
        if (_flat.Count == 0) { _selected = 0; _scroll = 0; return; }
        _selected = Math.Clamp(_selected, 0, _flat.Count - 1);
        EnsureVisible();
    }

    private void EnsureVisible()
    {
        if (_viewportRows <= 0)
        {
            return;
        }

        if (_selected < _scroll)
        {
            _scroll = _selected;
        }
        else if (_selected >= _scroll + _viewportRows)
        {
            _scroll = _selected - _viewportRows + 1;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _flat.Count - 1));
    }
}
