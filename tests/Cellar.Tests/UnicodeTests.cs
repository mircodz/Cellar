using System.Text;
using Cellar.Primitives;

namespace Cellar.Tests;

public class UnicodeTests
{
    [Theory]
    [InlineData('A', 1)]
    public void RuneWidth_Ascii_IsOne(char c, int expected)
    {
        Assert.Equal(expected, Unicode.RuneWidth(new Rune(c)));
    }

    [Theory]
    [InlineData(0x4E00)] // CJK 一
    [InlineData(0x3042)] // Hiragana あ
    [InlineData(0xFF21)] // Fullwidth Ａ
    [InlineData(0xAC00)] // Hangul 가
    public void RuneWidth_EastAsianWide_IsTwo(int codepoint)
    {
        Assert.Equal(2, Unicode.RuneWidth(new Rune(codepoint)));
    }

    [Theory]
    [InlineData(0x0301)] // combining acute accent
    [InlineData(0x200B)] // zero-width space
    [InlineData(0x202E)] // right-to-left override
    public void RuneWidth_ZeroWidth_IsZero(int codepoint)
    {
        Assert.Equal(0, Unicode.RuneWidth(new Rune(codepoint)));
    }

    [Fact]
    public void StringWidth_MixedAsciiAndCjk()
    {
        // "aあb" => 1 + 2 + 1
        Assert.Equal(4, Unicode.StringWidth("aあb"));
    }

    [Fact]
    public void StringWidth_CombiningMark_CountsAsBaseWidth()
    {
        // "e" + combining acute => single grapheme, width 1
        Assert.Equal(1, Unicode.StringWidth("é"));
    }

    [Fact]
    public void StringWidth_Empty_IsZero()
    {
        Assert.Equal(0, Unicode.StringWidth(""));
    }

    [Fact]
    public void StringWidth_EmojiVariationSelector_IsWide()
    {
        // Base heavy-check-mark (U+2714, width 1) + VS16 (U+FE0F) => emoji presentation,
        // which terminals render 2 cells wide.
        Assert.Equal(2, Unicode.StringWidth("✔️"));
        Assert.Equal(2, Unicode.GraphemeWidth("✔️"));
    }

    [Theory]
    [InlineData(0x0000, 0x2400)] // NUL as ␀
    [InlineData(0x001B, 0x241B)] // ESC as ␛
    [InlineData(0x000A, 0x240A)] // LF as ␊
    [InlineData(0x007F, 0x2421)] // DEL as ␡
    [InlineData(0x009B, 0xFFFD)] // C1 CSI
    [InlineData(0x0009, 0x0020)] // tab as a space
    [InlineData(0x0041, 0x0041)] // anything else is itself
    public void Printable_ReplacesControlCharacters(int codepoint, int expected)
    {
        Assert.Equal(expected, Unicode.Printable(new Rune(codepoint)).Value);
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x001B)]
    [InlineData(0x009B)]
    public void RuneWidth_ControlCharacter_IsTheWidthOfItsPicture(int codepoint)
    {
        Assert.Equal(1, Unicode.RuneWidth(new Rune(codepoint)));
    }

    [Fact]
    public void StringWidth_UnpairedSurrogate_CountsAsReplacementCharacter()
    {
        Assert.Equal(3, Unicode.StringWidth("a\uD800b"));
    }

    [Fact]
    public void Sanitize_ReturnsSafeTextItself()
    {
        const string text = "plain é 你好 ✔️ 👨‍👩‍👧";
        Assert.Same(text, Unicode.Sanitize(text));
    }

    [Fact]
    public void Sanitize_ReplacesControlsDropsBidiControlsAndFixesSurrogates()
    {
        Assert.Equal("a\u241B[2Jb\uFFFDc", Unicode.Sanitize("a\u001b[2J\u202Eb\uD800c"));
    }
}
