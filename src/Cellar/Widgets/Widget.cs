using System;
using System.Collections.Generic;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;

namespace Cellar.Widgets;

/// <summary>
/// Base class for renderable UI elements. Draws itself into a <see cref="Rect"/> of a
/// <see cref="Surface"/> and may handle input. Widgets are retained (hold state across frames).
/// </summary>
public abstract class Widget
{
    private List<LinkHit>? _links;
    private object? _hoveredLink;

    /// <summary>Whether this widget has input focus. Container widgets override to propagate down.</summary>
    public virtual bool HasFocus { get; set; }

    /// <summary>Whether this widget can receive focus. Interactive widgets override to true.</summary>
    public virtual bool IsFocusable => false;

    /// <summary>Reports the desired size given available space. Default: fills what it is given.</summary>
    public virtual Size Measure(Size available) => available;

    /// <summary>Draws the widget into <paramref name="area"/>. The clip is already set to it.</summary>
    public abstract void Render(Surface surface, Rect area);

    /// <summary>Handles an input event; returns true if consumed. Default: ignores all input.</summary>
    public virtual bool OnEvent(InputEvent e) => false;

    // Mount and unmount run once per live-tree transition, not per frame. Containers expose
    // descendants by overriding VisitChildren.

    /// <summary>Called once when the widget enters the live app tree. Override to start
    /// lifecycle-scoped work. Always call the base or drive children via <see cref="VisitChildren"/>.</summary>
    protected virtual void OnMount(App app) { }

    /// <summary>Called once when the widget leaves the tree or the app tears down. Override to
    /// cancel background work / release resources.</summary>
    protected virtual void OnUnmount() { }

    /// <summary>Enumerates this widget's live children for lifecycle propagation. Containers
    /// override to forward <see cref="Mount"/>/<see cref="Unmount"/> to their children. Default:
    /// no children.</summary>
    protected virtual void VisitChildren(Action<Widget> visit) { }

    /// <summary>Drives the mount lifecycle for this widget and its subtree. Called by the app on
    /// the root, and by containers on children they add after mount.</summary>
    public void Mount(App app)
    {
        OnMount(app);
        VisitChildren(child => child.Mount(app));
    }

    /// <summary>Drives the unmount lifecycle for this subtree (children first, then self).</summary>
    public void Unmount()
    {
        VisitChildren(static child => child.Unmount());
        OnUnmount();
    }

    // Link hit tracking is allocated only when used.

    /// <summary>Raised when a rendered link is clicked, with its <see cref="StyledText.Link"/> payload.
    /// The consumer decides what a click means (e.g. navigate to a gcroot address).</summary>
    public Action<object>? OnLinkClick { get; set; }

    /// <summary>The per-frame link-hit collector. A widget rendering styled text passes this to
    /// <see cref="TextRenderer.DrawLine"/> (after <see cref="ClearLinks"/>) so clicks map back to
    /// payloads. Lazily allocated the first time it is touched.</summary>
    protected List<LinkHit> Links => _links ??= new List<LinkHit>();

    /// <summary>Clears recorded link hits at the start of a render pass.</summary>
    protected void ClearLinks() => _links?.Clear();

    /// <summary>If a recorded link covers cell (x,y), fires <see cref="OnLinkClick"/> and returns true.</summary>
    protected bool DispatchLinkClick(int x, int y)
    {
        if (_links is null || OnLinkClick is null)
        {
            return false;
        }
        for (int i = 0; i < _links.Count; i++)
        {
            if (_links[i].Contains(x, y))
            {
                OnLinkClick(_links[i].Payload);
                return true;
            }
        }
        return false;
    }

    /// <summary>The link payload currently under the cursor, or null. Widgets restyle the
    /// hovered link on render; consumers can also read it or subscribe to <see cref="OnLinkHover"/>.</summary>
    protected object? HoveredLink => _hoveredLink;

    /// <summary>Raised when the hovered link changes — the new payload, or null when leaving one.</summary>
    public Action<object?>? OnLinkHover { get; set; }

    /// <summary>Updates the hovered link for cursor (x,y). Returns true if it changed (so the
    /// caller marks the frame dirty). Fires <see cref="OnLinkHover"/> on enter/leave.</summary>
    protected bool UpdateHover(int x, int y)
    {
        object? found = null;
        if (_links is not null)
        {
            for (int i = 0; i < _links.Count; i++)
            {
                if (_links[i].Contains(x, y))
                {
                    found = _links[i].Payload;
                    break;
                }
            }
        }

        if (!Equals(found, _hoveredLink))
        {
            _hoveredLink = found;
            OnLinkHover?.Invoke(found);
            return true;
        }
        return false;
    }

    /// <summary>Default background painted behind a hovered link that doesn't specify its own
    /// hover style. Null uses the theme's striped-row tone.</summary>
    public Color? LinkHoverBackground { get; set; }

    /// <summary>Re-paints the hovered link's cells. A link that specified its own hover style
    /// (via <see cref="StyledText.Link"/>) uses that; otherwise a highlight background + bold.
    /// Call at the end of a render pass, after link rects are recorded.</summary>
    protected void DrawHoverEmphasis(Surface surface)
    {
        if (_links is null || _hoveredLink is null)
        {
            return;
        }
        var defaultBg = LinkHoverBackground ?? Theming.Theme.Current.StripeBackground;
        for (int i = 0; i < _links.Count; i++)
        {
            var hit = _links[i];
            if (!Equals(hit.Payload, _hoveredLink))
            {
                continue;
            }
            for (int x = hit.Start; x < hit.End; x++)
            {
                var cell = surface.Get(x, hit.Y);
                var s = cell.Style;
                // A link may fully control its hover look; else default to highlight bg + bold so
                // it stands out even on a striped row whose background already matches the tone.
                var styled = hit.HoverStyle is { } hs
                    ? hs
                    : new Style(s.Foreground, defaultBg, s.Attributes | TextAttributes.Bold);
                surface.Set(x, hit.Y, cell.WithStyle(styled));
            }
        }
    }
}
