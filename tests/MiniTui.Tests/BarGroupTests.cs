using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Grouped and stacked bar charts and their legend (1.x spec §19.3).</summary>
public class BarGroupTests
{
    private static string[] Lines(Buffer buf) => [.. buf.ToLines().Select(l => l.TrimEnd())];

    private static BarGroup G(string label, params double[] values) =>
        new(label, [.. values.Select((v, i) => new Bar($"s{i}", v) { ValueText = "" })]);

    private static Buffer Draw(Func<BarChartBuilder, BarChartBuilder> configure, int width, int height)
    {
        var buf = new Buffer(width, height);
        configure(Widget.BarChart().Legend(LegendPosition.None)).Draw(buf, buf.Area);
        return buf;
    }

    // Grouped

    [Fact]
    public void Groups_DrawTheirBarsSideBySide_WithTheGroupGap_AndOneLabelEach()
    {
        var buf = Draw(c => c.Groups(G("ab", 4, 2), G("cd", 1, 3)).BarWidth(1).BarGap(0).GroupGap(2), 8, 5);

        Assert.Equal(
        [
            "█",
            "█    █",
            "██   █",
            "██  ██",
            "ab  cd",
        ], Lines(buf));
    }

    [Fact]
    public void ASeriesHasTheSameColorInEveryGroup_UnlessABarHasItsOwn()
    {
        var own = new Style(Color.Red);
        var groups = new[]
        {
            new BarGroup("a", [new Bar("x", 1), new Bar("y", 1)]),
            new BarGroup("b", [new Bar("x", 1), new Bar("y", 1) { Style = own }]),
        };
        var buf = Draw(c => c.Groups(groups).BarWidth(1).BarGap(0).GroupGap(1), 8, 3);
        var series = Theme.Current.ChartSeries;

        Assert.Equal(series[0].Fg, buf.Get(0, 0).Fg);
        Assert.Equal(series[1].Fg, buf.Get(1, 0).Fg);
        Assert.Equal(series[0].Fg, buf.Get(3, 0).Fg);
        Assert.Equal(Color.Red, buf.Get(4, 0).Fg);
    }

    [Fact]
    public void AGroupThatDoesntFit_IsLeftOut_Whole()
    {
        var buf = Draw(c => c.Groups(G("a", 1, 1), G("b", 1, 1)).BarWidth(2).BarGap(0).GroupGap(1), 7, 3);

        Assert.Equal("████", Lines(buf)[0]);
        Assert.Equal(" a", Lines(buf)[2][..2]);
    }

    [Fact]
    public void HorizontalGroups_AreConsecutiveRows_LabelledOnTheFirst()
    {
        var buf = Draw(c => c.Groups(G("ab", 4, 2), G("cd", 4)).Direction(Direction.Horizontal).BarGap(0).GroupGap(1), 7, 4);

        Assert.Equal(["ab ████", "   ██", "", "cd ████"], Lines(buf));
    }

    // Stacked

    [Fact]
    public void Stacked_PilesEachGroupIntoOneBar_WithWholeCellsBetweenSegments()
    {
        // Totals 4 and 2; 4 rows of bars: a cell per unit
        var buf = Draw(c => c.Groups(G("a", 1, 3), G("b", 1.4, 0.6)).Stacked().BarWidth(1).BarGap(1), 4, 5);
        var series = Theme.Current.ChartSeries;

        // Each stack's total is in its top cell
        Assert.Equal(["4", "█", "█ 2", "█ █", "a b"], Lines(buf));
        Assert.Equal(series[0].Fg, buf.Get(0, 3).Fg);
        Assert.Equal(series[1].Fg, buf.Get(0, 2).Fg);
        // 1.4 rounds to one cell, then 0.6 takes the next
        Assert.Equal(series[0].Fg, buf.Get(2, 3).Fg);
        Assert.Equal(series[1].Fg, buf.Get(2, 2).Fg);
    }

    [Fact]
    public void TheTopOfAStack_IsInEighths()
    {
        // Total 2.5 of 4 rows over a max of 4: 2.5 cells, the top half a cell
        var buf = Draw(c => c.Groups(G("a", 2, 0.5)).Stacked().BarWidth(1).Max(4), 1, 5);

        Assert.Equal("▄", buf.Get(0, 1).Symbol);
        Assert.Equal(Theme.Current.ChartSeries[1].Fg, buf.Get(0, 1).Fg);
    }

    [Fact]
    public void ASegmentThatRoundsToNothing_IsntDrawn_ButCounts()
    {
        var buf = Draw(c => c.Groups(G("a", 0.2, 3.8), G("b", 4)).Stacked().BarWidth(1), 3, 5);

        Assert.Equal(Theme.Current.ChartSeries[1].Fg, buf.Get(0, 3).Fg);
        Assert.Equal("█", buf.Get(0, 1).Symbol);
    }

