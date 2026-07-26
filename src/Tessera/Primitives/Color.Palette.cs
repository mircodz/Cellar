using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tessera.Primitives;

/// <summary>Hex, HSL, named-color, and interpolation helpers.</summary>
public readonly partial struct Color
{
    private static readonly Dictionary<string, Color> Named =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = Color.Rgb(0, 0, 0),
            ["white"] = Color.Rgb(255, 255, 255),
            ["red"] = Color.Rgb(255, 0, 0),
            ["green"] = Color.Rgb(0, 128, 0),
            ["lime"] = Color.Rgb(0, 255, 0),
            ["blue"] = Color.Rgb(0, 0, 255),
            ["yellow"] = Color.Rgb(255, 255, 0),
            ["cyan"] = Color.Rgb(0, 255, 255),
            ["magenta"] = Color.Rgb(255, 0, 255),
            ["gray"] = Color.Rgb(128, 128, 128),
            ["grey"] = Color.Rgb(128, 128, 128),
            ["silver"] = Color.Rgb(192, 192, 192),
            ["maroon"] = Color.Rgb(128, 0, 0),
            ["olive"] = Color.Rgb(128, 128, 0),
            ["teal"] = Color.Rgb(0, 128, 128),
            ["navy"] = Color.Rgb(0, 0, 128),
            ["purple"] = Color.Rgb(128, 0, 128),
            ["orange"] = Color.Rgb(255, 165, 0),
            ["pink"] = Color.Rgb(255, 192, 203),
            ["gold"] = Color.Rgb(255, 215, 0),
            ["coral"] = Color.Rgb(255, 127, 80),
            ["salmon"] = Color.Rgb(250, 128, 114),
            ["turquoise"] = Color.Rgb(64, 224, 208),
            ["violet"] = Color.Rgb(238, 130, 238),
            ["indigo"] = Color.Rgb(75, 0, 130),
            ["crimson"] = Color.Rgb(220, 20, 60),
            ["skyblue"] = Color.Rgb(135, 206, 235),
            ["slateblue"] = Color.Rgb(106, 90, 205),
            ["seagreen"] = Color.Rgb(46, 139, 87),
            ["forestgreen"] = Color.Rgb(34, 139, 34),
            ["tomato"] = Color.Rgb(255, 99, 71),
            ["steelblue"] = Color.Rgb(70, 130, 180),
        };

    /// <summary>Parses <c>#rgb</c>/<c>#rrggbb</c> (with or without '#'); throws on bad input.</summary>
    public static Color Hex(string hex)
    {
        if (TryHex(hex, out var color))
        {
            return color;
        }

        throw new FormatException($"'{hex}' is not a valid hex color.");
    }

    /// <summary>Parses a hex color, returning false instead of throwing on bad input.</summary>
    public static bool TryHex(string hex, out Color color)
    {
        color = Color.Default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        ReadOnlySpan<char> s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
        {
            s = s.Slice(1);
        }

        if (s.Length == 3)
        {
            if (!TryNibble(s[0], out int r) || !TryNibble(s[1], out int g) || !TryNibble(s[2], out int b))
            {
                return false;
            }

            color = Color.Rgb((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
            return true;
        }
        if (s.Length == 6)
        {
            if (!TryByte(s.Slice(0, 2), out byte r) ||
                !TryByte(s.Slice(2, 2), out byte g) ||
                !TryByte(s.Slice(4, 2), out byte b))
            {
                return false;
            }

            color = Color.Rgb(r, g, b);
            return true;
        }
        return false;
    }

    /// <summary>RGB from HSL: hue in degrees, saturation and lightness in [0,1].</summary>
    public static Color Hsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);

        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double hp = hue / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        double r1 = 0, g1 = 0, b1 = 0;

        if (hp < 1) { r1 = c; g1 = x; }
        else if (hp < 2) { r1 = x; g1 = c; }
        else if (hp < 3) { g1 = c; b1 = x; }
        else if (hp < 4) { g1 = x; b1 = c; }
        else if (hp < 5) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }

        double m = lightness - c / 2;
        return Color.Rgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    /// <summary>Linearly interpolates two colors in RGB space; <paramref name="t"/> clamped to [0,1].</summary>
    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        if (!a.IsRgb || !b.IsRgb)
        {
            return t < 0.5 ? a : b;
        }

        byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
        byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
        byte bl = (byte)Math.Round(a.B + (b.B - a.B) * t);
        return Color.Rgb(r, g, bl);
    }

    /// <summary>Returns <paramref name="steps"/> colors evenly interpolated across the stops.</summary>
    public static Color[] Gradient(int steps, params Color[] stops)
    {
        if (steps <= 0)
        {
            return [];
        }

        if (stops.Length == 0)
        {
            return Enumerable.Repeat(Color.Default, steps).ToArray();
        }

        if (stops.Length == 1)
        {
            return Enumerable.Repeat(stops[0], steps).ToArray();
        }

        var result = new Color[steps];
        if (steps == 1) { result[0] = stops[0]; return result; }

        int segments = stops.Length - 1;
        for (int i = 0; i < steps; i++)
        {
            double pos = (double)i / (steps - 1) * segments;
            int seg = Math.Min((int)pos, segments - 1);
            double localT = pos - seg;
            result[i] = Lerp(stops[seg], stops[seg + 1], localT);
        }
        return result;
    }

    /// <summary>Looks up a CSS-style color name (case-insensitive). Returns false if unknown.</summary>
    public static bool TryNamed(string name, out Color color)
        => Named.TryGetValue(name.Trim(), out color);

    private static bool TryNibble(char c, out int value)
    {
        switch (c)
        {
            case >= '0' and <= '9': value = c - '0'; return true;
            case >= 'a' and <= 'f': value = c - 'a' + 10; return true;
            case >= 'A' and <= 'F': value = c - 'A' + 10; return true;
            default: value = 0; return false;
        }
    }

    private static bool TryByte(ReadOnlySpan<char> s, out byte value)
        => byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
