using System;
using System.Collections.Generic;
using Tessera.Primitives;

namespace Tessera.Layout;

/// <summary>
/// Splits a <see cref="Rect"/> into sub-rects along one axis by a list of
/// <see cref="Constraint"/>s. Rounding remainder is distributed deterministically
/// left-to-right. The slots tile the parent exactly as long as something can absorb the
/// slack — a <see cref="Constraint.Fill"/> or <see cref="Constraint.Min"/> slot, or a
/// <see cref="Constraint.Percentage"/>/<see cref="Constraint.Ratio"/> slot. If every slot is
/// capped (all <see cref="Constraint.Length"/>/<see cref="Constraint.Max"/>) and the caps sum
/// to less than the total, the leftover is left unfilled rather than break a cap — add a Fill
/// to consume it.
/// </summary>
public static class LayoutSolver
{
    /// <summary>Resolves <paramref name="constraints"/> against <paramref name="area"/>, one rect each.</summary>
    public static Rect[] Split(Rect area, Direction direction, params Constraint[] constraints)
    {
        int n = constraints.Length;
        if (n == 0)
        {
            return [];
        }

        int total = direction == Direction.Horizontal ? area.Width : area.Height;
        Span<int> sizes = n <= 64 ? stackalloc int[n] : new int[n];
        SolveInto(total, constraints, sizes);

        var result = new Rect[n];
        int offset = direction == Direction.Horizontal ? area.X : area.Y;
        for (int i = 0; i < n; i++)
        {
            result[i] = direction == Direction.Horizontal
                ? new Rect(offset, area.Y, sizes[i], area.Height)
                : new Rect(area.X, offset, area.Width, sizes[i]);
            offset += sizes[i];
        }
        return result;
    }

    /// <summary>Computes the extent per constraint for a total. Exposed for direct testing.</summary>
    public static int[] Solve(int total, IReadOnlyList<Constraint> constraints)
    {
        var sizes = new int[constraints.Count];
        SolveInto(total, constraints, sizes);
        return sizes;
    }

