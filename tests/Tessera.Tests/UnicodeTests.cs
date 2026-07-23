using System.Text;
using Tessera.Primitives;

namespace Tessera.Tests;

public class UnicodeTests
{
    [Theory]
    [InlineData('A', 1)]
    [InlineData(' ', 1)]
    [InlineData('~', 1)]
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
    [InlineData(0x0000)] // NUL
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
}
