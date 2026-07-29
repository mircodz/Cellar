using Cellar.Primitives;
using Cellar.Rendering;

namespace Cellar.Widgets;

/// <summary>Insets a child widget by a fixed <see cref="Thickness"/> on each edge.</summary>
public sealed class Padding : Widget
{
    public Widget Child { get; set; }
    public Thickness Insets { get; set; }

    public Padding(Widget child, Thickness insets)
    {
        Child = child;
        Insets = insets;
    }

    public Padding(Widget child, int uniform) : this(child, new Thickness(uniform)) { }

    protected override void VisitChildren(System.Action<Widget> visit) => visit(Child);

    /// <summary>Focus flows through to the child.</summary>
    public override bool HasFocus
    {
        get => Child.HasFocus;
        set => Child.HasFocus = value;
    }

    /// <summary>Reports the child's focusability so containers forward focus into the padding.</summary>
    public override bool IsFocusable => Child.IsFocusable;

    public override void Render(Surface surface, Rect area)
    {
        var inner = area.Deflate(Insets);
        if (inner.IsEmpty)
        {
            return;
        }

        surface.SetClip(inner);
        Child.Render(surface, inner);
        surface.ResetClip();
    }

    public override bool OnEvent(Terminal.InputEvent e) => Child.OnEvent(e);
}
