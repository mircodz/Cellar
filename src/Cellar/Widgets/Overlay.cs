using System;
using Cellar.Primitives;
using Cellar.Rendering;

namespace Cellar.Widgets;

/// <summary>Where an overlay is positioned within the screen.</summary>
public enum OverlayPlacement
{
    /// <summary>Centered horizontally and vertically.</summary>
    Center,
    /// <summary>Pinned to the top, centered horizontally (e.g. a command palette).</summary>
    Top,
    /// <summary>Pinned to the bottom, centered horizontally.</summary>
    Bottom,
    /// <summary>An explicit rectangle supplied via <see cref="Overlay.Bounds"/>.</summary>
    Manual,
}

/// <summary>
/// A floating layer above the root content — the base for command palettes, dialogs,
/// dropdowns, tooltips, and toasts. Carries a content widget, placement + size, an optional
/// scrim, and a modality flag. A modal swallows all input; a non-modal lets unhandled input
/// fall through. Escape dismisses when <see cref="DismissOnEscape"/>.
/// </summary>
public sealed class Overlay
{
    private Rect _lastRect;

    public Widget Content { get; set; }

    /// <summary>Whether this overlay captures all input (true) or passes unhandled through.</summary>
    public bool Modal { get; set; } = true;

    /// <summary>Escape key dismisses the overlay when true.</summary>
    public bool DismissOnEscape { get; set; } = true;

    /// <summary>Placement strategy within the screen.</summary>
    public OverlayPlacement Placement { get; set; } = OverlayPlacement.Center;

    /// <summary>Preferred width in cells (or a fraction of the screen if <see cref="WidthPercent"/> is set).</summary>
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>If &gt; 0, width is this percentage of the screen (overrides <see cref="Width"/>).</summary>
    public int WidthPercent { get; set; }
    public int HeightPercent { get; set; }

    /// <summary>Explicit bounds for <see cref="OverlayPlacement.Manual"/>.</summary>
    public Rect Bounds { get; set; }

    /// <summary>Margin from the screen edge for Top/Bottom placement.</summary>
    public int Margin { get; set; } = 2;

    /// <summary>Scrim opacity over the content beneath (0 = none, 1 = black). Modal default dims.</summary>
    public double ScrimOpacity { get; set; } = 0.5;

    /// <summary>Invoked when the overlay is dismissed (by Escape or <see cref="App.PopOverlay"/>).</summary>
    public Action? OnDismiss { get; set; }

    public Overlay(Widget content)
    {
        Content = content;
    }

    /// <summary>Computes this overlay's rectangle within a screen of the given size.</summary>
    public Rect Resolve(Size screen)
    {
        if (Placement == OverlayPlacement.Manual)
        {
            return Bounds.Intersect(new Rect(0, 0, screen.Width, screen.Height));
        }

        int w = WidthPercent > 0 ? screen.Width * WidthPercent / 100 : (Width > 0 ? Width : screen.Width / 2);
        int h = HeightPercent > 0 ? screen.Height * HeightPercent / 100 : (Height > 0 ? Height : screen.Height / 2);
        w = Math.Clamp(w, 1, screen.Width);
        h = Math.Clamp(h, 1, screen.Height);

        int x = (screen.Width - w) / 2;
        int y = Placement switch
        {
            OverlayPlacement.Top => Math.Min(Margin, Math.Max(0, screen.Height - h)),
            OverlayPlacement.Bottom => Math.Max(0, screen.Height - h - Margin),
            _ => (screen.Height - h) / 2,
        };
        return new Rect(x, y, w, h);
    }

    /// <summary>Renders the scrim (if any), then the content clipped to its resolved rect.</summary>
    public void Render(Surface surface, Size screen)
    {
        if (Modal && ScrimOpacity > 0)
        {
            surface.Dim(new Rect(0, 0, screen.Width, screen.Height), ScrimOpacity);
        }

        var rect = Resolve(screen);
        _lastRect = rect;
        if (rect.IsEmpty)
        {
            return;
        }

        surface.SetClip(rect);
        Content.Render(surface, rect);
        surface.ResetClip();
    }

    /// <summary>The rectangle the overlay occupied on its last render (for hit-testing).</summary>
    public Rect LastRect => _lastRect;
}
