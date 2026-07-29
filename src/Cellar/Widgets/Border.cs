using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>The line style used to draw a <see cref="Border"/>.</summary>
public enum BorderStyle { Single, Rounded, Double, Thick }

/// <summary>
/// Draws a box (optionally titled) around a single child. Line/title styles default to the
/// ambient <see cref="Theme.Current"/> when unset.
/// </summary>
public sealed class Border : Widget
{
    public Widget? Child { get; set; }
    public string? Title { get; set; }
    public BorderStyle BorderStyle { get; set; } = BorderStyle.Rounded;

    /// <summary>Line style. If null, uses the current theme's border color.</summary>
    public Style? Style { get; set; }

    /// <summary>Title style. If null, uses the current theme's accent (bold).</summary>
    public Style? TitleStyle { get; set; }

    public Border(Widget? child = null, string? title = null)
    {
        Child = child;
        Title = title;
    }

    protected override void VisitChildren(System.Action<Widget> visit)
    {
        if (Child is not null)
        {
            visit(Child);
        }
    }

    private Style EffectiveStyle => Style ?? Theme.Current.BorderStyle;
    private Style EffectiveTitleStyle => TitleStyle ?? Theme.Current.HeaderStyle;

    /// <summary>The inner region available to the child after the 1-cell frame.</summary>
    public static Rect Inner(Rect area) => area.Deflate(new Thickness(1));

    public override void Render(Surface surface, Rect area)
    {
        if (area.Width < 2 || area.Height < 2)
        {
            return;
        }

        var g = Glyphs(BorderStyle);
        var lineStyle = EffectiveStyle;

        surface.Set(area.Left, area.Top, Cell.FromGrapheme(g.TopLeft, lineStyle));
        surface.Set(area.Right - 1, area.Top, Cell.FromGrapheme(g.TopRight, lineStyle));
        surface.Set(area.Left, area.Bottom - 1, Cell.FromGrapheme(g.BottomLeft, lineStyle));
        surface.Set(area.Right - 1, area.Bottom - 1, Cell.FromGrapheme(g.BottomRight, lineStyle));

        for (int x = area.Left + 1; x < area.Right - 1; x++)
        {
            surface.Set(x, area.Top, Cell.FromGrapheme(g.Horizontal, lineStyle));
            surface.Set(x, area.Bottom - 1, Cell.FromGrapheme(g.Horizontal, lineStyle));
        }

        for (int y = area.Top + 1; y < area.Bottom - 1; y++)
        {
            surface.Set(area.Left, y, Cell.FromGrapheme(g.Vertical, lineStyle));
            surface.Set(area.Right - 1, y, Cell.FromGrapheme(g.Vertical, lineStyle));
        }

        if (!string.IsNullOrEmpty(Title) && area.Width > 4)
        {
            string title = " " + Title + " ";
            int maxWidth = area.Width - 2;
            surface.SetClip(new Rect(area.Left + 1, area.Top, maxWidth, 1));
            surface.DrawText(area.Left + 2, area.Top, title, EffectiveTitleStyle);
            surface.SetClip(area); // restore to this widget's region
        }

        if (Child is not null)
        {
            var inner = Inner(area);
            if (!inner.IsEmpty)
            {
                surface.SetClip(inner);
                Child.Render(surface, inner);
                surface.SetClip(area);
            }
        }
    }

    private readonly record struct BorderGlyphs(
        string TopLeft,
        string TopRight,
        string BottomLeft,
        string BottomRight,
        string Horizontal,
        string Vertical);

    private static BorderGlyphs Glyphs(BorderStyle style) => style switch
    {
        BorderStyle.Single => new("┌", "┐", "└", "┘", "─", "│"),
        BorderStyle.Rounded => new("╭", "╮", "╰", "╯", "─", "│"),
        BorderStyle.Double => new("╔", "╗", "╚", "╝", "═", "║"),
        BorderStyle.Thick => new("┏", "┓", "┗", "┛", "━", "┃"),
        _ => new("┌", "┐", "└", "┘", "─", "│"),
    };
}
