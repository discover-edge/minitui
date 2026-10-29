// Regression tests for the baseline defects in docs/SPEC.md §2.

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
    public void D9_SetString_WideCharacterOccupiesTwoColumns()
    {
        var buf = new Buffer(5, 1);

        buf.SetString(0, 0, "日x", Style.Default);

        Assert.Equal("日", buf.Get(0, 0).Symbol);
        Assert.True(buf.Get(1, 0).IsContinuation);
        Assert.Equal("x", buf.Get(2, 0).Symbol);
    }

    [Fact]
    public void D9_SetString_DoesNotSplitSurrogatePairs()
    {
        var buf = new Buffer(5, 1);

        buf.SetString(0, 0, "👍x", Style.Default);

        Assert.Equal("👍", buf.Get(0, 0).Symbol);
        Assert.Equal("x", buf.Get(2, 0).Symbol);
    }

    [Fact]
    public void D10_SetString_KeepsExistingBackground()
    {
        var buf = new Buffer(3, 1);
        buf.FillRect(buf.Area, Cell.Empty with { Bg = Color.Blue });

        buf.SetString(0, 0, "x", new Style(Color.White));

        Assert.Equal(new Cell("x", Color.White, Color.Blue, Modifiers.None), buf.Get(0, 0));
    }

    [Fact]
    public void D10_Block_KeepsExistingBackground()
    {
        var buf = new Buffer(3, 3);
        buf.FillRect(buf.Area, Cell.Empty with { Bg = Color.Blue });

        Widget.Block().BorderStyle(new Style(Color.White)).Draw(buf, buf.Area);

        Assert.Equal(new Cell("┌", Color.White, Color.Blue, Modifiers.None), buf.Get(0, 0));
    }

    [Fact]
    public void D10_EmptyCell_DoesNotHardcodeBlackBackground()
    {
        Assert.Equal(Color.Reset, Cell.Empty.Bg);
    }

    [Fact]
    public void D14_SetString_ReplacesControlCharacters()
    {
        var buf = new Buffer(6, 1);

        buf.SetString(0, 0, "\x1b[31mX", Style.Default);

        Assert.Equal("\uFFFD", buf.Get(0, 0).Symbol);
        Assert.Equal("[", buf.Get(1, 0).Symbol);
        Assert.Equal("X", buf.Get(5, 0).Symbol);
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\x7f")]
    [InlineData("\u009b")]
    public void D14_Cell_ReplacesControlCharacters(string c)
    {
        var buf = new Buffer(1, 1);

        buf.Set(0, 0, Cell.Empty with { Symbol = c });

        Assert.Equal("\uFFFD", buf.Get(0, 0).Symbol);
    }
}
