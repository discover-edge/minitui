// Regression tests for D7, written against the 0.7 Add(name, int)/AddFill API.
#pragma warning disable CS0618

namespace MiniTui.Tests;

public class LayoutDefectTests
{
    [Fact]
    public void D7_FillRegions_NeverGetANegativeSize_WhenFixedSizesExceedTheSpace()
    {
        var layout = Layout.Vertical(new Rect(0, 0, 20, 10))
            .Add("header", 8)
            .AddFill("body")
            .Add("footer", 8)
            .Build();

        Assert.True(layout["body"].Height >= 0);
        Assert.True(layout["footer"].Bottom <= 10);
    }

    [Fact]
    public void D7_FillRemainder_IsDistributed()
    {
        var layout = Layout.Vertical(new Rect(0, 0, 20, 10))
            .AddFill("a")
            .AddFill("b")
            .AddFill("c")
            .Build();

        Assert.Equal([4, 3, 3], new[] { layout["a"].Height, layout["b"].Height, layout["c"].Height });
        Assert.Equal(10, layout["c"].Bottom);
    }
}