    /// <summary>Allocation-free <see cref="Solve"/>: writes extents into <paramref name="sizes"/>.</summary>
    public static void SolveInto(int total, IReadOnlyList<Constraint> constraints, Span<int> sizes)
    {
        int n = constraints.Count;
        sizes.Slice(0, n).Clear();
        if (total <= 0)
        {
            return;
        }

        // Small n (columns / stack children) stays on the stack; large layouts fall back to heap.
        const int StackMax = 64;
        int[]? rented = n > StackMax ? new int[n * 2] : null;
        double[]? rentedW = n > StackMax ? new double[n] : null;
        bool[]? rentedO = n > StackMax ? new bool[n * 2] : null;
        Span<int> min = rented is null ? stackalloc int[n] : rented.AsSpan(0, n);
        Span<int> max = rented is null ? stackalloc int[n] : rented.AsSpan(n, n);
        Span<double> flexWeight = rentedW ?? stackalloc double[n];
        Span<bool> open = rentedO is null ? stackalloc bool[n] : rentedO.AsSpan(0, n);
        Span<bool> proportional = rentedO is null ? stackalloc bool[n] : rentedO.AsSpan(n, n);
        double totalFlexWeight = 0;
        long fixedSum = 0;

        for (int i = 0; i < n; i++)
        {
            var c = constraints[i];
            switch (c.Type)
            {
                case Constraint.Kind.Length:
                    min[i] = max[i] = c.A;
                    sizes[i] = c.A;
                    fixedSum += c.A;
                    break;
                case Constraint.Kind.Percentage:
                    int p = (int)Math.Round(total * (c.A / 100.0));
                    min[i] = max[i] = p;
                    sizes[i] = p;
                    fixedSum += p;
                    proportional[i] = true;
                    break;
                case Constraint.Kind.Ratio:
                    int r = (int)Math.Round(total * ((double)c.A / c.B));
                    min[i] = max[i] = r;
                    sizes[i] = r;
                    fixedSum += r;
                    proportional[i] = true;
                    break;
                case Constraint.Kind.Min:
                    min[i] = c.A;
                    max[i] = int.MaxValue;
                    sizes[i] = c.A;
                    fixedSum += c.A;
                    flexWeight[i] = 1;
                    totalFlexWeight += 1;
                    break;
                case Constraint.Kind.Max:
                    min[i] = 0;
                    max[i] = c.A;
                    sizes[i] = 0;
                    flexWeight[i] = 1;
                    totalFlexWeight += 1;
                    break;
                case Constraint.Kind.Fill:
                    min[i] = 0;
                    max[i] = int.MaxValue;
                    sizes[i] = 0;
                    flexWeight[i] = c.A;
                    totalFlexWeight += c.A;
                    break;
            }
        }

        int remaining = fixedSum >= total ? 0 : total - (int)fixedSum;

        // Distribute leftover space to flexible slots by weight, respecting max bounds.
        // Iterate because clamping one slot at its max frees weight for the others.
        if (remaining > 0 && totalFlexWeight > 0)
        {
            for (int i = 0; i < n; i++)
            {
                open[i] = flexWeight[i] > 0 && sizes[i] < max[i];
            }

            Span<int> want = n > StackMax ? new int[n] : stackalloc int[n];
            while (remaining > 0)
            {
                double activeWeight = 0;
                for (int i = 0; i < n; i++)
                {
                    if (open[i])
                    {
                        activeWeight += flexWeight[i];
                    }
                }

                if (activeWeight <= 0)
                {
                    break;
                }

                bool anyClamped = false;
                int distributedThisPass = 0;
                // Largest-remainder distribution for determinism.
                for (int i = 0; i < n; i++)
                {
                    want[i] = open[i] ? (int)Math.Floor(remaining * (flexWeight[i] / activeWeight)) : 0;
                }
                // Hand out floor amounts, clamping at max.
                for (int i = 0; i < n; i++)
                {
                    if (!open[i] || want[i] == 0)
                    {
                        continue;
                    }

                    int grant = Math.Min(want[i], max[i] - sizes[i]);
                    sizes[i] += grant;
                    distributedThisPass += grant;
                    if (sizes[i] >= max[i]) { open[i] = false; anyClamped = true; }
                }
                remaining -= distributedThisPass;

                // Give the sub-weight remainder one cell at a time, left to right.
                if (!anyClamped)
                {
                    for (int i = 0; i < n && remaining > 0; i++)
                    {
                        if (!open[i])
                        {
                            continue;
                        }

                        int grant = Math.Min(1, max[i] - sizes[i]);
                        sizes[i] += grant;
                        remaining -= grant;
                        if (sizes[i] >= max[i])
                        {
                            open[i] = false;
                        }
                    }
                    if (distributedThisPass == 0 && remaining > 0)
                    {
                        // No progress possible (all open slots at max) — stop.
                        break;
                    }
                }
            }
        }

        if (remaining > 0 && totalFlexWeight <= 0)
        {
            bool distributed;
            do
            {
                distributed = false;
                for (int i = 0; i < n && remaining > 0; i++)
                {
                    if (proportional[i])
                    {
                        sizes[i]++;
                        remaining--;
                        distributed = true;
                    }
                }
            }
            while (remaining > 0 && distributed);
        }

        long sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += sizes[i];
        }

        long overflow = sum - total;
        for (int pass = 0; pass < 2 && overflow > 0; pass++)
        {
            for (int i = n - 1; i >= 0 && overflow > 0; i--)
            {
                int floor = pass == 0 && constraints[i].Type == Constraint.Kind.Min
                    ? min[i]
                    : 0;
                long reducible = Math.Min(overflow, (long)sizes[i] - floor);
                if (reducible <= 0)
                {
                    continue;
                }

                sizes[i] -= (int)reducible;
                overflow -= reducible;
            }
        }
    }
}
