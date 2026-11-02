namespace MiniTui.Tests;

/// <summary>Fuzzy.Match (1.x spec §13.5).</summary>
public class FuzzyTests
{
    [Theory]
    [InlineData("ref", "Refresh")]
    [InlineData("REF", "refresh")]
    [InlineData("rfs", "Refresh")]
    [InlineData("new file", "New File")]
    [InlineData("new file", "NewFile")]
    [InlineData("nf", "New File")]
    [InlineData("a", "a")]
    public void Matches_TheCharactersInOrder_IgnoringCaseAndSpaces(string pattern, string text)
    {
        Assert.NotNull(Fuzzy.Match(pattern, text));
    }

    [Theory]
    [InlineData("fer", "Refresh")]
    [InlineData("refreshx", "Refresh")]
    [InlineData("z", "Refresh")]
    [InlineData("ab", "a")]
    [InlineData("a", "")]
    public void DoesntMatch_MissingOrOutOfOrderCharacters(string pattern, string text)
    {
        Assert.Null(Fuzzy.Match(pattern, text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPattern_MatchesEverything_WithScoreZero(string pattern)
    {
        var match = Fuzzy.Match(pattern, "Anything");

        Assert.NotNull(match);
        Assert.Equal(0, match.Value.Score);
        Assert.Empty(match.Value.Positions);
        Assert.NotNull(Fuzzy.Match(pattern, ""));
    }

    [Theory]
    [InlineData("nf", "New File", new[] { 0, 4 })]
    [InlineData("nf", "Info New File", new[] { 5, 9 })]
    [InlineData("of", "OpenFile", new[] { 0, 4 })]
    [InlineData("sc", "save-config", new[] { 0, 5 })]
    [InlineData("ref", "Refresh", new[] { 0, 1, 2 })]
    public void Positions_PreferWordStartsAndRuns(string pattern, string text, int[] positions)
    {
        Assert.Equal(positions, Fuzzy.Match(pattern, text)!.Value.Positions);
    }

    [Theory]
    [InlineData("ref", "Refresh", "Rename File")]          // a prefix beats word starts
    [InlineData("rf", "Rename File", "Rarefy")]            // word starts beat scattered letters
    [InlineData("save", "Save", "Save As")]                // the whole text beats a prefix
    [InlineData("op", "Open", "Go to Project")]            // a run beats separate word starts
    [InlineData("Save", "Save", "save")]                   // the same case scores higher
    [InlineData("gl", "Go Line", "Go   Long")]             // skipped characters cost
    public void Scores_OrderTheMatches(string pattern, string better, string worse)
    {
        var a = Fuzzy.Match(pattern, better)!.Value.Score;
        var b = Fuzzy.Match(pattern, worse)!.Value.Score;

        Assert.True(a > b, $"{better} ({a}) should beat {worse} ({b})");
    }

    [Fact]
    public void NonBmpText_DoesntThrow()
    {
        Assert.NotNull(Fuzzy.Match("a", "😀 a"));
        Assert.NotNull(Fuzzy.Match("😀", "x😀y"));
        Assert.Null(Fuzzy.Match("😀", "abc"));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Fuzzy.Match(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => Fuzzy.Match("a", null!));
    }
}
