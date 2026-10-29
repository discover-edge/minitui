// Regression tests for the baseline defects in docs/SPEC.md §2.
// Some use the 0.7 Cell(char, ConsoleColor, ConsoleColor) shim on purpose.
#pragma warning disable CS0618

using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class DefectTests
{
    [Theory]
    [InlineData(3, 3, 2)]
    [InlineData(1, 10, 1)]
    [InlineData(0, 0, 5)]
    public void D6_Shrink_NeverReturnsNegativeSize(int width, int height, int margin)
    {
        var rect = new Rect(10, 10, width, height).Shrink(margin);

        Assert.True(rect.Width >= 0);
        Assert.True(rect.Height >= 0);
    }

    [Fact]
    public void D6_Shrink_OverShrunkRectCollapsesToCenter()
    {
        var rect = new Rect(10, 20, 4, 6).Shrink(5);

        Assert.Equal(new Rect(12, 23, 0, 0), rect);
    }

    [Fact]
    public void D6_ShrinkHV_NeverReturnsNegativeSize()
    {
        var rect = new Rect(0, 0, 2, 2).Shrink(3, 3);

        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }

    [Fact]
    public void D9_DrawText_WideCharacterOccupiesTwoColumns()
    {
        var buf = new Buffer(5, 1);

        buf.DrawText(0, 0, "日x");

        Assert.Equal('日', buf.Get(0, 0).Char);
        Assert.Equal('x', buf.Get(2, 0).Char);
    }

    [Fact]
    public void D9_DrawText_DoesNotSplitSurrogatePairs()
    {
        var buf = new Buffer(5, 1);

        buf.DrawText(0, 0, "👍x");

        Assert.False(char.IsSurrogate(buf.Get(1, 0).Char));
        Assert.Equal('x', buf.Get(2, 0).Char);
    }

    [Fact]
    public void D10_DrawText_KeepsExistingBackground()
    {
        var buf = new Buffer(3, 1);
        buf.FillRect(buf.Area, new Cell(' ', ConsoleColor.Gray, ConsoleColor.Blue));

        buf.DrawText(0, 0, "x", ConsoleColor.White);

        Assert.Equal(new Cell('x', ConsoleColor.White, ConsoleColor.Blue), buf.Get(0, 0));
    }

    [Fact]
    public void D10_DrawBox_KeepsExistingBackground()
    {
        var buf = new Buffer(3, 3);
        buf.FillRect(buf.Area, new Cell(' ', ConsoleColor.Gray, ConsoleColor.Blue));

        buf.DrawBox(buf.Area, ConsoleColor.White);

        Assert.Equal(new Cell('┌', ConsoleColor.White, ConsoleColor.Blue), buf.Get(0, 0));
    }

    [Fact]
    public void D10_EmptyCell_DoesNotHardcodeBlackBackground()
    {
        Assert.NotEqual(new Cell(' ', ConsoleColor.Gray, ConsoleColor.Black), Cell.Empty);
    }

    [Fact]
    public void D14_DrawText_ReplacesControlCharacters()
    {
        var buf = new Buffer(6, 1);

        buf.DrawText(0, 0, "\x1b[31mX");

        Assert.Equal('�', buf.Get(0, 0).Char);
        Assert.Equal('[', buf.Get(1, 0).Char);
        Assert.Equal('X', buf.Get(5, 0).Char);
    }

    [Theory]
    [InlineData('\0')]
    [InlineData('\x7f')]
    [InlineData('\u009b')]
    public void D14_Cell_ReplacesControlCharacters(char c)
    {
        var buf = new Buffer(1, 1);

        buf.Set(0, 0, new Cell(c, ConsoleColor.Gray, ConsoleColor.Black));

        Assert.Equal('�', buf.Get(0, 0).Char);
    }
}
