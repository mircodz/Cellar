using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// A framed "card" container: themed border, interior padding, and optional background fill
/// around a single child. Nicer defaults than a bare <see cref="Border"/>; colors follow
/// <see cref="Theme.Current"/> unless overridden.
/// </summary>
public sealed class Panel : Widget
{
    private readonly Border _border = new();

    public Widget? Child { get; set; }
    public string? Title { get; set; }
    public BorderStyle BorderStyle { get; set; } = BorderStyle.Rounded;
    public Thickness Padding { get; set; } = new(1, 0);

    /// <summary>Border line style. Null uses the theme border color.</summary>
    public Style? BorderLineStyle { get; set; }

    /// <summary>Title style. Null uses the theme accent (bold).</summary>
    public Style? TitleStyle { get; set; }

    /// <summary>Optional background fill for the panel interior. Null leaves it transparent.</summary>
    public Color? Background { get; set; }

    public Panel(Widget? child = null, string? title = null)
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

    /// <summary>Focus flows through to the child.</summary>
    public override bool HasFocus
    {
        get => Child?.HasFocus ?? false;
        set
        {
            if (Child is not null)
            {
                Child.HasFocus = value;
            }
        }
    }

    /// <summary>Reports the child's focusability so containers forward focus in.</summary>
    public override bool IsFocusable => Child?.IsFocusable ?? false;

    public override void Render(Surface surface, Rect area)
    {
        if (area.Width < 2 || area.Height < 2)
        {
            return;
        }

        if (Background is { } bg)
        {
            surface.FillRect(area, new Style(Color.Default, bg));
        }

        _border.Child = null;
        _border.Title = Title;
        _border.BorderStyle = BorderStyle;
        _border.Style = BorderLineStyle;
        _border.TitleStyle = TitleStyle;
        _border.Render(surface, area);

        if (Child is not null)
        {
            var inner = Border.Inner(area).Deflate(Padding);
            if (!inner.IsEmpty)
            {
                surface.SetClip(inner);
                Child.Render(surface, inner);
                surface.ResetClip();
            }
        }
    }

    public override bool OnEvent(Terminal.InputEvent e) => Child?.OnEvent(e) ?? false;
}
