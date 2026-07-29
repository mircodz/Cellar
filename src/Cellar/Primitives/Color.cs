using System;

namespace Cellar.Primitives;

/// <summary>The color depth a terminal can render. Used to downgrade truecolor output.</summary>
public enum ColorDepth
{
    /// <summary>Monochrome / no color.</summary>
    NoColor = 0,
    /// <summary>Basic 16-color ANSI palette.</summary>
    Ansi16 = 4,
    /// <summary>256-color xterm palette.</summary>
    Ansi256 = 8,
    /// <summary>24-bit truecolor.</summary>
    TrueColor = 24,
}

/// <summary>
/// A color: terminal default, one of the 16 ANSI colors, or 24-bit RGB. RGB values are
/// quantized on demand to fit a terminal's capabilities.
/// </summary>
public readonly partial struct Color : IEquatable<Color>
{
    private enum Kind : byte { Default, Ansi, Rgb }

    private readonly Kind _kind;
    private readonly byte _r; // Ansi: palette index 0-15 stored in _r
    private readonly byte _g;
    private readonly byte _b;

    private Color(Kind kind, byte r, byte g, byte b)
    {
        _kind = kind;
        _r = r;
        _g = g;
        _b = b;
    }

    /// <summary>The terminal's default foreground/background (no SGR color set).</summary>
    public static Color Default => new(Kind.Default, 0, 0, 0);

    /// <summary>A 24-bit RGB color.</summary>
    public static Color Rgb(byte r, byte g, byte b) => new(Kind.Rgb, r, g, b);

    /// <summary>Clamps an integer to the 0-255 color-channel range.</summary>
    public static byte ClampChannel(int value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>One of the 16 standard ANSI palette colors (index 0-15).</summary>
    public static Color Ansi(int index)
    {
        if (index is < 0 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "ANSI index must be 0-15.");
        }

        return new Color(Kind.Ansi, (byte)index, 0, 0);
    }

    public bool IsDefault => _kind == Kind.Default;
    public bool IsRgb => _kind == Kind.Rgb;
    public bool IsAnsi => _kind == Kind.Ansi;

    public byte R => _r;
    public byte G => _g;
    public byte B => _b;

    /// <summary>The ANSI palette index for an <see cref="IsAnsi"/> color.</summary>
    public int AnsiIndex => _r;

    public static Color Black => Ansi(0);
    public static Color Red => Ansi(1);
    public static Color Green => Ansi(2);
    public static Color Yellow => Ansi(3);
    public static Color Blue => Ansi(4);
    public static Color Magenta => Ansi(5);
    public static Color Cyan => Ansi(6);
    public static Color White => Ansi(7);
    public static Color BrightBlack => Ansi(8);
    public static Color BrightRed => Ansi(9);
    public static Color BrightGreen => Ansi(10);
    public static Color BrightYellow => Ansi(11);
    public static Color BrightBlue => Ansi(12);
    public static Color BrightMagenta => Ansi(13);
    public static Color BrightCyan => Ansi(14);
    public static Color BrightWhite => Ansi(15);

    public bool Equals(Color other) =>
        _kind == other._kind && _r == other._r && _g == other._g && _b == other._b;

    public override bool Equals(object? obj) => obj is Color c && Equals(c);
    public override int GetHashCode() => HashCode.Combine((byte)_kind, _r, _g, _b);
    public static bool operator ==(Color a, Color b) => a.Equals(b);
    public static bool operator !=(Color a, Color b) => !a.Equals(b);

    public override string ToString() => _kind switch
    {
        Kind.Default => "Default",
        Kind.Ansi => $"Ansi({_r})",
        _ => $"Rgb({_r},{_g},{_b})",
    };
}
