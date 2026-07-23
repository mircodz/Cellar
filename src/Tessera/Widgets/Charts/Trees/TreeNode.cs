using System;
using System.Collections.Generic;

namespace Tessera.Widgets.Charts.Trees;

/// <summary>
/// A node in a <see cref="TreeView{T}"/> wrapping a value of type <typeparamref name="T"/>.
///
/// Children can be supplied two ways:
/// <list type="bullet">
/// <item><b>Eager</b> — add child nodes directly via <see cref="AddChild(T)"/> or
/// <see cref="Children"/>.</item>
/// <item><b>Lazy</b> — provide a <c>childrenFactory</c> that runs only the first time the node
/// is expanded. This is what lets huge trees (e.g. a full call-stack trace) stay cheap: an
/// unexpanded subtree is never materialized, so cost scales with what the user opens, not the
/// tree's total size.</item>
/// </list>
///
/// For lazy nodes that have not yet materialized, <see cref="HasChildren"/> lets a caller
/// declare up front whether the expand caret should be shown (so the UI can offer to expand
/// a node before paying to compute its children).
/// </summary>
public sealed class TreeNode<T>
{
    private readonly Func<T, IEnumerable<T>>? _childrenFactory;
    private List<TreeNode<T>>? _children;
    private bool _materialized;
    private bool? _hasChildrenHint;

    public T Value { get; set; }

    /// <summary>The parent node, or null for a root.</summary>
    public TreeNode<T>? Parent { get; private set; }

    /// <summary>Depth from the root (root = 0). Used for indentation.</summary>
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    /// <summary>Whether this node is currently expanded (its children shown).</summary>
    public bool IsExpanded { get; private set; }

    public TreeNode(T value, Func<T, IEnumerable<T>>? childrenFactory = null)
    {
        Value = value;
        _childrenFactory = childrenFactory;
    }

    /// <summary>
    /// Explicitly declares whether this node has children, without materializing them. Set
    /// this for lazy nodes so the caret renders correctly before expansion. When null and a
    /// factory exists, the node is assumed to potentially have children.
    /// </summary>
    public bool? HasChildrenHint
    {
        get => _hasChildrenHint;
        set => _hasChildrenHint = value;
    }

    /// <summary>
    /// Whether this node should show an expand affordance. True if it already has
    /// materialized children, or a hint says so, or it has an unmaterialized lazy factory.
    /// </summary>
    public bool HasChildren
    {
        get
        {
            if (_materialized)
            {
                return _children is { Count: > 0 };
            }

            if (_hasChildrenHint is { } hint)
            {
                return hint;
            }

            return _childrenFactory is not null;
        }
    }

    /// <summary>
    /// The child nodes. Accessing this materializes lazy children (invoking the factory
    /// once). For unexpanded lazy nodes, prefer <see cref="HasChildren"/> to avoid the cost.
    /// </summary>
    public IReadOnlyList<TreeNode<T>> Children
    {
        get
        {
            Materialize();
            return _children!;
        }
    }

    /// <summary>Adds an eager child node and returns it.</summary>
    public TreeNode<T> AddChild(T value)
    {
        var node = new TreeNode<T>(value) { Parent = this };
        (_children ??= new()).Add(node);
        _materialized = true;
        return node;
    }

    /// <summary>Adds an already-built child node (e.g. one with its own lazy factory).</summary>
    public TreeNode<T> AddChild(TreeNode<T> node)
    {
        node.Parent = this;
        (_children ??= new()).Add(node);
        _materialized = true;
        return node;
    }

    /// <summary>Expands the node, materializing lazy children on first expand.</summary>
    public void Expand()
    {
        if (!HasChildren)
        {
            return;
        }

        Materialize();
        IsExpanded = true;
    }

    /// <summary>Collapses the node. Materialized children are retained for a fast re-expand.</summary>
    public void Collapse() => IsExpanded = false;

    public void Toggle()
    {
        if (IsExpanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    /// <summary>Expands this node and, recursively, all descendants. Materializes the subtree.</summary>
    public void ExpandAll()
    {
        if (!HasChildren)
        {
            return;
        }

        Expand();
        foreach (var c in _children!)
        {
            c.ExpandAll();
        }
    }

    /// <summary>Collapses this node and all descendants without discarding materialized data.</summary>
    public void CollapseAll()
    {
        Collapse();
        if (_children is null)
        {
            return;
        }

        foreach (var c in _children)
        {
            c.CollapseAll();
        }
    }

    private void Materialize()
    {
        if (_materialized)
        {
            return;
        }

        _materialized = true;
        _children ??= new();
        if (_childrenFactory is not null)
        {
            foreach (var childValue in _childrenFactory(Value))
            {
                _children.Add(new TreeNode<T>(childValue, _childrenFactory) { Parent = this });
            }
        }
    }
}
