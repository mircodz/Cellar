using System;
using System.Collections.Generic;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Text;
using Tessera.Theming;

namespace Tessera.Widgets.Charts;

/// <summary>
/// A flame graph (rendered as a top-down icicle) for hierarchical cost such as allocation
/// stacks, CPU samples, or retained sizes. Each frame's width is its weight relative to the
/// zoomed root. Click (or Enter) to zoom in, Backspace/Escape to zoom out, arrows to move selection.
/// </summary>
public sealed class FlameGraph<T> : Widget
{
    private FlameNode<T>? _root;
    private FlameNode<T>? _zoom;
    private FlameNode<T>? _selected;

    // Per-render layout record for hit-testing: each drawn frame's screen rect + node.
    private readonly List<(Rect rect, FlameNode<T> node)> _hits = new();

    // Warm flame-ish palette (reds/oranges/yellows) with a couple of cooler accents.
    private static readonly Color[] Palette =
    {
        Color.Hex("#d9534f"), Color.Hex("#e8724f"), Color.Hex("#f0904f"),
        Color.Hex("#f4bf75"), Color.Hex("#e6c34f"), Color.Hex("#d98f4f"),
        Color.Hex("#c9534f"), Color.Hex("#e07b53"),
    };

    /// <summary>Maps a frame to its bar color. Null uses a hash-of-label palette rotation.</summary>
    public Func<FlameNode<T>, Color>? ColorSelector { get; set; }

    /// <summary>Formats the text shown inside a frame (when it is wide enough). Default: the label.</summary>
    public Func<FlameNode<T>, string>? LabelSelector { get; set; }

    /// <summary>Raised when the selected frame changes.</summary>
    public Action<FlameNode<T>>? OnSelect { get; set; }

    /// <summary>Raised when a frame is zoomed into.</summary>
    public Action<FlameNode<T>>? OnZoom { get; set; }

    public override bool IsFocusable => true;

    public FlameNode<T>? Root
    {
        get => _root;
        set { _root = value; _zoom = value; _selected = value; }
    }

    /// <summary>The frame currently zoomed to (the full-width root of the view).</summary>
    public FlameNode<T>? ZoomedFrame => _zoom;

    /// <summary>The currently selected/highlighted frame.</summary>
    public FlameNode<T>? SelectedFrame => _selected;

    /// <summary>Zooms so <paramref name="frame"/> fills the width; its subtree fills the graph.</summary>
    public void ZoomTo(FlameNode<T> frame)
    {
        _zoom = frame;
        _selected = frame;
        OnZoom?.Invoke(frame);
    }

    /// <summary>Zooms back out one level (to the zoomed frame's parent), stopping at the root.</summary>
    public void ZoomOut()
    {
        if (_zoom?.Parent is { } p)
        {
            _zoom = p;
            _selected = p;
            OnZoom?.Invoke(p);
        }
    }

    /// <summary>Resets the zoom to the true root.</summary>
    public void ResetZoom()
    {
        _zoom = _root;
        _selected = _root;
        if (_root is not null) OnZoom?.Invoke(_root);
    }

    public override void Render(Surface surface, Rect area)
    {
        _hits.Clear();
        if (area.IsEmpty || _zoom is null) return;

        var theme = Theme.Current;
        double rootWeight = _zoom.Weight;
        if (rootWeight <= 0) return;

        DrawFrame(surface, _zoom, area.X, area.Y, area.Width, area, theme, depth: 0);
    }

