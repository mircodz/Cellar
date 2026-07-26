using System;
using System.Globalization;
using System.Text;
using Tessera.Primitives;

namespace Tessera.Rendering;

/// <summary>
/// Renders a <see cref="Surface"/> to a standalone SVG string, for docs and README images.
/// Each cell becomes a background rect (equal-background runs are coalesced) plus its glyph in a
/// monospace font. Colors resolve to <c>#rrggbb</c>: truecolor directly, the 16 ANSI colors via
/// a standard xterm palette, and default fg/bg via <see cref="DefaultForeground"/>/<see cref="DefaultBackground"/>.
/// </summary>
public sealed class SvgRenderer
{
    // Standard xterm 16-color palette, for resolving ANSI colors to concrete RGB.
    private static readonly Color[] Ansi16 =
    {
        Color.Rgb(0, 0, 0),       Color.Rgb(205, 0, 0),     Color.Rgb(0, 205, 0),     Color.Rgb(205, 205, 0),
        Color.Rgb(0, 0, 238),     Color.Rgb(205, 0, 205),   Color.Rgb(0, 205, 205),   Color.Rgb(229, 229, 229),
        Color.Rgb(127, 127, 127), Color.Rgb(255, 0, 0),     Color.Rgb(0, 255, 0),     Color.Rgb(255, 255, 0),
        Color.Rgb(92, 92, 255),   Color.Rgb(255, 0, 255),   Color.Rgb(0, 255, 255),   Color.Rgb(255, 255, 255),
    };

    /// <summary>Cell width in pixels.</summary>
    public int CellWidth { get; set; } = 9;

    /// <summary>Cell height in pixels.</summary>
    public int CellHeight { get; set; } = 18;

    /// <summary>Font size in pixels (defaults to a bit under the cell height).</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>Monospace font family used for glyphs.</summary>
    public string FontFamily { get; set; } = "ui-monospace, 'Cascadia Mono', 'JetBrains Mono', Menlo, Consolas, monospace";

    /// <summary>Concrete color substituted for a default (inherit) background.</summary>
    public Color DefaultBackground { get; set; } = Color.Rgb(21, 21, 21);

    /// <summary>Concrete color substituted for a default foreground.</summary>
    public Color DefaultForeground { get; set; } = Color.Rgb(208, 208, 208);

    /// <summary>Blank pixels of margin around the cell grid (filled with the background color).</summary>
    public int Padding { get; set; } = 12;

