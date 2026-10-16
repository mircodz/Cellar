using System;
using System.Collections.Generic;
using Cellar.Primitives;
using Cellar.Rendering;

namespace Cellar.Text;

/// <summary>Horizontal justification for a laid-out line of styled text.</summary>
public enum Justify { Left, Center, Right, Fill }

/// <summary>
/// Lays out and draws <see cref="StyledText"/>: word-wrapping, justifying, and ellipsis
/// truncation, preserving style boundaries. Walks graphemes and writes cells directly to
/// avoid per-cell string allocations.
/// </summary>
public static class TextRenderer
{
    /// <summary>Wraps <paramref name="text"/> to <paramref name="width"/> columns, one
    /// <see cref="StyledText"/> per line (at spaces and line breaks; over-long words are hard-split).</summary>
    public static List<StyledText> Wrap(StyledText text, int width)
    {
        var lines = new List<StyledText>();
        if (width <= 0)
        {
            lines.Add(StyledText.Empty());
            return lines;
        }

        var current = StyledText.Empty();
        int lineWidth = 0;

        foreach (var span in text.Spans)
        {
            foreach (var (word, kind) in Tokenize(span.Text))
            {
                if (kind == Token.LineBreak)
                {
                    lines.Add(current);
                    current = StyledText.Empty();
                    lineWidth = 0;
                    continue;
                }

                int wordWidth = Unicode.StringWidth(word);

                if (kind == Token.Space)
                {
                    // Collapse a space that would overflow into a line break instead.
                    if (lineWidth + wordWidth > width)
                    {
                        lines.Add(current);
                        current = StyledText.Empty();
                        lineWidth = 0;
                    }
                    else
                    {
                        current.Append(word, span.Style);
                        lineWidth += wordWidth;
                    }
                    continue;
                }

                if (wordWidth > width)
                {
                    // Hard-split an over-long word across lines.
                    foreach (var piece in HardSplit(word, width, lineWidth))
                    {
                        int pieceWidth = Unicode.StringWidth(piece);
                        if (lineWidth + pieceWidth > width && lineWidth > 0)
                        {
                            lines.Add(current);
                            current = StyledText.Empty();
                            lineWidth = 0;
                        }
                        current.Append(piece, span.Style);
                        lineWidth += pieceWidth;
                    }
                    continue;
                }

                if (lineWidth + wordWidth > width && lineWidth > 0)
                {
                    lines.Add(current);
                    current = StyledText.Empty();
                    lineWidth = 0;
                }
                current.Append(word, span.Style);
                lineWidth += wordWidth;
            }
        }

        lines.Add(current);
        return lines;
    }

