using System;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Widgets;

namespace Tessera.Demo;

/// <summary>A GitHub-style contribution heatmap: a 7×N grid of cells shaded by intensity.
/// Pure background fills over a color ramp.</summary>
public sealed class Heatmap : Widget
{
    public double[,] Values { get; init; } = new double[0, 0]; // [week, day], 0..1
    public Color Low { get; init; } = Color.Rgb(30, 40, 30);
    public Color High { get; init; } = Color.Rgb(60, 200, 90);

    public override void Render(Surface surface, Rect area)
    {
        int weeks = Values.GetLength(0), days = Values.GetLength(1);
        for (int w = 0; w < weeks; w++)
        {
            for (int d = 0; d < days; d++)
            {
                double t = Math.Clamp(Values[w, d], 0, 1);
                var c = Tessera.Primitives.Colors.Lerp(Low, High, t);
                // Each day is a 2-wide cell for a squarer look.
                var rect = new Rect(area.X + w * 2, area.Y + d, 2, 1);
                surface.FillRect(rect, new Style(Color.Default, c));
            }
        }
    }
}