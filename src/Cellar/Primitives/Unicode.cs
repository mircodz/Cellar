using System;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace Cellar.Primitives;

/// <summary>
/// Terminal display-width for Unicode text (a wcwidth port): most scalars are 1 cell,
/// East-Asian Wide/Fullwidth are 2, and combining and format marks are 0. Control characters
/// are 1: they're never written to the terminal, but drawn as a visible stand-in (<see cref="Printable"/>).
/// </summary>
public static class Unicode
{
    /// <summary>Display width of a single scalar in cells: 0, 1, or 2.</summary>
    public static int RuneWidth(Rune rune)
    {
        int cp = rune.Value;
        // Printable ASCII is the overwhelming common case; skip the category lookups below.
        if (cp >= 0x20 && cp < 0x7f)
        {
            return 1;
        }
        if (IsControl(cp))
        {
            return 1;
        }
        if (IsZeroWidth(cp))
        {
            return 0;
        }
        if (IsWide(cp))
        {
            return 2;
        }
        return 1;
    }

    // Emoji variation selector: forces the preceding base character into 2-cell emoji
    // presentation (e.g. ✔️ ❤️), even though the base alone measures 1.
    private const int VariationSelector16 = 0xFE0F;

    /// <summary>
    /// Display width of a string in cells. Allocation-free: sums each scalar's width. Equals
    /// grapheme-cluster width because combining marks are zero-width, so no segmentation is needed.
    /// </summary>
    public static int StringWidth(string text) => StringWidth(text.AsSpan());

    /// <summary>Span overload of <see cref="StringWidth(string)"/>. Allocation-free.</summary>
    public static int StringWidth(ReadOnlySpan<char> text)
    {
        int total = 0;
        int i = 0;
        while (i < text.Length)
        {
            Rune.DecodeFromUtf16(text[i..], out Rune rune, out int length); // U+FFFD for an unpaired surrogate
            i += length;

            int w = RuneWidth(rune);
            // A following VS16 promotes a text-presentation base to 2-cell emoji width.
            if (w == 1 && i < text.Length && text[i] == VariationSelector16)
            {
                w = 2;
            }
            total += w;
        }
        return total;
    }

    /// <summary>Width of a grapheme cluster: the width of its first non-zero-width scalar.</summary>
    public static int GraphemeWidth(string grapheme)
    {
        if (string.IsNullOrEmpty(grapheme))
        {
            return 0;
        }

        int width = 0;
        foreach (var rune in grapheme.EnumerateRunes())
        {
            if (rune.Value == VariationSelector16)
            {
                return 2; // emoji presentation of whatever base preceded it
            }

            if (width == 0)
            {
                width = RuneWidth(rune);
            }
        }
        return width;
    }

    /// <summary>What is drawn for <paramref name="rune"/>. The terminal would act on a control character instead of
    /// showing it (ESC starts an escape sequence, CR moves the cursor), so C0 controls and DEL are drawn as their
    /// Control Pictures glyph (ESC as ␛), C1 controls as U+FFFD, and a tab as a space. Anything else is itself.</summary>
    public static Rune Printable(Rune rune)
    {
        int cp = rune.Value;
        if (!IsControl(cp))
        {
            return rune;
        }
        return cp switch
        {
            '\t' => new Rune(' '),
            < 0x20 => new Rune(0x2400 + cp),
            0x7f => new Rune(0x2421),
            _ => Rune.ReplacementChar,
        };
    }

    /// <summary>Whether <paramref name="cp"/> is a bidi control: LRM, RLM, ALM, or an embedding, override, or
    /// isolate. They're invisible, but a bidi-aware terminal reorders the text after them, so they're never drawn.</summary>
    public static bool IsBidiControl(int cp) =>
        cp is 0x061c or 0x200e or 0x200f or (>= 0x202a and <= 0x202e) or (>= 0x2066 and <= 0x2069);

    /// <summary>Makes text safe to write to the terminal: control characters become what <see cref="Printable"/>
    /// draws for them, bidi controls are dropped, and unpaired surrogates become U+FFFD. Returns
    /// <paramref name="text"/> itself when it's already safe.</summary>
    public static string Sanitize(string text)
    {
        var span = text.AsSpan();
        int safe = 0;
        while (safe < span.Length)
        {
            if (Rune.DecodeFromUtf16(span[safe..], out Rune rune, out int length) != OperationStatus.Done ||
                IsControl(rune.Value) || IsBidiControl(rune.Value))
            {
                break;
            }
            safe += length;
        }
        if (safe == span.Length)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        builder.Append(span[..safe]);
        Span<char> utf16 = stackalloc char[2];
        for (int i = safe; i < span.Length;)
        {
            Rune.DecodeFromUtf16(span[i..], out Rune rune, out int length); // U+FFFD for an unpaired surrogate
            i += length;
            if (!IsBidiControl(rune.Value))
            {
                builder.Append(utf16[..Printable(rune).EncodeToUtf16(utf16)]);
            }
        }
        return builder.ToString();
    }

    // C0 controls, DEL, and C1 controls.
    private static bool IsControl(int cp) => cp < 0x20 || (cp >= 0x7f && cp < 0xa0);

    private static bool IsZeroWidth(int cp)
    {
        switch (Rune.GetUnicodeCategory(new Rune(cp)))
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.EnclosingMark:
                return true;
            case UnicodeCategory.Format:
                return cp != 0x00AD; // soft hyphen renders as width 1
        }
        // Zero-width space, ZWNJ, ZWJ, word joiner.
        return cp is 0x200B or 0x200C or 0x200D or 0x2060;
    }

    // East-Asian Wide/Fullwidth ranges — the compact common-case table, not exhaustive.
    private static bool IsWide(int cp)
    {
        return
            (cp >= 0x1100 && cp <= 0x115F)   || // Hangul Jamo init. consonants
            (cp == 0x2329 || cp == 0x232A)   || // angle brackets
            (cp >= 0x2E80 && cp <= 0x303E)   || // CJK Radicals .. Kangxi
            (cp >= 0x3041 && cp <= 0x33FF)   || // Hiragana .. CJK symbols
            (cp >= 0x3400 && cp <= 0x4DBF)   || // CJK Ext A
            (cp >= 0x4E00 && cp <= 0x9FFF)   || // CJK Unified Ideographs
            (cp >= 0xA000 && cp <= 0xA4CF)   || // Yi
            (cp >= 0xAC00 && cp <= 0xD7A3)   || // Hangul Syllables
            (cp >= 0xF900 && cp <= 0xFAFF)   || // CJK Compat Ideographs
            (cp >= 0xFE10 && cp <= 0xFE19)   || // Vertical forms
            (cp >= 0xFE30 && cp <= 0xFE6F)   || // CJK Compat forms / Small forms
            (cp >= 0xFF00 && cp <= 0xFF60)   || // Fullwidth forms
            (cp >= 0xFFE0 && cp <= 0xFFE6)   || // Fullwidth signs
            (cp >= 0x1F300 && cp <= 0x1F64F) || // Misc symbols & pictographs, emoji
            (cp >= 0x1F900 && cp <= 0x1F9FF) || // Supplemental symbols & pictographs
            (cp >= 0x20000 && cp <= 0x3FFFD);   // CJK Ext B..F and beyond
    }
}
