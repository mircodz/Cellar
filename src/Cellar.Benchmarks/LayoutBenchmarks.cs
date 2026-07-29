using BenchmarkDotNet.Attributes;
using Cellar.Layout;
using Cellar.Primitives;

namespace Cellar.Benchmarks;

/// <summary>
/// Benchmarks the layout constraint solver, which runs for every container each frame.
/// </summary>
[MemoryDiagnoser]
public class LayoutBenchmarks
{
    private Constraint[] _mixed = null!;
    private Rect _area;

    [GlobalSetup]
    public void Setup()
    {
        _area = new Rect(0, 0, 200, 50);
        _mixed = new[]
        {
            Constraint.Length(10),
            Constraint.Percentage(25),
            Constraint.Fill(1),
            Constraint.Fill(2),
            Constraint.Min(5),
            Constraint.Max(30),
        };
    }

    [Benchmark(Description = "Solve 6 mixed constraints")]
    public int[] SolveMixed() => LayoutSolver.Solve(200, _mixed);

    [Benchmark(Description = "Split rect (6 constraints)")]
    public Rect[] SplitRect() => LayoutSolver.Split(_area, Direction.Horizontal, _mixed);
}
