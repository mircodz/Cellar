using System;

namespace Cellar.Layout;

/// <summary>The axis along which a region is split.</summary>
public enum Direction
{
    Horizontal,
    Vertical,
}

/// <summary>
/// A sizing rule for one slot in a split (ratatui-style). Priority: fixed
/// <see cref="Length"/> and <see cref="Min"/>/<see cref="Max"/> bounds, then
/// <see cref="Percentage"/>/<see cref="Ratio"/>, then <see cref="Fill"/> shares the rest.
/// </summary>
public readonly struct Constraint
{
    /// <summary>Which kind of sizing rule this constraint is.</summary>
    public enum Kind { Length, Percentage, Ratio, Min, Max, Fill }

    public Kind Type { get; }
    public int A { get; }
    public int B { get; }

    private Constraint(Kind type, int a, int b)
    {
        Type = type;
        A = a;
        B = b;
    }

    /// <summary>An exact size in cells.</summary>
    public static Constraint Length(int cells) => new(Kind.Length, Math.Max(0, cells), 0);

    /// <summary>A percentage (0-100) of the total available extent.</summary>
    public static Constraint Percentage(int percent) => new(Kind.Percentage, Math.Clamp(percent, 0, 100), 0);

    /// <summary><paramref name="num"/>/<paramref name="den"/> of the total extent.</summary>
    public static Constraint Ratio(int num, int den) => new(Kind.Ratio, Math.Max(0, num), Math.Max(1, den));

    /// <summary>At least this many cells; grows to fill leftover space if available.</summary>
    public static Constraint Min(int cells) => new(Kind.Min, Math.Max(0, cells), 0);

    /// <summary>At most this many cells.</summary>
    public static Constraint Max(int cells) => new(Kind.Max, Math.Max(0, cells), 0);

    /// <summary>Takes a share of leftover space proportional to <paramref name="weight"/>.</summary>
    public static Constraint Fill(int weight = 1) => new(Kind.Fill, Math.Max(1, weight), 0);
}
