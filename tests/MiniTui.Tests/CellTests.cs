#pragma warning disable CS0618 // Exercises the 0.7 compatibility shims.

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
    public void ObsoleteCharConstructor_MapsToSymbolAndNamedColors()
    {
        var cell = new Cell('x', ConsoleColor.Red, ConsoleColor.Blue);

        Assert.Equal(new Cell("x", Color.Red, Color.Blue, Modifiers.None), cell);
        Assert.Equal('x', cell.Char);
    }

    [Fact]
    public void ObsoleteChar_IsFirstUtf16UnitOrSpace()
    {
        Assert.Equal('\uD83D', (Cell.Empty with { Symbol = "👍" }).Char);
        Assert.Equal(' ', (Cell.Empty with { Symbol = "" }).Char);
    }

    [Fact]
    public void ObsoleteFrom_UsesDefaultColors()
    {
        Assert.Equal(Cell.Empty with { Symbol = "q" }, Cell.From('q'));
    }

    [Fact]
    public void AsciiSymbols_AreCached()
    {
        Assert.Same(new Cell('a', ConsoleColor.Gray, ConsoleColor.Black).Symbol, Cell.From('a').Symbol);
    }

    [Fact]
    public void ObsoleteCharConstructor_ReplacesLoneSurrogate()
    {
        Assert.Equal("�", new Cell('\uD83D', ConsoleColor.Gray, ConsoleColor.Black).Symbol);
    }
}
