using System;

namespace Cellar.Primitives;

/// <summary>A rectangle in cell coordinates: top-left (X,Y) spanning Width×Height (clamped ≥0).</summary>
public readonly record struct Rect
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
    }

    public Rect(Point origin, Size size)
        : this(origin.X, origin.Y, size.Width, size.Height) { }

    public static Rect Empty => new(0, 0, 0, 0);

    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public Size Size => new(Width, Height);
    public Point Origin => new(X, Y);
    public bool IsEmpty => Width == 0 || Height == 0;
    public int Area => Width * Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
    public bool Contains(Point p) => Contains(p.X, p.Y);

    /// <summary>Shrinks the rect inward by the given thickness, clamping at zero size.</summary>
    public Rect Deflate(Thickness t)
    {
        var x = X + t.Left;
        var y = Y + t.Top;
        var w = Width - t.Left - t.Right;
        var h = Height - t.Top - t.Bottom;
        return new Rect(x, y, w < 0 ? 0 : w, h < 0 ? 0 : h);
    }

    /// <summary>Returns the overlap of two rects, or <see cref="Empty"/> if disjoint.</summary>
    public Rect Intersect(Rect other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min(Right, other.Right);
        var y2 = Math.Min(Bottom, other.Bottom);
        if (x2 <= x1 || y2 <= y1)
        {
            return Empty;
        }

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }
}
