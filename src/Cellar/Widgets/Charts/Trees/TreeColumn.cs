using System;
using Cellar.Text;

namespace Cellar.Widgets.Charts.Trees;

/// <summary>Horizontal alignment of a tree-table column's cell content.</summary>
public enum TreeColumnAlign { Left, Right }

/// <summary>
/// A fixed-width metric column rendered on the right side of a <see cref="TreeView{T}"/>,
/// aligned across all rows regardless of tree depth. The name column on the left carries the
/// guides and flexes to fill; these columns stay in rigid vertical alignment (e.g. a time and
/// a percentage), giving the tree a table on the right.
/// </summary>
public sealed class TreeColumn<T>
{
    /// <summary>Optional header label shown above the column.</summary>
    public string Header { get; set; }

    /// <summary>Fixed width in cells.</summary>
    public int Width { get; set; }

    /// <summary>Cell alignment within the column (metrics usually right-align).</summary>
    public TreeColumnAlign Align { get; set; }

    /// <summary>Produces the styled cell content for a node.</summary>
    public Func<TreeNode<T>, StyledText> Render { get; set; }

    public TreeColumn(string header, int width, Func<TreeNode<T>, StyledText> render,
        TreeColumnAlign align = TreeColumnAlign.Right)
    {
        Header = header;
        Width = width;
        Render = render;
        Align = align;
    }
}
