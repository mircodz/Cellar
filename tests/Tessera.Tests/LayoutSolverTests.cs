using Tessera.Layout;
using Tessera.Primitives;

namespace Tessera.Tests;

public class LayoutSolverTests
{
    private static int Sum(int[] a)
    {
        int s = 0;
        foreach (var x in a)
        {
            s += x;
        }

        return s;
    }

    [Fact]
    public void FixedLengths_ExactSplit()
    {
        var sizes = LayoutSolver.Solve(100, [Constraint.Length(30), Constraint.Length(70)]);
        Assert.Equal([30, 70], sizes);
    }

    [Fact]
    public void Fill_SharesLeftoverEqually()
    {
        var sizes = LayoutSolver.Solve(100, [Constraint.Length(20), Constraint.Fill(), Constraint.Fill()]);
        Assert.Equal(20, sizes[0]);
        Assert.Equal(80, sizes[1] + sizes[2]);
        Assert.Equal(40, sizes[1]);
        Assert.Equal(40, sizes[2]);
    }

    [Fact]
    public void Fill_WeightsAreProportional()
    {
        var sizes = LayoutSolver.Solve(90, [Constraint.Fill(1), Constraint.Fill(2)]);
        Assert.Equal(90, Sum(sizes));
        Assert.Equal(30, sizes[0]);
        Assert.Equal(60, sizes[1]);
    }

    [Fact]
    public void Percentage_SumsToTotal()
    {
        var sizes = LayoutSolver.Solve(100, [Constraint.Percentage(25), Constraint.Percentage(75)]);
        Assert.Equal([25, 75], sizes);
    }

    [Fact]
    public void Min_GrowsToFillLeftover()
    {
        // Single Min(10) against 50 => grows to 50.
        var sizes = LayoutSolver.Solve(50, [Constraint.Min(10)]);
        Assert.Equal(50, sizes[0]);
    }

    [Fact]
    public void Max_CapsSize_LeftoverGoesToFill()
    {
        var sizes = LayoutSolver.Solve(100, [Constraint.Max(20), Constraint.Fill()]);
        Assert.Equal(20, sizes[0]);
        Assert.Equal(80, sizes[1]);
    }

    [Fact]
    public void AllCapped_LeavesRemainderUnfilled_RatherThanBreakACap()
    {
        // Every slot is capped and the caps fall short of the total. Nothing may grow past its
        // cap, so the leftover stays unfilled (add a Fill to consume it). The result never
        // overflows and never exceeds a cap.
        var sizes = LayoutSolver.Solve(100, [Constraint.Max(10), Constraint.Max(10)]);
        Assert.Equal(10, sizes[0]);
        Assert.Equal(10, sizes[1]);

        var mixed = LayoutSolver.Solve(100, [Constraint.Length(20), Constraint.Max(10)]);
        Assert.Equal(20, mixed[0]);
        Assert.Equal(10, mixed[1]);
    }

    [Fact]
    public void OverConstrained_ShrinksToFit()
    {
        var sizes = LayoutSolver.Solve(50, [Constraint.Length(40), Constraint.Length(40)]);
        Assert.Equal(50, Sum(sizes)); // never overflows
        Assert.Equal(40, sizes[0]);
        Assert.Equal(10, sizes[1]);
    }

    [Fact]
    public void TinyTotal_DoesNotCrash()
    {
        var sizes = LayoutSolver.Solve(1, [Constraint.Fill(), Constraint.Fill(), Constraint.Fill()]);
        Assert.Equal(1, Sum(sizes));
    }

    [Fact]
    public void ZeroTotal_AllZero()
    {
        var sizes = LayoutSolver.Solve(0, [Constraint.Length(10), Constraint.Fill()]);
        Assert.Equal(0, Sum(sizes));
    }

    [Fact]
    public void Split_TilesParentExactly_Horizontal()
    {
        var rects = LayoutSolver.Split(new Rect(0, 0, 100, 10), Direction.Horizontal,
            Constraint.Length(30), Constraint.Fill());
        Assert.Equal(new Rect(0, 0, 30, 10), rects[0]);
        Assert.Equal(new Rect(30, 0, 70, 10), rects[1]);
        Assert.Equal(100, rects[1].Right); // no gap, no overflow
    }

    [Fact]
    public void Split_Vertical_OffsetsCorrectly()
    {
        var rects = LayoutSolver.Split(new Rect(5, 5, 20, 30), Direction.Vertical,
            Constraint.Length(10), Constraint.Fill());
        Assert.Equal(new Rect(5, 5, 20, 10), rects[0]);
        Assert.Equal(new Rect(5, 15, 20, 20), rects[1]);
    }

    [Fact]
    public void FillRemainder_DistributedLeftToRight()
    {
        // 3 equal fills over 10 => 4,3,3 (largest remainder to the left).
        var sizes = LayoutSolver.Solve(10, [Constraint.Fill(), Constraint.Fill(), Constraint.Fill()]);
        Assert.Equal(10, Sum(sizes));
        Assert.Equal(4, sizes[0]);
        Assert.Equal(3, sizes[1]);
        Assert.Equal(3, sizes[2]);
    }

    [Fact]
    public void Percentages_ThatRoundShort_StillTileExactly()
    {
        // 33% × 3 rounds to 33+33+33 = 99; the leftover cell must be handed back so the
        // split covers the full width (bug: last column was never painted).
        var sizes = LayoutSolver.Solve(100, [Constraint.Percentage(33), Constraint.Percentage(33), Constraint.Percentage(33)]);
        Assert.Equal(100, Sum(sizes));
    }

    [Fact]
    public void Percentages_WithLargeShortfall_StillTileExactly()
    {
        var sizes = LayoutSolver.Solve(100,
            [Constraint.Percentage(40), Constraint.Percentage(40)]);

        Assert.Equal([50, 50], sizes);
    }

    [Fact]
    public void OversizedFixedConstraints_DoNotOverflowArithmetic()
    {
        var sizes = LayoutSolver.Solve(100,
            [Constraint.Length(int.MaxValue), Constraint.Length(int.MaxValue)]);

        Assert.Equal(100, Sum(sizes));
        Assert.All(sizes, size => Assert.True(size >= 0));
    }

    [Fact]
    public void Overflow_ShrinkRespectsMin()
    {
        // Length(80) + Min(30) into 100 overshoots by 10. The Min slot must keep its floor;
        // the fixed-length slot yields instead (bug: Min was silently shrunk to 20).
        var sizes = LayoutSolver.Solve(100, [Constraint.Length(80), Constraint.Min(30)]);
        Assert.Equal(100, Sum(sizes));
        Assert.True(sizes[1] >= 30, $"Min(30) slot shrank to {sizes[1]}");
        Assert.Equal(70, sizes[0]);
    }

    [Fact]
    public void Overflow_MinsBeyondTotal_StillFit()
    {
        // Two Min slots that together exceed the total: no floor can be honored, but the
        // result must still sum to total without going negative.
        var sizes = LayoutSolver.Solve(50, [Constraint.Min(40), Constraint.Min(40)]);
        Assert.Equal(50, Sum(sizes));
        Assert.True(sizes[0] >= 0 && sizes[1] >= 0);
    }
}
