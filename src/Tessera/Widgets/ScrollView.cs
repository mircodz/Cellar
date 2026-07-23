using System;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Theming;

namespace Tessera.Widgets;

/// <summary>
/// A vertically scrolling viewport over a taller child: the child renders into an offscreen
/// buffer, then the visible slice is blitted. A themed scrollbar is drawn on overflow. Scrolls
/// with Up/Down, PageUp/PageDown, Home/End, and the wheel.
/// </summary>
public sealed class ScrollView : Widget
{
    private int _offset;          // topmost visible content row
    private int _contentHeight;   // measured on last render
    private int _viewportHeight;  // measured on last render
    private Surface? _buffer;     // reused offscreen render target

    public Widget Child { get; set; }

    /// <summary>Whether to reserve a column and draw a scrollbar when content overflows.</summary>
    public bool ShowScrollbar { get; set; } = true;

    /// <summary>The scrollbar style used when <see cref="ShowScrollbar"/> is on.</summary>
    public Scrollbar Scrollbar { get; set; } = Scrollbar.Default;

    public override bool IsFocusable => true;

    public ScrollView(Widget child) => Child = child;

    /// <summary>Current scroll position (topmost visible content row).</summary>
    public int Offset
    {
        get => _offset;
        set => _offset = Math.Max(0, value);
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        var theme = Theme.Current;

        // Measure at the full width first; only reserve the scrollbar column if the content
        // actually overflows. Reserving unconditionally squeezes content that fits and leaves
        // a dead blank column. Overflow can change with the narrower width (re-wrapping), so
        // re-measure once when we take the column.
        var full = Child.Measure(new Size(area.Width, int.MaxValue / 2));
        bool needsBar = ShowScrollbar && full.Height > area.Height;
        int barWidth = needsBar ? 1 : 0;
        var contentArea = new Rect(area.X, area.Y, Math.Max(0, area.Width - barWidth), area.Height);

        var desired = needsBar
            ? Child.Measure(new Size(contentArea.Width, int.MaxValue / 2))
            : full;
        _contentHeight = Math.Max(desired.Height, contentArea.Height);
        _viewportHeight = contentArea.Height;

        ClampOffset();

        // Render the child into a reused offscreen buffer as tall as its content, then blit the
        // visible window. This lets a child lay out its full height without knowing about scrolling.
        if (_buffer is null || _buffer.Width != contentArea.Width || _buffer.Height != _contentHeight)
        {
            _buffer = new Surface(contentArea.Width, _contentHeight);
        }
        var buffer = _buffer;
        buffer.Clear(theme.TextStyle);
        buffer.SetClip(buffer.Bounds);
        Child.Render(buffer, buffer.Bounds);

        for (int row = 0; row < contentArea.Height; row++)
        {
            int srcY = _offset + row;
            if (srcY >= _contentHeight)
            {
                break;
            }

            for (int col = 0; col < contentArea.Width; col++)
            {
                surface.Set(contentArea.X + col, contentArea.Y + row, buffer.Get(col, srcY));
            }
        }

        if (needsBar && _contentHeight > _viewportHeight)
        {
            var bar = new Rect(area.Right - 1, area.Y, 1, area.Height);
            Scrollbar.Render(surface, bar, _offset, _viewportHeight, _contentHeight);
        }
    }

    public override bool OnEvent(InputEvent e)
    {
        switch (e)
        {
            case KeyEvent key when HasFocus:
                return HandleKey(key);
            case MouseEvent { Kind: MouseEventKind.Wheel } m:
                Offset += m.Button == MouseButton.WheelUp ? -3 : 3;
                ClampOffset();
                return true;
        }
        return false;
    }

    private bool HandleKey(KeyEvent key)
    {
        int page = Math.Max(1, _viewportHeight - 1);
        switch (key.Key)
        {
            case Key.Up: Offset -= 1; ClampOffset(); return true;
            case Key.Down: Offset += 1; ClampOffset(); return true;
            case Key.PageUp: Offset -= page; ClampOffset(); return true;
            case Key.PageDown: Offset += page; ClampOffset(); return true;
            case Key.Home: Offset = 0; return true;
            case Key.End: Offset = Math.Max(0, _contentHeight - _viewportHeight); return true;
        }
        return false;
    }

    private void ClampOffset()
    {
        int maxOffset = Math.Max(0, _contentHeight - _viewportHeight);
        if (_offset > maxOffset)
        {
            _offset = maxOffset;
        }

        if (_offset < 0)
        {
            _offset = 0;
        }
    }
}
