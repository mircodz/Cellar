using System;

namespace Cellar.Primitives;

/// <summary>Maps 24-bit RGB down to the 256- or 16-color palettes. Pure and deterministic.</summary>
public static class ColorQuantizer
{
    /// <summary>The 6 levels used per channel by the xterm 6x6x6 color cube (indices 16-231).</summary>
    private static readonly int[] CubeLevels = { 0, 95, 135, 175, 215, 255 };

    // Standard xterm RGB values for the first 16 palette entries, used for 16-color matching.
    private static readonly (int R, int G, int B)[] Ansi16 =
    {
        (  0,   0,   0), (128,   0,   0), (  0, 128,   0), (128, 128,   0),
        (  0,   0, 128), (128,   0, 128), (  0, 128, 128), (192, 192, 192),
        (128, 128, 128), (255,   0,   0), (  0, 255,   0), (255, 255,   0),
        (  0,   0, 255), (255,   0, 255), (  0, 255, 255), (255, 255, 255),
    };

    /// <summary>Maps an RGB triple to the nearest xterm-256 palette index (0-255).</summary>
    public static int ToAnsi256(byte r, byte g, byte b)
    {
        int cubeIndex = 16
            + 36 * NearestCubeComponent(r)
            + 6 * NearestCubeComponent(g)
            + NearestCubeComponent(b);
        int cubeDist = Distance(r, g, b, CubeRgb(cubeIndex));

        // The grayscale ramp (232-255) often matches gray tones better than the color cube.
        int grayIndex = NearestGray(r, g, b, out (int, int, int) grayRgb);
        int grayDist = Distance(r, g, b, grayRgb);

        return grayDist < cubeDist ? grayIndex : cubeIndex;
    }

    /// <summary>Maps an RGB triple to the nearest of the 16 base ANSI colors (0-15).</summary>
    public static int ToAnsi16(byte r, byte g, byte b)
    {
        int best = 0;
        int bestDist = int.MaxValue;
        for (int i = 0; i < Ansi16.Length; i++)
        {
            int d = Distance(r, g, b, Ansi16[i]);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private static int NearestCubeComponent(byte value)
    {
        int best = 0;
        int bestDist = int.MaxValue;
        for (int i = 0; i < CubeLevels.Length; i++)
        {
            int d = Math.Abs(CubeLevels[i] - value);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private static (int, int, int) CubeRgb(int index)
    {
        int i = index - 16;
        int r = CubeLevels[i / 36];
        int g = CubeLevels[(i / 6) % 6];
        int b = CubeLevels[i % 6];
        return (r, g, b);
    }

    private static int NearestGray(byte r, byte g, byte b, out (int, int, int) rgb)
    {
        // xterm grayscale ramp: index 232..255 => 8 + 10*n, n in 0..23.
        int avg = (r + g + b) / 3;
        int n = (int)Math.Round((avg - 8) / 10.0);
        n = Math.Clamp(n, 0, 23);
        int level = 8 + 10 * n;
        rgb = (level, level, level);
        return 232 + n;
    }

    private static int Distance(int r, int g, int b, (int R, int G, int B) c)
    {
        int dr = r - c.R, dg = g - c.G, db = b - c.B;
        return dr * dr + dg * dg + db * db;
    }
}