    [Fact]
    public void AStack_ShowsItsTotal_InItsTopCell()
    {
        var groups = new[] { new BarGroup("a", [new Bar("x", 2), new Bar("y", 2)]) };
        var buf = Draw(c => c.Groups(groups).Stacked().BarWidth(3), 3, 5);

        Assert.Equal("█4█", Lines(buf)[0]);
        Assert.True(buf.Get(1, 0).Modifiers.HasFlag(Modifiers.Reversed));
    }

    [Fact]
    public void StackedMax_IsTheLargestTotal_AndNegativesCountAsZero()
    {
        var buf = Draw(c => c.Groups(G("a", 1, -5, double.NaN, 1), G("b", 4)).Stacked().BarWidth(1), 3, 5);

        Assert.Equal(["  4", "  █", "2 █", "█ █", "a b"], Lines(buf));
    }

    [Fact]
    public void HorizontalStacks_GrowRight()
    {
        var buf = Draw(c => c.Groups(G("a", 2, 2)).Stacked().Direction(Direction.Horizontal), 6, 1);
        var series = Theme.Current.ChartSeries;

        // The total after the bar leaves it two cells
        Assert.Equal("a ██ 4", Lines(buf)[0]);
        Assert.Equal(series[0].Fg, buf.Get(2, 0).Fg);
        Assert.Equal(series[1].Fg, buf.Get(3, 0).Fg);
    }

    // The legend

    [Theory]
    [InlineData(LegendPosition.TopRight, 14, 0)]
    [InlineData(LegendPosition.TopLeft, 0, 0)]
    [InlineData(LegendPosition.BottomRight, 14, 6)]
    [InlineData(LegendPosition.BottomLeft, 0, 6)]
    public void TheLegend_NamesTheSeries_InACorner(LegendPosition position, int x, int y)
    {
        var groups = new[] { new BarGroup("a", [new Bar("GET", 1), new Bar("POST", 1)]) };
        var buf = new Buffer(22, 10);
        Widget.BarChart().Groups(groups).Legend(position).Draw(buf, buf.Area);

        Assert.Equal("┌", buf.Get(x, y).Symbol);
        Assert.Contains("GET", Lines(buf)[y + 1], StringComparison.Ordinal);
        Assert.Contains("POST", Lines(buf)[y + 2], StringComparison.Ordinal);
        Assert.Equal(Theme.Current.ChartSeries[1].Fg, buf.Get(x + 1, y + 2).Fg);
    }

    [Fact]
    public void TheLegend_IsLeftOut_WithNone_WhenTooBig_OrWithoutLabels()
    {
        var groups = new[] { new BarGroup("a", [new Bar("GET", 1), new Bar("POST", 1)]) };
        var unnamed = new[] { new BarGroup("a", [new Bar("", 1)]) };

        Assert.DoesNotContain("GET", string.Join("\n", Lines(Draw(c => c.Groups(groups), 22, 10))), StringComparison.Ordinal);
        foreach (var (configure, w, h) in new (Func<BarChartBuilder, BarChartBuilder>, int, int)[]
        {
            (c => c.Groups(groups).Legend(LegendPosition.TopRight), 14, 10),
            (c => c.Groups(unnamed).Legend(LegendPosition.TopRight), 22, 10),
        })
        {
            var buf = new Buffer(w, h);
            configure(Widget.BarChart()).Draw(buf, buf.Area);
            Assert.DoesNotContain("┌", string.Join("\n", Lines(buf)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PlainBars_AreUnchanged_AndBarsReplaceGroups()
    {
        var buf = Draw(c => c.Groups(G("a", 1)).Bars(new Bar("x", 1)).BarWidth(1), 3, 3);

        Assert.Equal(["█", "1", "x"], Lines(buf));
        Assert.Equal(Theme.Current.GaugeFilled.Fg, buf.Get(0, 0).Fg);
    }

    [Fact]
    public void NoSize_Throws()
    {
        for (var w = 0; w < 12; w++)
        for (var h = 0; h < 6; h++)
        foreach (var stacked in new[] { false, true })
        foreach (var direction in new[] { Direction.Vertical, Direction.Horizontal })
        {
            var buf = new Buffer(Math.Max(1, w), Math.Max(1, h));
            Widget.BarChart().Groups(G("a", 1, 2), G("b", 3)).Stacked(stacked).Direction(direction).Draw(buf, new Rect(0, 0, w, h));
        }
    }
}
