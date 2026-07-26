using System;
using System.Text;
using Tessera.Primitives;

namespace Tessera.Rendering;

/// <summary>
/// Holds a front buffer (on screen) and a back buffer (next frame). <see cref="BuildDiff"/>
/// emits the minimal ANSI to turn front into back: it moves the cursor only on run breaks,
/// re-emits style only on change, and skips unchanged cells, then promotes back to front.
/// </summary>
public sealed class Screen
{
    private Surface _back;
    private Cell[] _front;
    private readonly AnsiStyleWriter _styleWriter;
    private readonly StringBuilder _diffBuffer = new(4096);
    private bool _frontValid;

    public Screen(int width, int height, ColorDepth depth)
    {
        _back = new Surface(width, height);
        _front = new Cell[width * height];
        _styleWriter = new AnsiStyleWriter(depth);
        _frontValid = false;
    }

    /// <summary>The back buffer widgets draw into for the next frame.</summary>
    public Surface Back => _back;

    public int Width => _back.Width;
    public int Height => _back.Height;

    /// <summary>Resizes both buffers and invalidates the front so the next diff repaints fully.</summary>
    public void Resize(int width, int height)
    {
        _back.Resize(width, height);
        _front = new Cell[width * height];
        _frontValid = false;
    }

    /// <summary>Forces the next <see cref="ComputeDiff"/> to repaint every cell.</summary>
    public void Invalidate() => _frontValid = false;

    /// <summary>A concrete color substituted at emit time for cells with a default background;
    /// null keeps backgrounds terminal-transparent.</summary>
    public Color? DefaultBackground
    {
        get => _styleWriter.DefaultBackground;
        set
        {
            if (_styleWriter.DefaultBackground != value)
            {
                _styleWriter.DefaultBackground = value;
                _frontValid = false; // repaint fully so existing cells pick up the new bg
            }
        }
    }

    /// <summary>Returns the front-to-back ANSI patch as an allocated string.</summary>
    public string ComputeDiff() => BuildDiff().ToString();

    /// <summary>
    /// Builds the front→back patch into a reused <see cref="StringBuilder"/> and promotes
    /// back to front. Returns the builder (empty when nothing changed) so callers can write
    /// it without allocating a string. Valid until the next call.
    /// </summary>
    public StringBuilder BuildDiff()
    {
        var sb = _diffBuffer;
        sb.Clear();
        int w = _back.Width, h = _back.Height;

        bool cursorKnown = false;
        int cursorX = -1, cursorY = -1;
        bool styleKnown = false;
        Style lastStyle = Style.Default;
        bool anyChanged = false;

        for (int y = 0; y < h; y++)
        {
            int x = 0;
            while (x < w)
            {
                var cell = _back.Get(x, y);

                if (cell.IsContinuation)
                {
                    x++;
                    continue;
                }

                bool changed = !_frontValid || !FrontCell(x, y, w).Equals(cell);
                if (!changed)
                {
                    x++;
                    continue;
                }

                anyChanged = true;

                if (!cursorKnown || cursorX != x || cursorY != y)
                {
                    sb.Append("\x1b[").Append(y + 1).Append(';').Append(x + 1).Append('H');
                    cursorX = x;
                    cursorY = y;
                    cursorKnown = true;
                }

                if (!styleKnown || lastStyle != cell.Style)
                {
                    _styleWriter.WriteTransition(sb, cell.Style);
                    lastStyle = cell.Style;
                    styleKnown = true;
                }

                if (cell.IsEmpty)
                {
                    sb.Append(' ');
                }
                else
                {
                    cell.AppendTo(sb);
                }

                int advance = cell.Width == 0 ? 1 : cell.Width;
                cursorX += advance;
                x += advance;
            }
        }

        if (sb.Length > 0)
        {
            sb.Append(Terminal.Ansi.Reset); // leave the terminal in a clean state
        }

        // Nothing changed and the front is already a valid mirror of the back: the copy would
        // be a no-op memcpy of the whole grid, so skip it. The one case we must still promote
        // is a forced full repaint that emitted nothing (e.g. an all-blank frame after
        // Invalidate) — there _frontValid is false and anyChanged stays false, so promote.
        if (anyChanged || !_frontValid)
        {
            PromoteBackToFront();
        }

        return sb;
    }

    private Cell FrontCell(int x, int y, int w)
    {
        if (!_frontValid)
        {
            return Cell.Blank();
        }

        return _front[y * w + x];
    }

    private void PromoteBackToFront()
    {
        int len = _back.Width * _back.Height;
        if (_front.Length != len)
        {
            _front = new Cell[len];
        }

        Array.Copy(_back.Cells, _front, len);
        _frontValid = true;
    }
}
