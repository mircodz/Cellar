namespace Cellar.Primitives;

/// <summary>A width/height in cells. Never negative; construction clamps to zero.</summary>
public readonly record struct Size
{
    public int Width { get; }
    public int Height { get; }

    public Size(int width, int height)
    {
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
    }

    public static Size Empty => new(0, 0);

    public bool IsEmpty => Width == 0 || Height == 0;
}
