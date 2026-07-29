using BenchmarkDotNet.Attributes;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Text;

namespace Cellar.Benchmarks;

/// <summary>
/// Benchmarks the styled-text engine: building a multi-span StyledText, measuring it, and
/// word-wrapping to a width. Widgets rebuild text every frame, so allocation here matters.
/// </summary>
[MemoryDiagnoser]
public class TextBenchmarks
{
    private Surface _surface = null!;
    private StyledText _styled = null!;
    private const string Paragraph =
        "This paragraph is word-wrapped and justified by the styled-text engine. " +
        "Wide glyphs and combining marks are measured correctly, so alignment stays " +
        "intact across the whole block of flowing text that a widget might render.";

    [GlobalSetup]
    public void Setup()
    {
        _surface = new Surface(120, 40);
        _styled = StyledText.Of("error: ").Bold().Fg(Color.Red)
            .Append("file not found ").Fg(Color.White)
            .Append("(retry?)").Italic().Fg(Color.Cyan);
    }

    [Benchmark(Description = "Build 3-span StyledText")]
    public int BuildStyledText()
    {
        var t = StyledText.Of("error: ").Bold().Fg(Color.Red)
            .Append("file not found ").Fg(Color.White)
            .Append("(retry?)").Italic().Fg(Color.Cyan);
        return t.Width;
    }

    [Benchmark(Description = "Measure StyledText width")]
    public int MeasureWidth() => _styled.Width;

    [Benchmark(Description = "Word-wrap paragraph to 40 cols")]
    public int WrapParagraph()
    {
        var lines = TextRenderer.Wrap(StyledText.Of(Paragraph), 40);
        return lines.Count;
    }

    [Benchmark(Description = "DrawLine styled, justified")]
    public void DrawLineStyled()
    {
        TextRenderer.DrawLine(_surface, 0, 0, 120, _styled, Justify.Center);
    }

    [Benchmark(Description = "StringWidth of mixed text")]
    public int StringWidthMixed() => Unicode.StringWidth("Hello 你好 world é combining");
}
