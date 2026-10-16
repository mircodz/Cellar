using System;
using System.Text;
using Cellar.Primitives;

namespace Cellar.Rendering;

/// <summary>
/// A mutable grid of <see cref="Cell"/>s widgets draw into (the "back buffer" the
/// <see cref="Screen"/> diffs). Writes are clipped to the bounds and an optional clip rect;
/// wide glyphs occupy two columns via a continuation cell.
/// </summary>
public sealed class Surface
{
    private Cell[] _cells;
    private Rect _clip;

    public int Width { get; private set; }
    public int Height { get; private set; }

    public Surface(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _cells = new Cell[Width * Height];
        _clip = Bounds;
        Clear(Style.Default);
    }

    public Rect Bounds => new(0, 0, Width, Height);

    /// <summary>The backing cell array in row-major order. For the compositor's fast copy.</summary>
    internal Cell[] Cells => _cells;

    /// <summary>The active clip rectangle; writes outside it are discarded.</summary>
    public Rect Clip => _clip;

    /// <summary>Resizes the surface, discarding contents and resetting the clip.</summary>
    public void Resize(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        int len = Width * Height;
        if (_cells.Length != len)
        {
            _cells = new Cell[len];
        }

        _clip = Bounds;
        Clear(Style.Default);
    }

    /// <summary>Restricts drawing to the intersection of the current clip and <paramref name="rect"/>.</summary>
    public void SetClip(Rect rect) => _clip = rect.Intersect(Bounds);

    public void ResetClip() => _clip = Bounds;

    /// <summary>Fills the entire surface with blank cells of the given style.</summary>
    public void Clear(Style style)
    {
        var blank = Cell.Blank(style);
        Array.Fill(_cells, blank);
    }

    /// <summary>Reads the cell at (x,y). Out-of-bounds reads return a blank cell.</summary>
    public Cell Get(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return Cell.Blank();
        }

