using System;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Widgets;

namespace Cellar.Demo;

/// <summary>
/// Shows a child and offers it every event first; keys it doesn't use go to a <see cref="Keymap"/>. That's how an
/// app wires a keymap too (<c>app.OnUnhandledEvent = keymap.Handle</c>), scoped here to one demo page.
/// </summary>
internal sealed class KeyHost(Widget child, Keymap keymap, Action? afterKey = null) : Widget
{
    public override bool IsFocusable => true;

    public override bool HasFocus
    {
        get => child.HasFocus || _focus;
        set
        {
            _focus = value;
            child.HasFocus = value;
        }
    }

    private bool _focus;

    public override void Render(Surface surface, Rect area) => child.Render(surface, area);

    protected override void VisitChildren(Action<Widget> visit) => visit(child);

    public override bool OnEvent(InputEvent e)
    {
        if (child.OnEvent(e))
        {
            return true;
        }

        bool used = keymap.Handle(e);
        if (used)
        {
            afterKey?.Invoke();
        }

        return used;
    }
}
