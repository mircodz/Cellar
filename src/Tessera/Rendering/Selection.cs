using System;
using Tessera.Primitives;

namespace Tessera.Rendering;

/// <summary>How a <see cref="Selection"/> covers the cells between its anchor and focus.</summary>
public enum SelectionMode
{
    /// <summary>Text-flow: anchor→end-of-line, full lines between, start-of-line→focus.</summary>
    Linear,
    /// <summary>A rectangle between the anchor and focus corners (column selection).</summary>
    Block,
}

/// <summary>
/// A text selection over a <see cref="Surface"/>, from an anchor cell to a focus cell
/// (direction-agnostic). Use <see cref="Contains"/> during highlight rendering and
/// <see cref="Surface.ExtractText"/> to read the selected text.
/// </summary>
public readonly struct Selection
{
    public Point Anchor { get; }
    public Point Focus { get; }
    public SelectionMode Mode { get; }

    public Selection(Point anchor, Point focus, SelectionMode mode = SelectionMode.Linear)
    {
        Anchor = anchor;
        Focus = focus;
        Mode = mode;
    }

    /// <summary>True when anchor and focus are the same cell (nothing to copy).</summary>
    public bool IsEmpty => Anchor == Focus;

    /// <summary>The anchor/focus ordered so start ≤ end in reading order (row-major).</summary>
    public (Point Start, Point End) Normalized()
    {
        bool anchorFirst = Anchor.Y < Focus.Y || (Anchor.Y == Focus.Y && Anchor.X <= Focus.X);
        return anchorFirst ? (Anchor, Focus) : (Focus, Anchor);
    }

    /// <summary>Whether cell (x,y) falls inside the selection under the current mode.</summary>
    public bool Contains(int x, int y)
    {
        if (Mode == SelectionMode.Block)
        {
            int x0 = Math.Min(Anchor.X, Focus.X), x1 = Math.Max(Anchor.X, Focus.X);
            int y0 = Math.Min(Anchor.Y, Focus.Y), y1 = Math.Max(Anchor.Y, Focus.Y);
            return x >= x0 && x <= x1 && y >= y0 && y <= y1;
        }

        var (start, end) = Normalized();
        if (y < start.Y || y > end.Y)
        {
            return false;
        }

        if (start.Y == end.Y)
        {
            return x >= start.X && x <= end.X;
        }

        if (y == start.Y)
        {
            return x >= start.X;
        }

        if (y == end.Y)
        {
            return x <= end.X;
        }

        return true;
    }
}
