namespace MiniTui.Tests;

public class GeometryTests
{
    [Fact]
    public void IsEmpty()
    {
        Assert.True(new Rect(0, 0, 0, 5).IsEmpty);
        Assert.True(new Rect(0, 0, 5, -1).IsEmpty);
        Assert.False(new Rect(0, 0, 1, 1).IsEmpty);
    }

    [Fact]
    public void Shrink_NormalCase()
    {
        Assert.Equal(new Rect(2, 3, 6, 4), new Rect(1, 2, 8, 6).Shrink(1));
        Assert.Equal(new Rect(3, 2, 4, 6), new Rect(1, 2, 8, 6).Shrink(2, 0));
    }

    [Fact]
    public void Inner_AppliesEachSide()
    {
        Assert.Equal(new Rect(1, 2, 6, 4), new Rect(0, 0, 10, 10).Inner(new Margin(1, 2, 3, 4)));
    }

    [Fact]
    public void Inner_CollapsesOnlyTheAxisThatDoesNotFit()
    {
        Assert.Equal(new Rect(5, 1, 0, 8), new Rect(0, 0, 10, 10).Inner(new Margin(6, 1)));
    }

    [Fact]
    public void Margin_Constructors()
    {
        Assert.Equal(new Margin(2, 2, 2, 2), new Margin(2));
        Assert.Equal(new Margin(3, 1, 3, 1), new Margin(3, 1));
    }

    [Fact]
    public void Intersect()
    {
        Assert.Equal(new Rect(5, 5, 5, 5), new Rect(0, 0, 10, 10).Intersect(new Rect(5, 5, 10, 10)));
        Assert.True(new Rect(0, 0, 5, 5).Intersect(new Rect(10, 10, 5, 5)).IsEmpty);
        Assert.True(new Rect(0, 0, 5, 5).Intersect(new Rect(2, 2, -3, -3)).IsEmpty);
    }

    [Fact]
    public void Clamp_MovesInsideBounds()
    {
        var bounds = new Rect(0, 0, 20, 10);

        Assert.Equal(new Rect(15, 7, 5, 3), new Rect(18, 9, 5, 3).Clamp(bounds));
        Assert.Equal(new Rect(0, 0, 5, 3), new Rect(-4, -1, 5, 3).Clamp(bounds));
    }

    [Fact]
    public void Clamp_ShrinksToBounds()
    {
        Assert.Equal(new Rect(2, 2, 4, 4), new Rect(0, 0, 50, 50).Clamp(new Rect(2, 2, 4, 4)));
    }

    [Fact]
    public void RowsAndColumns()
    {
        var rect = new Rect(1, 2, 3, 2);

        Assert.Equal([new Rect(1, 2, 3, 1), new Rect(1, 3, 3, 1)], rect.Rows());
        Assert.Equal([new Rect(1, 2, 1, 2), new Rect(2, 2, 1, 2), new Rect(3, 2, 1, 2)], rect.Columns());
        Assert.Empty(new Rect(0, 0, -1, -1).Rows());
        Assert.Empty(new Rect(0, 0, -1, -1).Columns());
    }

    [Fact]
    public void Split_ClampsOffset()
    {
        var rect = new Rect(0, 0, 10, 4);

        Assert.Equal((new Rect(0, 0, 10, 4), new Rect(0, 4, 10, 0)), rect.SplitHorizontal(9));
        Assert.Equal((new Rect(0, 0, 0, 4), new Rect(0, 0, 10, 4)), rect.SplitVertical(-2));
    }
}
