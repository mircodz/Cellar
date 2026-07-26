using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Widgets.Charts;

namespace Tessera.Benchmarks;

/// <summary>
/// Benchmarks the input decoder — it runs on every keystroke/mouse move, parsing raw bytes
/// into events. Cheap and hot, so allocation matters.
/// </summary>
[MemoryDiagnoser]
public class InputBenchmarks
{
    private byte[] _ascii = null!;
    private byte[] _mouseDrag = null!;
    private byte[] _arrows = null!;
    private byte[] _utf8 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ascii = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog");
        // A burst of SGR mouse-drag reports, as during a selection.
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++)
        {
            sb.Append($"\x1b[<32;{10 + i};{5 + i}M");
        }

        _mouseDrag = Encoding.UTF8.GetBytes(sb.ToString());
        _arrows = Encoding.UTF8.GetBytes("\x1b[A\x1b[B\x1b[C\x1b[D\x1b[A\x1b[B\x1b[C\x1b[D");
        _utf8 = Encoding.UTF8.GetBytes("héllo 你好 wörld café ünïcöde");
    }

    [Benchmark(Description = "Decode ASCII text")]
    public int DecodeAscii() => new InputDecoder().Feed(_ascii).Count;

    [Benchmark(Description = "Decode mouse-drag burst (SGR)")]
    public int DecodeMouseDrag() => new InputDecoder().Feed(_mouseDrag).Count;

    [Benchmark(Description = "Decode arrow-key sequences")]
    public int DecodeArrows() => new InputDecoder().Feed(_arrows).Count;

    [Benchmark(Description = "Decode UTF-8 multibyte")]
    public int DecodeUtf8() => new InputDecoder().Feed(_utf8).Count;
}

/// <summary>
/// Benchmarks text selection: extracting a large highlighted region into a string and
/// applying the highlight over the surface. Runs on drag and copy.
/// </summary>
[MemoryDiagnoser]
public class SelectionBenchmarks
{
    private Surface _surface = null!;
    private Selection _fullScreen;

    [GlobalSetup]
    public void Setup()
    {
        _surface = new Surface(120, 40);
        for (int y = 0; y < 40; y++)
        {
            _surface.DrawText(0, y, new string('x', 120), Style.Default);
        }

        _fullScreen = new Selection(new Point(0, 0), new Point(119, 39));
    }

    [Benchmark(Description = "ExtractText full screen")]
    public int ExtractFullScreen() => _surface.ExtractText(_fullScreen).Length;

    [Benchmark(Description = "HighlightSelection full screen")]
    public void HighlightFullScreen() =>
        _surface.HighlightSelection(_fullScreen, Color.White, Color.Blue);
}

/// <summary>
/// Benchmarks chart rendering. The braille line chart is the heaviest (2×4 subpixel packing);
/// the sparkline runs live on a streaming series.
/// </summary>
[MemoryDiagnoser]
public class ChartBenchmarks
{
    private Surface _surface = null!;
    private LineChart _line = null!;
    private Sparkline _spark = null!;
    private BarChart _bar = null!;
    private FlameGraph<int> _flame = null!;
    private Rect _area;

    [GlobalSetup]
    public void Setup()
    {
        _surface = new Surface(120, 20);
        _area = new Rect(0, 0, 120, 20);

        _line = new LineChart { UseBraille = true };
        var s = new Series("wave");
        for (int i = 0; i < 400; i++)
        {
            s.Add(i, Math.Sin(i * 0.1) * Math.Cos(i * 0.03));
        }

        _line.Series.Add(s);

        _spark = new Sparkline();
        for (int i = 0; i < 200; i++)
        {
            _spark.Push(Math.Sin(i * 0.2));
        }

        _bar = new BarChart
        {
            Values = { ("a", 42), ("b", 18), ("c", 27), ("d", 9), ("e", 33), ("f", 15) },
        };

        // A flamegraph of a realistic allocation trace: a balanced tree ~8 levels deep with a
        // fan-out of 3, giving a few hundred frames — far more than fit on screen, so this
        // exercises the recursive layout + clipping under load.
        _flame = new FlameGraph<int> { LabelSelector = n => $"Frame_{n.Value}" };
        var root = new FlameNode<int>(0, "root", 1_000_000);
        BuildFlameTree(root, depth: 0, maxDepth: 8, fanout: 3, weight: root.Weight);
        _flame.Root = root;
    }

    private static int _flameId;
    private static void BuildFlameTree(FlameNode<int> node, int depth, int maxDepth, int fanout, double weight)
    {
        if (depth >= maxDepth) return;
        double childWeight = weight * 0.9 / fanout; // children account for ~90% of the parent
        for (int i = 0; i < fanout; i++)
        {
            var child = node.Add(++_flameId, $"m_{_flameId}", childWeight);
            BuildFlameTree(child, depth + 1, maxDepth, fanout, childWeight);
        }
    }

    [Benchmark(Description = "LineChart braille render")]
    public void LineChartBraille() => _line.Render(_surface, _area);

    [Benchmark(Description = "Sparkline push + render")]
    public void SparklinePushRender()
    {
        _spark.Push(0.5);
        _spark.Render(_surface, new Rect(0, 0, 120, 1));
    }

    [Benchmark(Description = "BarChart render")]
    public void BarChartRender() => _bar.Render(_surface, new Rect(0, 0, 120, 6));

    [Benchmark(Description = "FlameGraph render (8-deep, fan-out 3)")]
    public void FlameGraphRender() => _flame.Render(_surface, _area);
}

/// <summary>
/// Scaling test for the flame graph: renders trees of very different total sizes into the
/// SAME viewport. Because the widget stops recursing once a subtree rounds to sub-cell width,
/// render cost should track the number of <i>visible</i> frames (bounded by the viewport),
/// not the total tree size — so a 10x bigger tree should NOT be 10x slower.
/// </summary>
[MemoryDiagnoser]
public class FlameGraphScaleBenchmarks
{
    private Surface _surface = null!;
    private Rect _area;
    private FlameGraph<int> _flame = null!;
    private int _totalFrames;

    // Total-frame targets an order of magnitude apart: a few hundred, a few thousand, tens of
    // thousands, and a genuinely huge ~130k-frame trace.
    [Params(6, 9, 11, 12)]
    public int MaxDepth;

    public int Fanout => 3;

    [GlobalSetup]
    public void Setup()
    {
        _surface = new Surface(160, 45); // a large terminal
        _area = _surface.Bounds;

        _totalFrames = 0;
        _flame = new FlameGraph<int> { LabelSelector = n => n.Label };
        var root = new FlameNode<int>(0, "root", 1_000_000);
        Build(root, 0, MaxDepth, Fanout, root.Weight);
        _flame.Root = root;
        System.Console.Error.WriteLine($"[MaxDepth={MaxDepth}] total frames = {_totalFrames}");
    }

    private void Build(FlameNode<int> node, int depth, int maxDepth, int fanout, double weight)
    {
        if (depth >= maxDepth) return;
        double childWeight = weight * 0.95 / fanout;
        for (int i = 0; i < fanout; i++)
        {
            _totalFrames++;
            var child = node.Add(_totalFrames, "frame", childWeight);
            Build(child, depth + 1, maxDepth, fanout, childWeight);
        }
    }

    [Benchmark(Description = "FlameGraph render (scaling by total frames)")]
    public void Render() => _flame.Render(_surface, _area);
}
