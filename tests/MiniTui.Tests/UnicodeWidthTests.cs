namespace MiniTui.Tests;

public class UnicodeWidthTests
{
    [Theory]
    [InlineData("abc", 3)]
    [InlineData("日本", 4)]
    [InlineData("é", 1)]          // é precomposed
    [InlineData("é", 1)]         // é decomposed
    [InlineData("👍", 2)]
    [InlineData("👍🏽", 2)]
    [InlineData("👨‍👩‍👧", 2)]
    [InlineData("🇺🇸", 2)]
    [InlineData("❤️", 2)]
    [InlineData("​", 0)]          // zero width space
    [InlineData("⠿", 1)]               // braille
    [InlineData("─│┌", 3)]
    public void Of_RequiredCases(string text, int expected)
    {
        Assert.Equal(expected, UnicodeWidth.Of(text));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("́", 0)]          // Mn
    [InlineData("⃝", 0)]          // Me
    [InlineData("­", 0)]          // Cf (soft hyphen)
    [InlineData("🇺", 2)]               // lone regional indicator: Emoji_Presentation
    [InlineData("❤", 1)]               // text presentation without VS16
    [InlineData("１", 2)]              // fullwidth digit
    [InlineData("한", 2)]
    [InlineData("�", 1)]
    public void OfGrapheme_Rules(string grapheme, int expected)
    {
        Assert.Equal(expected, UnicodeWidth.OfGrapheme(grapheme));
    }

    [Fact]
    public void Of_AsciiFollowedByCombiningMark_UsesGraphemeWidth()
    {
        Assert.Equal(2, UnicodeWidth.Of("áb"));
    }

    [Fact]
    public void Graphemes_SplitsClusters()
    {
        Assert.Equal(["a", "é", "👨‍👩‍👧", "🇺🇸"], UnicodeWidth.Graphemes("aé👨‍👩‍👧🇺🇸"));
    }

    [Theory]
    [InlineData("hello", 10, "hello")]
    [InlineData("hello", 5, "hello")]
    [InlineData("hello", 4, "hel…")]
    [InlineData("日本語", 5, "日本…")]
    [InlineData("日本語", 4, "日…")]
    [InlineData("hello", 1, "…")]
    [InlineData("hello", 0, "")]
    [InlineData("hello", -3, "")]
    [InlineData("👨‍👩‍👧x", 2, "…")]
    public void Truncate_KeepsWholeGraphemes(string text, int maxWidth, string expected)
    {
        Assert.Equal(expected, UnicodeWidth.Truncate(text, maxWidth));
    }

    [Fact]
    public void Truncate_DropsEllipsisWiderThanMaxWidth()
    {
        Assert.Equal("ab", UnicodeWidth.Truncate("abcdef", 2, "..."));
    }
}
