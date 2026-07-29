using System.Collections.Generic;

namespace Cellar.Widgets.Charts;

/// <summary>
/// A node in a <see cref="FlameGraph{T}"/>: a labeled frame with a numeric weight (bytes,
/// samples, time) and children. Width in the graph is its weight relative to the zoomed root.
/// </summary>
public sealed class FlameNode<T>
{
    public T Value { get; }
    public string Label { get; set; }
    public double Weight { get; set; }
    public List<FlameNode<T>> Children { get; } = new();
    public FlameNode<T>? Parent { get; private set; }

    public FlameNode(T value, string label, double weight)
    {
        Value = value;
        Label = label;
        Weight = weight;
    }

    /// <summary>Adds a child frame and returns it.</summary>
    public FlameNode<T> Add(T value, string label, double weight)
    {
        var child = new FlameNode<T>(value, label, weight) { Parent = this };
        Children.Add(child);
        return child;
    }

    /// <summary>Adds an already-built child (e.g. a subtree).</summary>
    public FlameNode<T> Add(FlameNode<T> child)
    {
        child.Parent = this;
        Children.Add(child);
        return child;
    }

    /// <summary>Depth from the root (root = 0).</summary>
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    /// <summary>The sum of the children's weights (their combined inclusive cost).</summary>
    public double ChildrenWeight
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < Children.Count; i++) sum += Children[i].Weight;
            return sum;
        }
    }
}