        return _cells[y * Width + x];
    }

    /// <summary>Writes a cell at (x,y), honoring the clip. Wide cells write a continuation and
    /// are dropped if the trailing column overflows the clip, so a half-glyph never renders.</summary>
    public void Set(int x, int y, Cell cell)
    {
        if (!_clip.Contains(x, y))
        {
            return;
        }

        if (cell.IsWide)
        {
            if (!_clip.Contains(x + 1, y))
            {
                WriteRaw(x, y, Cell.Blank(cell.Style));
                return;
            }
            ClearOrphanedWideLeft(x, y);
            WriteRaw(x, y, cell);
            WriteRaw(x + 1, y, Cell.Continuation(cell.Style));
        }
        else if (cell.IsContinuation)
        {
            WriteRaw(x, y, cell);
        }
        else
        {
            var existing = Get(x, y);
            WriteRaw(x, y, cell);
            if (existing.IsWide)
            {
                WriteRaw(x + 1, y, Cell.Blank(cell.Style));
            }

            ClearOrphanedWideLeft(x, y);
        }
    }

    /// <summary>Draws a string at (x,y), advancing by each grapheme's width. Returns the ending x.</summary>
    public int DrawText(int x, int y, string text, Style style)
        => DrawText(x, y, text.AsSpan(), style);

    /// <summary>Span overload of <see cref="DrawText(int,int,string,Style)"/>. Lets callers
    /// draw a slice of a larger string without allocating a substring. Control characters are drawn as visible
    /// stand-ins (<see cref="Unicode.Printable"/>), so text can't move the cursor or start an escape sequence.</summary>
    public int DrawText(int x, int y, ReadOnlySpan<char> text, Style style)
    {
        int cx = x;
        int i = 0;
        while (i < text.Length)
        {
            // One grapheme cluster: a base scalar and the zero-width marks after it.
            Rune.DecodeFromUtf16(text[i..], out Rune rune, out int length); // U+FFFD for an unpaired surrogate
            int end = i + length;
            while (end < text.Length)
            {
                Rune.DecodeFromUtf16(text[end..], out Rune next, out int nextLength);
                if (Unicode.RuneWidth(next) != 0)
                {
                    break;
                }
                end += nextLength;
            }

            // Marks with no base before them take no cells, as StringWidth measures them.
            if (Unicode.RuneWidth(rune) > 0)
            {
                // Only a cluster of several scalars needs a string.
                Cell cell = end == i + length
                    ? Cell.FromRune(rune, style)
                    : Cell.FromGrapheme(new string(text[i..end]), style);
                Set(cx, y, cell);
                cx += cell.Width;
            }
            i = end;
        }
        return cx;
    }

    /// <summary>Fills a rectangular region (clipped) with a blank cell of the given style.</summary>
    public void FillRect(Rect rect, Style style)
    {
        var area = rect.Intersect(_clip);
        var blank = Cell.Blank(style);
        for (int yy = area.Top; yy < area.Bottom; yy++)
        {
            for (int xx = area.Left; xx < area.Right; xx++)
            {
                WriteRaw(xx, yy, blank);
            }
        }
    }

    /// <summary>Darkens every drawn cell in a region toward black by <paramref name="factor"/>
    /// (a modal scrim). Only truecolor cells are affected; ANSI/default are left as-is.</summary>
    public void Dim(Rect rect, double factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        double keep = 1 - factor;
        var area = rect.Intersect(_clip);
        for (int yy = area.Top; yy < area.Bottom; yy++)
        {
            for (int xx = area.Left; xx < area.Right; xx++)
            {
                var cell = _cells[yy * Width + xx];
                var s = cell.Style;
                var fg = DarkenColor(s.Foreground, keep);
                var bg = DarkenColor(s.Background, keep);
                if (fg == s.Foreground && bg == s.Background)
                {
                    continue;
                }

                var dimmed = cell.WithStyle(new Style(fg, bg, s.Attributes));
                WriteRaw(xx, yy, dimmed);
            }
        }
    }

    private static Color DarkenColor(Color c, double keep)
    {
        if (!c.IsRgb)
        {
            return c;
        }

        return Color.Rgb((byte)(c.R * keep), (byte)(c.G * keep), (byte)(c.B * keep));
    }

    private void WriteRaw(int x, int y, Cell cell)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        _cells[y * Width + x] = cell;
    }

    // If the cell to the left of (x,y) is a wide glyph, this write orphans its
    // continuation; blank the wide glyph so no half-character survives.
    private void ClearOrphanedWideLeft(int x, int y)
    {
        if (x <= 0)
        {
            return;
        }

        var left = Get(x - 1, y);
        if (left.IsWide)
        {
            WriteRaw(x - 1, y, Cell.Blank(left.Style));
        }
    }

    /// <summary>Reads the graphemes covered by <paramref name="selection"/> into a string,
    /// lines joined with '\n' and trailing whitespace trimmed per line (like a terminal).</summary>
    public string ExtractText(Selection selection)
    {
        if (selection.IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Width * Height + Height); // worst case: every cell + one '\n' per row
        int y0, y1;
        if (selection.Mode == SelectionMode.Block)
        {
            y0 = Math.Min(selection.Anchor.Y, selection.Focus.Y);
            y1 = Math.Max(selection.Anchor.Y, selection.Focus.Y);
        }
        else
        {
            var (start, end) = selection.Normalized();
            y0 = start.Y;
            y1 = end.Y;
        }

        y0 = Math.Max(0, y0);
        y1 = Math.Min(Height - 1, y1);

        for (int y = y0; y <= y1; y++)
        {
            int lineStart = sb.Length;
            for (int x = 0; x < Width; x++)
            {
                if (!selection.Contains(x, y))
                {
                    continue;
                }

                var cell = Get(x, y);
                if (cell.IsContinuation)
                {
                    continue; // trailing half of a wide glyph
                }

                if (cell.IsEmpty)
                {
                    sb.Append(' ');
                }
                else
                {
                    cell.AppendTo(sb);
                }
            }

            // Trim this line's trailing spaces in place, like a native terminal selection.
            while (sb.Length > lineStart && sb[sb.Length - 1] == ' ')
            {
                sb.Length--;
            }

            if (y < y1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>Repaints cells inside <paramref name="selection"/> with the selection colors,
    /// keeping each glyph. Called after widgets render so the highlight sits on top.</summary>
    public void HighlightSelection(Selection selection, Color foreground, Color background)
    {
        if (selection.IsEmpty)
        {
            return;
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (!selection.Contains(x, y))
                {
                    continue;
                }

                var cell = _cells[y * Width + x];
                if (cell.IsContinuation)
                {
                    _cells[y * Width + x] = Cell.Continuation(new Style(foreground, background));
                    continue;
                }

                WriteRaw(x, y, cell.WithStyle(new Style(foreground, background, cell.Style.Attributes)));
                if (cell.IsWide && x + 1 < Width)
                {
                    _cells[y * Width + x + 1] = Cell.Continuation(new Style(foreground, background));
                }
            }
        }
    }
}
