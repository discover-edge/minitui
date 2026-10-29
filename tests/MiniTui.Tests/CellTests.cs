
namespace MiniTui.Tests;

public class CellTests
{
    [Fact]
    public void Empty_IsSpaceWithResetColors()
    {
        Assert.Equal(new Cell(" ", Color.Reset, Color.Reset, Modifiers.None), Cell.Empty);
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("日", 2)]
    [InlineData("👍", 2)]
    [InlineData("", 0)]
    [InlineData("́", 1)]
    public void Width_IsZeroForContinuation_ElseOneOrTwo(string symbol, int expected)
    {
        var cell = Cell.Empty with { Symbol = symbol };

        Assert.Equal(expected, cell.Width);
        Assert.Equal(symbol.Length == 0, cell.IsContinuation);
    }

    [Fact]
    public void DefaultCell_HasSpaceSymbol()
    {
        Assert.Equal(" ", default(Cell).Symbol);
    }

    [Fact]
    public void AsciiSymbols_AreCached()
    {
        Assert.Same(Symbols.Of('a'), Symbols.Of('a'));
    }

    [Fact]
    public void LoneSurrogate_IsReplaced()
    {
        Assert.Equal("\uFFFD", Symbols.Of('\uD83D'));
    }
}
