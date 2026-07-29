using System.Text;
using Cellar.Primitives;

namespace Cellar.Rendering;

/// <summary>
/// Encodes <see cref="Style"/> transitions into ANSI SGR sequences, downgrading truecolor to
/// the target <see cref="ColorDepth"/>. Writes into a caller-provided <see cref="StringBuilder"/>.
/// </summary>
public sealed class AnsiStyleWriter
{
    private readonly ColorDepth _depth;

    public AnsiStyleWriter(ColorDepth depth) => _depth = depth;

    /// <summary>A concrete color emitted for default-background cells (one solid screen fill);
    /// null keeps true transparency.</summary>
    public Color? DefaultBackground { get; set; }

    /// <summary>Emits the SGR sequence setting style <paramref name="to"/> (full reset + rebuild).</summary>
    public void WriteTransition(StringBuilder sb, Style to)
    {
        sb.Append("\x1b[0"); // reset, then additively build the target state

        var a = to.Attributes;
        if ((a & TextAttributes.Bold) != 0)
        {
            sb.Append(";1");
        }

        if ((a & TextAttributes.Dim) != 0)
        {
            sb.Append(";2");
        }

        if ((a & TextAttributes.Italic) != 0)
        {
            sb.Append(";3");
        }

        if ((a & TextAttributes.Underline) != 0)
        {
            sb.Append(";4");
        }

        if ((a & TextAttributes.Blink) != 0)
        {
            sb.Append(";5");
        }

        if ((a & TextAttributes.Reverse) != 0)
        {
            sb.Append(";7");
        }

        if ((a & TextAttributes.Strikethrough) != 0)
        {
            sb.Append(";9");
        }

        AppendColor(sb, to.Foreground, isBackground: false);

        // Substitute the configured default background for an "inherit" (default) background.
        var bg = to.Background;
        if (bg.IsDefault && DefaultBackground is { } db)
        {
            bg = db;
        }

        AppendColor(sb, bg, isBackground: true);

        sb.Append('m');
    }

    private void AppendColor(StringBuilder sb, Color color, bool isBackground)
    {
        if (color.IsDefault)
        {
            return; // reset already left it at terminal default
        }

        if (color.IsAnsi)
        {
            AppendAnsi16(sb, color.AnsiIndex, isBackground);
            return;
        }

        // RGB — downgrade according to terminal capability.
        switch (_depth)
        {
            case ColorDepth.TrueColor:
                sb.Append(isBackground ? ";48;2;" : ";38;2;");
                sb.Append(color.R).Append(';').Append(color.G).Append(';').Append(color.B);
                break;
            case ColorDepth.Ansi256:
                sb.Append(isBackground ? ";48;5;" : ";38;5;");
                sb.Append(ColorQuantizer.ToAnsi256(color.R, color.G, color.B));
                break;
            case ColorDepth.Ansi16:
                AppendAnsi16(sb, ColorQuantizer.ToAnsi16(color.R, color.G, color.B), isBackground);
                break;
            case ColorDepth.NoColor:
            default:
                break;
        }
    }

    private static void AppendAnsi16(StringBuilder sb, int index, bool isBackground)
    {
        // 0-7 => 30-37 / 40-47 ; 8-15 => 90-97 / 100-107 (bright)
        int baseCode = index < 8
            ? (isBackground ? 40 : 30) + index
            : (isBackground ? 100 : 90) + (index - 8);
        sb.Append(';').Append(baseCode);
    }
}