    /// <summary>Draws one line of styled text at row <paramref name="y"/> in the band
    /// [<paramref name="x"/>, x+<paramref name="width"/>), justified; overflow gets an ellipsis.
    /// When <paramref name="links"/> is provided, each drawn link span's screen range is recorded
    /// into it for hit-testing.</summary>
    public static void DrawLine(Surface surface, int x, int y, int width, StyledText line, Justify justify,
        List<LinkHit>? links = null)
    {
        if (width <= 0)
        {
            return;
        }

        int textWidth = line.Width;
        var spans = line.Spans; // IReadOnlyList backed by List<Span> — no per-frame array copy

        if (textWidth > width)
        {
            DrawTruncated(surface, x, y, width, spans);
            return;
        }

        int startX = justify switch
        {
            Justify.Right => x + (width - textWidth),
            Justify.Center => x + (width - textWidth) / 2,
            _ => x,
        };

        if (justify == Justify.Fill && spans.Count > 0)
        {
            DrawFilled(surface, x, y, width, spans, textWidth);
            return;
        }

        int cx = startX;
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            int nextX = surface.DrawText(cx, y, span.Text, span.Style);
            if (links is not null && span.Link is not null && nextX > cx)
            {
                // A LinkInfo wrapper carries a custom hover style; a bare object is just the payload.
                if (span.Link is LinkInfo info)
                {
                    links.Add(new LinkHit(y, cx, nextX, info.Payload, info.HoverStyle));
                }
                else
                {
                    links.Add(new LinkHit(y, cx, nextX, span.Link));
                }
            }
            cx = nextX;
        }
    }

    /// <summary>Wraps <paramref name="text"/> to the region and draws the lines that fit.
    /// Returns the number drawn.</summary>
    public static int DrawBlock(Surface surface, Rect area, StyledText text, Justify justify)
    {
        if (area.IsEmpty)
        {
            return 0;
        }

        var lines = Wrap(text, area.Width);
        int n = Math.Min(lines.Count, area.Height);
        for (int i = 0; i < n; i++)
        {
            DrawLine(surface, area.X, area.Y + i, area.Width, lines[i], justify);
        }

        return n;
    }

    private static void DrawFilled(Surface surface, int x, int y, int width, IReadOnlyList<Span> spans, int textWidth)
    {
        // Distribute extra space across inter-word gaps for justified (fill) text. Indexed
        // loops rather than foreach, so we don't box the IReadOnlyList enumerator.
        int gaps = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (s.Text.Length > 0 && string.IsNullOrWhiteSpace(s.Text))
            {
                gaps++;
            }
        }

        if (gaps == 0)
        {
            int cx = x;
            for (int i = 0; i < spans.Count; i++)
            {
                cx = surface.DrawText(cx, y, spans[i].Text, spans[i].Style);
            }

            return;
        }

        int extra = width - textWidth;
        int perGap = extra / gaps;
        int remainder = extra % gaps;

        int drawX = x;
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (span.Text.Length > 0 && string.IsNullOrWhiteSpace(span.Text))
            {
                int add = perGap + (remainder-- > 0 ? 1 : 0);
                drawX = surface.DrawText(drawX, y, span.Text, span.Style);
                drawX += add;
            }
            else
            {
                drawX = surface.DrawText(drawX, y, span.Text, span.Style);
            }
        }
    }

    private static void DrawTruncated(Surface surface, int x, int y, int width, IReadOnlyList<Span> spans)
    {
        if (width == 1)
        {
            surface.DrawText(x, y, "…", spans.Count > 0 ? spans[0].Style : Style.Default);
            return;
        }

        int budget = width - 1;
        int cx = x;
        int used = 0;
        Style lastStyle = Style.Default;

        for (int si = 0; si < spans.Count; si++)
        {
            var span = spans[si];
            lastStyle = span.Style;
            var text = span.Text.AsSpan();
            int i = 0;
            while (i < text.Length)
            {
                // Take one grapheme cluster: a base scalar plus any trailing zero-width marks,
                // so a combining sequence ("e"+U+0301) is drawn as one unit, not split.
                int start = i;
                System.Text.Rune.DecodeFromUtf16(text[i..], out var rune, out int len);
                int gw = Unicode.RuneWidth(rune);
                i += len;

                while (i < text.Length)
                {
                    System.Text.Rune.DecodeFromUtf16(text[i..], out var next, out int nlen);
                    if (Unicode.RuneWidth(next) != 0)
                    {
                        break;
                    }
                    i += nlen; // absorb the mark into this cluster
                }

                if (gw > 0 && used + gw > budget)
                {
                    surface.DrawText(cx, y, "…", span.Style);
                    return;
                }
                cx = surface.DrawText(cx, y, text.Slice(start, i - start), span.Style);
                used += gw;
            }
        }
        surface.DrawText(cx, y, "…", lastStyle);
    }

    private enum Token { Word, Space, LineBreak }

    // Splits text into words, runs of spaces, and line breaks (\n or \r\n).
    private static IEnumerable<(string token, Token kind)> Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            int breakLength = LineBreakAt(text, i);
            if (breakLength > 0)
            {
                i += breakLength;
                yield return ("", Token.LineBreak);
                continue;
            }

            bool space = char.IsWhiteSpace(text[i]);
            int start = i;
            while (i < text.Length && char.IsWhiteSpace(text[i]) == space && LineBreakAt(text, i) == 0)
            {
                i++;
            }

            yield return (text.Substring(start, i - start), space ? Token.Space : Token.Word);
        }
    }

    private static int LineBreakAt(string text, int i) => text[i] switch
    {
        '\n' => 1,
        '\r' when i + 1 < text.Length && text[i + 1] == '\n' => 2,
        _ => 0,
    };

    private static IEnumerable<string> HardSplit(string word, int width, int startWidth)
    {
        var sb = new System.Text.StringBuilder();
        int w = 0;
        foreach (var rune in word.EnumerateRunes())
        {
            int gw = Unicode.RuneWidth(rune);
            if (w + gw > width && sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
                w = 0;
            }
            sb.Append(rune.ToString());
            w += gw;
        }
        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }
}
