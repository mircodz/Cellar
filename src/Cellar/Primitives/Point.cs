namespace Cellar.Primitives;

/// <summary>A point in cell coordinates. Origin is top-left, (0,0).</summary>
public readonly record struct Point(int X, int Y)
{
    public static Point Origin => new(0, 0);

    public Point Offset(int dx, int dy) => new(X + dx, Y + dy);
}
