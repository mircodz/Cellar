namespace Cellar.Primitives;

/// <summary>Edge insets in cells, used for padding, margins, and border widths.</summary>
public readonly record struct Thickness(int Left, int Top, int Right, int Bottom)
{
    public Thickness(int uniform) : this(uniform, uniform, uniform, uniform) { }

    public Thickness(int horizontal, int vertical)
        : this(horizontal, vertical, horizontal, vertical) { }

    public static Thickness Zero => new(0, 0, 0, 0);

    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;
}
