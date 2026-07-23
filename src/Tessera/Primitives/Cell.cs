using System;
using System.Text;

namespace Tessera.Primitives;

/// <summary>
/// One terminal cell: a grapheme plus its style. <see cref="Width"/> is 0/1/2; the trailing
/// half of a wide glyph is a width-0 <see cref="Continuation"/> so the grid stays aligned.
/// The grapheme is stored inline (1-2 UTF-16 chars) so drawing doesn't allocate; longer
/// clusters fall back to a string. <see cref="AppendTo"/> avoids materializing that string.
/// </summary>
public readonly struct Cell : IEquatable<Cell>
{
    // _c0/_c1 hold 1-2 UTF-16 code units; _text holds longer clusters. Blank => _c0=' '.
    // Continuation/empty => _c0='\0'. When _text is set the inline chars are unused.
    private readonly char _c0;
    private readonly char _c1;
    private readonly string? _text;

    public Style Style { get; }
    public byte Width { get; }

    private Cell(char c0, char c1, string? text, Style style, byte width)
    {
        _c0 = c0;
        _c1 = c1;
        _text = text;
        Style = style;
        Width = width;
    }

    /// <summary>A blank cell: a space with the given style (default style if omitted).</summary>
    public static Cell Blank(Style style) => new(' ', '\0', null, style, 1);
    public static Cell Blank() => Blank(Style.Default);

    /// <summary>The right half of a wide glyph: no glyph, zero width.</summary>
    public static Cell Continuation(Style style) => new('\0', '\0', null, style, 0);

    /// <summary>Builds a cell from a grapheme cluster, computing its display width.</summary>
    public static Cell FromGrapheme(string grapheme, Style style)
    {
        if (string.IsNullOrEmpty(grapheme))
        {
            return Blank(style);
        }

        int w = Unicode.GraphemeWidth(grapheme);
        if (w <= 0)
        {
            return Blank(style);
        }

        byte width = (byte)Math.Min(w, 2);
        if (grapheme.Length == 1)
        {
            return new Cell(grapheme[0], '\0', null, style, width);
        }
        if (grapheme.Length == 2 && char.IsHighSurrogate(grapheme[0]) && char.IsLowSurrogate(grapheme[1]))
        {
            return new Cell(grapheme[0], grapheme[1], null, style, width);
        }
        return new Cell('\0', '\0', grapheme, style, width);
    }

    public static Cell FromRune(Rune rune, Style style)
    {
        int w = Unicode.RuneWidth(rune);
        if (w <= 0)
        {
            return Blank(style);
        }

        byte width = (byte)Math.Min(w, 2);
        Span<char> chars = stackalloc char[2];
        int n = rune.EncodeToUtf16(chars);
        return n == 1
            ? new Cell(chars[0], '\0', null, style, width)
            : new Cell(chars[0], chars[1], null, style, width);
    }

    /// <summary>Fast path for a single BMP character (no surrogate). Width is 1 or 2.</summary>
    public static Cell FromChar(char c, byte width, Style style) =>
        new(c, '\0', null, style, width);

    /// <summary>Returns this cell with a different style, keeping its glyph and width (allocation-free).</summary>
    public Cell WithStyle(Style style) => new(_c0, _c1, _text, style, Width);

    /// <summary>The grapheme as a string (allocates for inline cells — prefer <see cref="AppendTo"/>).</summary>
    public string Grapheme
    {
        get
        {
            if (_text is not null)
            {
                return _text;
            }

            if (_c0 == '\0')
            {
                return string.Empty;
            }

            if (_c1 == '\0')
            {
                return _c0.ToString();
            }

            return new string(stackalloc char[] { _c0, _c1 });
        }
    }

    /// <summary>Appends this cell's glyph to a builder without allocating.</summary>
    public void AppendTo(StringBuilder sb)
    {
        if (_text is not null) { sb.Append(_text); return; }
        if (_c0 == '\0')
        {
            return;
        }

        sb.Append(_c0);
        if (_c1 != '\0')
        {
            sb.Append(_c1);
        }
    }

    public bool IsContinuation => Width == 0;
    public bool IsWide => Width == 2;

    /// <summary>True when the cell carries no printable glyph (continuation/empty).</summary>
    public bool IsEmpty => _text is null && _c0 == '\0';

    public bool Equals(Cell other) =>
        Width == other.Width &&
        Style == other.Style &&
        _c0 == other._c0 &&
        _c1 == other._c1 &&
        string.Equals(_text, other._text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Cell c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(_c0, _c1, _text, Style, Width);
    public static bool operator ==(Cell a, Cell b) => a.Equals(b);
    public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
}