    /// <summary>Renders <paramref name="surface"/> to an SVG document string.</summary>
    public string Render(Surface surface)
    {
        int w = surface.Width, h = surface.Height;
        int pad = Padding;
        int pxW = w * CellWidth + pad * 2, pxH = h * CellHeight + pad * 2;

        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(pxW)
          .Append("\" height=\"").Append(pxH)
          .Append("\" viewBox=\"0 0 ").Append(pxW).Append(' ').Append(pxH).Append("\">\n");

        sb.Append("<rect width=\"").Append(pxW).Append("\" height=\"").Append(pxH)
          .Append("\" fill=\"").Append(Hex(DefaultBackground)).Append("\"/>\n");

        sb.Append("<g transform=\"translate(").Append(pad).Append(',').Append(pad).Append(")\">\n");

        // Per-row background rects: coalesce horizontal runs of the same background color.
        for (int y = 0; y < h; y++)
        {
            int x = 0;
            while (x < w)
            {
                var bg = ResolveBg(surface.Get(x, y).Style.Background);
                int start = x;
                while (x < w && ColorEq(ResolveBg(surface.Get(x, y).Style.Background), bg))
                {
                    x++;
                }
                if (!ColorEq(bg, DefaultBackground))
                {
                    AppendRect(sb, start, y, x - start, bg);
                }
            }
        }

        // Block-element glyphs (bars, fills, thumbs) render as exact <rect>s in the cell's
        // foreground color rather than text, so they tile with no font baseline gaps.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var cell = surface.Get(x, y);
                if (cell.Grapheme is { Length: 1 } g && TryBlockRect(g[0], out double fx, out double fy, out double fw, out double fh))
                {
                    var fg = ResolveFg(cell.Style.Foreground);
                    sb.Append("<rect x=\"").Append(FmtPx(x * CellWidth + fx * CellWidth))
                      .Append("\" y=\"").Append(FmtPx(y * CellHeight + fy * CellHeight))
                      .Append("\" width=\"").Append(FmtPx(fw * CellWidth))
                      .Append("\" height=\"").Append(FmtPx(fh * CellHeight))
                      .Append("\" fill=\"").Append(Hex(fg)).Append("\"/>\n");
                }
            }
        }

        // Glyphs: one <text> per non-blank cell, pinned to its own column and forced to the cell
        // width. Done per-cell so glyphs don't accumulate sub-pixel drift across a row. Block
        // cells were already drawn as rects above.
        sb.Append("<g font-family=\"").Append(Escape(FontFamily)).Append("\" font-size=\"")
          .Append(FontSize.ToString(CultureInfo.InvariantCulture)).Append("\" text-anchor=\"middle\">\n");
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var cell = surface.Get(x, y);
                if (cell.IsContinuation || cell.IsEmpty || cell.Grapheme is " " or "")
                {
                    continue;
                }
                if (cell.Grapheme is { Length: 1 } bg && TryBlockRect(bg[0], out _, out _, out _, out _))
                {
                    continue; // drawn as a rect
                }
                AppendGlyph(sb, x, y, cell.Grapheme, cell.Style, cell.Width == 2 ? 2 : 1);
            }
        }
        sb.Append("</g>\n</g>\n</svg>\n");
        return sb.ToString();
    }

    private void AppendRect(StringBuilder sb, int col, int row, int cells, Color color)
    {
        sb.Append("<rect x=\"").Append(col * CellWidth)
          .Append("\" y=\"").Append(row * CellHeight)
          .Append("\" width=\"").Append(cells * CellWidth)
          .Append("\" height=\"").Append(CellHeight)
          .Append("\" fill=\"").Append(Hex(color)).Append("\"/>\n");
    }

    // Draws one glyph centered (text-anchor=middle) at the column's mid-point and forced to the
    // cell width, so it lands on the grid without drift.
    private void AppendGlyph(StringBuilder sb, int col, int row, string glyph, Style style, int cells)
    {
        var fg = ResolveFg(style.Foreground);
        int span = cells * CellWidth;
        double cx = col * CellWidth + span / 2.0;
        double ty = row * CellHeight + FontSize;
        sb.Append("<text x=\"").Append(FmtPx(cx))
          .Append("\" y=\"").Append(FmtPx(ty))
          .Append("\" textLength=\"").Append(span)
          .Append("\" fill=\"").Append(Hex(fg)).Append('"');

        var a = style.Attributes;
        if ((a & TextAttributes.Bold) != 0) sb.Append(" font-weight=\"bold\"");
        if ((a & TextAttributes.Italic) != 0) sb.Append(" font-style=\"italic\"");
        if ((a & (TextAttributes.Underline | TextAttributes.Strikethrough)) != 0)
        {
            sb.Append(" text-decoration=\"");
            if ((a & TextAttributes.Underline) != 0) sb.Append("underline ");
            if ((a & TextAttributes.Strikethrough) != 0) sb.Append("line-through");
            sb.Append('"');
        }
        sb.Append('>').Append(Escape(glyph)).Append("</text>\n");
    }

    private Color ResolveBg(Color c) => c.IsDefault ? DefaultBackground : Resolve(c);
    private Color ResolveFg(Color c) => c.IsDefault ? DefaultForeground : Resolve(c);

    private static string FmtPx(double v) =>
        v == Math.Floor(v) ? ((int)v).ToString(CultureInfo.InvariantCulture)
                           : v.ToString("0.##", CultureInfo.InvariantCulture);

    // Maps a block-element glyph to a filled sub-cell rectangle in cell-fraction coords
    // (x,y = top-left; w,h = size; all in 0..1). Covers the glyphs Tessera actually emits:
    // full block, lower eighths (sparkline/bar bottoms), and left eighths (progress/edges).
    private static bool TryBlockRect(char c, out double x, out double y, out double w, out double h)
    {
        x = 0; y = 0; w = 1; h = 1;
        switch (c)
        {
            case '█': return true;                                   // full
            case '▁': y = 7.0 / 8; h = 1.0 / 8; return true;         // lower 1/8..8/8
            case '▂': y = 6.0 / 8; h = 2.0 / 8; return true;
            case '▃': y = 5.0 / 8; h = 3.0 / 8; return true;
            case '▄': y = 4.0 / 8; h = 4.0 / 8; return true;
            case '▅': y = 3.0 / 8; h = 5.0 / 8; return true;
            case '▆': y = 2.0 / 8; h = 6.0 / 8; return true;
            case '▇': y = 1.0 / 8; h = 7.0 / 8; return true;
            case '▔': h = 1.0 / 8; return true;                      // upper 1/8
            case '▏': w = 1.0 / 8; return true;                      // left 1/8..7/8
            case '▎': w = 2.0 / 8; return true;
            case '▍': w = 3.0 / 8; return true;
            case '▌': w = 4.0 / 8; return true;
            case '▋': w = 5.0 / 8; return true;
            case '▊': w = 6.0 / 8; return true;
            case '▉': w = 7.0 / 8; return true;
            case '▐': x = 4.0 / 8; w = 4.0 / 8; return true;         // right half
            default: return false;
        }
    }

    private static Color Resolve(Color c) =>
        c.IsAnsi ? Ansi16[c.AnsiIndex & 15] : c;

    private static bool ColorEq(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

    private static string Hex(Color c)
    {
        var r = c.IsAnsi ? Ansi16[c.AnsiIndex & 15] : c;
        return $"#{r.R:x2}{r.G:x2}{r.B:x2}";
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
