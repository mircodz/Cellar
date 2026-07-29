using System;
using System.Globalization;
using System.Text;

namespace Cellar.Primitives;

/// <summary>
/// Terminal display-width for Unicode text (a wcwidth port): most scalars are 1 cell,
/// East-Asian Wide/Fullwidth are 2, and combining/control/format marks are 0.
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
        if (cp == 0)
        {
            return 0;
        }
        if (cp < 32 || (cp >= 0x7f && cp < 0xa0)) // C0/C1 controls
        {
            return 0;
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
            char c = text[i];
            Rune rune;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                rune = new Rune(c, text[i + 1]);
                i += 2;
            }
            else
            {
                rune = new Rune(c);
                i += 1;
            }

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