    // Recursively draws a frame across [x, x+width) on row (area.Y + depth), then lays its
    // children out proportionally on the row below.
    private void DrawFrame(Surface surface, FlameNode<T> node, int x, int y, int width, Rect area, Theme theme, int depth)
    {
        if (width <= 0 || y >= area.Bottom) return;

        var color = ColorSelector?.Invoke(node) ?? PaletteFor(node.Label);
        bool isSelected = ReferenceEqualities(node, _selected);
        var fillBg = isSelected ? theme.Accent : color;
        var fg = Contrast(fillBg);

        var rect = new Rect(x, y, width, 1);
        surface.FillRect(rect, new Style(fg, fillBg));
        _hits.Add((rect, node));

        // Frame label, if it fits (leave a 1-cell inset). Skip it for the many sub-3-cell frames
        // in a deep graph, which avoids the string work for labels that couldn't show anyway.
        if (width >= 3)
        {
            string label = LabelSelector?.Invoke(node) ?? node.Label;
            if (!string.IsNullOrEmpty(label))
            {
                surface.SetClip(rect);
                TextRenderer.DrawLine(surface, x + 1, y, width - 1,
                    new StyledText(label, new Style(fg, fillBg)), Justify.Left);
                surface.ResetClip();
            }
        }

        // Lay out children on the next row, widths proportional to weight. Children may sum to
        // less than the parent (self-cost / unaccounted); we pack them from the left.
        int childY = y + 1;
        if (childY >= area.Bottom || node.Children.Count == 0) return;

        double parentWeight = node.Weight;
        int childX = x;
        int remainingWidth = width;
        double remainingWeight = parentWeight;
        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (remainingWeight <= 0) break;
            int cw = (int)Math.Round(child.Weight / parentWeight * width);
            cw = Math.Min(cw, remainingWidth);
            if (cw <= 0) { continue; }

            DrawFrame(surface, child, childX, childY, cw, area, theme, depth + 1);
            childX += cw;
            remainingWidth -= cw;
            remainingWeight -= child.Weight;
        }
    }

    public override bool OnEvent(InputEvent e)
    {
        switch (e)
        {
            case MouseEvent { Kind: MouseEventKind.Down } m:
                return HandleClick(m);
            case KeyEvent key when HasFocus:
                return HandleKey(key);
        }
        return false;
    }

    private bool HandleClick(MouseEvent m)
    {
        for (int i = 0; i < _hits.Count; i++)
        {
            var (rect, node) = _hits[i];
            if (rect.Contains(m.X, m.Y))
            {
                if (ReferenceEqualities(node, _selected)) ZoomTo(node);
                else Select(node);
                return true;
            }
        }
        return false;
    }

    private bool HandleKey(KeyEvent key)
    {
        switch (key.Key)
        {
            case Key.Enter:
                if (_selected is not null) ZoomTo(_selected);
                return true;
            case Key.Backspace:
            case Key.Escape:
                ZoomOut();
                return true;
            case Key.Up:
                if (_selected?.Parent is { } up && !ReferenceEqualities(_selected, _zoom)) Select(up);
                return true;
            case Key.Down:
                if (_selected is { Children.Count: > 0 } d) Select(d.Children[0]);
                return true;
            case Key.Left:
                MoveSibling(-1);
                return true;
            case Key.Right:
                MoveSibling(1);
                return true;
        }
        return false;
    }

    private void MoveSibling(int delta)
    {
        var node = _selected;
        if (node?.Parent is not { } parent) return;
        int idx = parent.Children.IndexOf(node);
        int next = idx + delta;
        if (next >= 0 && next < parent.Children.Count) Select(parent.Children[next]);
    }

    private void Select(FlameNode<T> node)
    {
        if (!ReferenceEqualities(node, _selected))
        {
            _selected = node;
            OnSelect?.Invoke(node);
        }
    }

    private static bool ReferenceEqualities(FlameNode<T>? a, FlameNode<T>? b) => ReferenceEquals(a, b);

    // A stable color per label, so the same frame keeps its color across zooms.
    private static Color PaletteFor(string label)
    {
        int h = 0;
        for (int i = 0; i < label.Length; i++) h = h * 31 + label[i];
        return Palette[(h & 0x7fffffff) % Palette.Length];
    }

    // Picks black or white text for legibility over a background color.
    private static Color Contrast(Color bg)
    {
        if (!bg.IsRgb) return Color.White;
        int luma = (bg.R * 299 + bg.G * 587 + bg.B * 114) / 1000;
        return luma > 140 ? Color.Rgb(20, 20, 20) : Color.Rgb(240, 240, 240);
    }
}
