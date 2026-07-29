using System.Collections.Generic;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Theming;

namespace Cellar.Widgets.Charts;

/// <summary>One legend entry: a swatch color and its label (optionally with a value/percent).</summary>
public sealed class LegendItem
{
    public string Label { get; set; }
    public Color Color { get; set; }
    public string? Value { get; set; }

    public LegendItem(string label, Color color, string? value = null)
    {
        Label = label;
        Color = color;
        Value = value;
    }
}

/// <summary>
/// A color legend: a row or column of "● label" entries naming the colors used by a chart,
/// each with an optional value/percentage.
/// </summary>
public sealed class Legend : Widget
{
    public List<LegendItem> Items { get; } = new();

    /// <summary>Lay entries left-to-right (true) or stacked top-to-bottom (false).</summary>
    public bool Horizontal { get; set; } = true;

    /// <summary>The swatch glyph.</summary>
    public string Swatch { get; set; } = "●";

    /// <summary>Cells of spacing between horizontal entries.</summary>
    public int Spacing { get; set; } = 2;

    public override Size Measure(Size available) =>
        Horizontal ? new Size(available.Width, 1) : new Size(available.Width, Items.Count);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty || Items.Count == 0)
        {
            return;
        }

        var textStyle = new Style(Theme.Current.Foreground, Color.Default);
        var mutedStyle = new Style(Theme.Current.Muted, Color.Default);

        if (Horizontal)
        {
            int x = area.X;
            foreach (var item in Items)
            {
                if (x >= area.Right)
                {
                    break;
                }

                x = DrawEntry(surface, x, area.Y, item, textStyle, mutedStyle);
                x += Spacing;
            }
        }
        else
        {
            int y = area.Y;
            foreach (var item in Items)
            {
                if (y >= area.Bottom)
                {
                    break;
                }

                DrawEntry(surface, area.X, y, item, textStyle, mutedStyle);
                y++;
            }
        }
    }

    private int DrawEntry(Surface surface, int x, int y, LegendItem item, Style textStyle, Style mutedStyle)
    {
        x = surface.DrawText(x, y, Swatch, new Style(item.Color, Color.Default));
        x = surface.DrawText(x, y, " ", textStyle);
        x = surface.DrawText(x, y, item.Label, textStyle);
        if (!string.IsNullOrEmpty(item.Value))
        {
            x = surface.DrawText(x, y, " ", mutedStyle);
            x = surface.DrawText(x, y, item.Value, mutedStyle);
        }
        return x;
    }
}
