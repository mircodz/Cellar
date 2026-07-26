using System;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Theming;
using Tessera.Widgets;

namespace Tessera.Demo;

/// <summary>A grid of color swatches with a selection cursor and a hex readout of the selection.
/// Interactive: arrow keys move the cursor, a click selects the swatch under the pointer. Shows raw
/// cell painting (FillRect) + keyboard/mouse routing against the last-rendered rect.</summary>
public sealed class ColorSwatchGrid : Widget
{
    public Color[] Colors { get; init; } = [];
    public int Columns { get; init; } = 8;
    public int Selected { get; set; }

    /// <summary>Raised when the selection changes, with the newly selected color.</summary>
    public Action<Color>? OnSelect { get; set; }

    public override bool IsFocusable => true;
    public override bool HasFocus { get; set; }

    private const int SwatchW = 4, SwatchH = 2;
    private Rect _lastArea; // for mouse hit-testing (absolute coords)

    public override void Render(Surface surface, Rect area)
    {
        _lastArea = area;
        for (int i = 0; i < Colors.Length; i++)
        {
            int col = i % Columns, row = i / Columns;
            var rect = new Rect(area.X + col * SwatchW, area.Y + row * SwatchH, SwatchW, SwatchH);
            surface.FillRect(rect, new Style(Color.Default, Colors[i]));
            if (i == Selected)
            {
                // Mark the selected swatch; brighten the cursor when focused so it reads clearly.
                var mark = HasFocus ? Color.Rgb(255, 255, 255) : Color.Rgb(200, 200, 200);
                surface.DrawText(rect.X, rect.Y, "[", new Style(mark, Colors[i]));
                surface.DrawText(rect.Right - 1, rect.Y, "]", new Style(mark, Colors[i]));
            }
        }
        // Hex readout below the grid.
        int rows = (Colors.Length + Columns - 1) / Columns;
        var sel = Colors[Math.Clamp(Selected, 0, Colors.Length - 1)];
        string hex = sel.IsRgb ? $"#{sel.R:x2}{sel.G:x2}{sel.B:x2}" : "ansi";
        surface.DrawText(area.X, area.Y + rows * SwatchH + 1,
            $"selected  {hex}", new Style(Theme.Current.Foreground, Color.Default));
    }

    public override bool OnEvent(InputEvent e)
    {
        if (Colors.Length == 0)
        {
            return false;
        }

        switch (e)
        {
            case KeyEvent key when HasFocus:
                int next = key.Key switch
                {
                    Key.Left => Selected - 1,
                    Key.Right => Selected + 1,
                    Key.Up => Selected - Columns,
                    Key.Down => Selected + Columns,
                    _ => Selected,
                };
                return TrySelect(next);

            case MouseEvent { Kind: MouseEventKind.Down, Button: MouseButton.Left } m
                when _lastArea.Contains(m.X, m.Y):
                int col = (m.X - _lastArea.X) / SwatchW;
                int row = (m.Y - _lastArea.Y) / SwatchH;
                if (col >= 0 && col < Columns)
                {
                    return TrySelect(row * Columns + col);
                }
                return false;
        }
        return false;
    }

    private bool TrySelect(int index)
    {
        if (index < 0 || index >= Colors.Length)
        {
            return false; // out of range (e.g. Left at column 0) — let it fall through
        }
        if (index == Selected)
        {
            return true; // in-range no-op: consume so focus stays put
        }
        Selected = index;
        OnSelect?.Invoke(Colors[index]);
        Repaint.Request(); // self-request a coalesced repaint so the cursor moves without a poll
        return true;
    }
}