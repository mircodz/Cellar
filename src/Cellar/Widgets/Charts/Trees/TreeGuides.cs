namespace Cellar.Widgets.Charts.Trees;

/// <summary>
/// The glyphs used to draw a tree's indentation guides and expand affordances. Swappable so
/// callers can pick a look (or fall back to pure ASCII on limited terminals) without the
/// widget hardcoding a style.
/// </summary>
public sealed class TreeGuides
{
    /// <summary>Vertical guide connecting a parent to siblings below (usually "│ ").</summary>
    public required string Vertical { get; init; }

    /// <summary>Branch to a non-last child (usually "├─").</summary>
    public required string Branch { get; init; }

    /// <summary>Branch to the last child (usually "└─").</summary>
    public required string LastBranch { get; init; }

    /// <summary>Blank indentation where an ancestor has no more siblings (usually "  ").</summary>
    public required string Indent { get; init; }

    /// <summary>Caret shown on an expanded node.</summary>
    public required string ExpandedCaret { get; init; }

    /// <summary>Caret shown on a collapsed node with children.</summary>
    public required string CollapsedCaret { get; init; }

    /// <summary>Marker for a leaf (no children).</summary>
    public required string LeafMarker { get; init; }

    /// <summary>Rounded Unicode box-drawing guides with filled-arrow carets. The default.</summary>
    public static TreeGuides Unicode { get; } = new()
    {
        Vertical = "│ ",
        Branch = "├─",
        LastBranch = "└─",
        Indent = "  ",
        ExpandedCaret = "▼ ",
        CollapsedCaret = "▶ ",
        LeafMarker = "  ",
    };

    /// <summary>Pure ASCII guides for terminals without box-drawing support.</summary>
    public static TreeGuides Ascii { get; } = new()
    {
        Vertical = "| ",
        Branch = "|-",
        LastBranch = "`-",
        Indent = "  ",
        ExpandedCaret = "- ",
        CollapsedCaret = "+ ",
        LeafMarker = "  ",
    };
}
