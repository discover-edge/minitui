using System.Globalization;

namespace MiniTui.Tests;

/// <summary>Label counts, formatters, time axes, ticks, grid lines and area charts (1.x spec §19.4–§19.6).</summary>
public class ChartAxisTests
{
    private static string[] Lines(CellBuffer buf) => [.. buf.ToLines().Select(l => l.TrimEnd())];

    private static CellBuffer Draw(Func<ChartBuilder, ChartBuilder> configure, int width, int height)
    {
        var buf = new CellBuffer(width, height);
        configure(Widget.Chart().Legend(LegendPosition.None)).Draw(buf, buf.Area);
        return buf;
    }

    // Labels

    [Fact]
    public void LabelCount_SpreadsThatManyLabels()
    {
        var buf = Draw(c => c.XAxis(new Axis { Min = 0, Max = 4, LabelCount = 5 }).YAxis(new Axis { Min = 0, Max = 1, LabelCount = 2 }), 20, 5);

        Assert.Equal("  0   1   2    3   4", Lines(buf)[^1]);
        Assert.Equal("1│", Lines(buf)[0]);
        Assert.Equal("0│", Lines(buf)[2]);
    }

    [Fact]
    public void LabelCount_IsAtLeastTwo()
    {
        var buf = Draw(c => c.XAxis(new Axis { Min = 0, Max = 9, LabelCount = 0 }).YAxis(new Axis { Labels = [] }), 12, 3);

        // The empty Y labels leave the axis column, so the plot starts at column 1
        Assert.Equal(" 0         9", Lines(buf)[^1]);
    }

    [Fact]
    public void AFormatter_ReplacesTheLabelFormat()
    {
        var buf = Draw(c => c.XAxis(new Axis { Min = 0, Max = 10, LabelFormat = "0.00", Formatter = v => $"<{v}>" }).YAxis(new Axis { Labels = [] }), 20, 3);

        Assert.Equal(" <0>     <5>    <10>", Lines(buf)[^1]);
    }

    [Theory]
    [InlineData(-90, "-1:30")]
    [InlineData(-0.4, "0:00")]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(3725, "2:05")]
    public void Duration_FormatsSeconds_WithAMinusBeforeNegatives(double seconds, string label)
    {
        Assert.Equal(label, Axis.Duration().Formatter!(seconds));
    }

    [Fact]
    public void Duration_TakesAFormat_AndIgnoresWhatItCantFormat()
    {
        Assert.Equal("1:02:05", Axis.Duration(@"h\:mm\:ss").Formatter!(3725));
        Assert.Equal("", Axis.Duration().Formatter!(double.NaN));
        Assert.Equal("", Axis.Duration().Formatter!(double.MaxValue));
    }

    [Fact]
    public void Time_FormatsTheLocalTime()
    {
        var time = new DateTimeOffset(2026, 11, 3, 12, 34, 56, TimeSpan.Zero);

        var label = Axis.Time().Formatter!(ChartTime.ToValue(time));

        Assert.Equal(time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), label);
        Assert.Equal(time.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Axis.Time("yyyy-MM-dd").Formatter!(ChartTime.ToValue(time)));
    }

    [Fact]
    public void ChartTime_RoundTrips()
    {
        var time = new DateTimeOffset(2026, 11, 3, 12, 34, 56, TimeSpan.FromHours(2));

        Assert.Equal(time.UtcDateTime, ChartTime.FromValue(ChartTime.ToValue(time)).UtcDateTime);
        Assert.Equal(0, ChartTime.ToValue(DateTimeOffset.UnixEpoch));
        Assert.Equal(90, ChartTime.ToValue(TimeSpan.FromMinutes(1.5)));
    }

    [Fact]
    public void TimeAxes_WorkWithWith()
    {
        var axis = Axis.Duration() with { Title = "ago", Min = -60, Max = 0 };
        var buf = Draw(c => c.XAxis(axis).YAxis(new Axis { Labels = [] }), 20, 4);

        Assert.Equal(" -1:00  -0:30   0:00", Lines(buf)[^1]);
    }

    // Ticks and grid lines

    [Fact]
    public void Ticks_MarkTheAxes_AtTheLabels_AndTheCornerStays()
    {
        var buf = Draw(c => c.XAxis(new Axis { Min = 0, Max = 4, LabelCount = 3, Ticks = true }).YAxis(new Axis { Min = 0, Max = 1, Ticks = true }), 11, 6);
        var lines = Lines(buf);

        // The Y axis is in column 3, beside labels "0", "0.5" and "1" right-aligned; a 4-row plot puts the middle on row 1
        Assert.Equal(["  1┤", "0.5┤", "   │", "  0┤", "   └┬──┬──┬", "    0  2  4"], lines);
    }

    [Fact]
    public void Grid_DrawsDottedLinesAcrossThePlot_ButNotBesideTheAxes()
    {
        var buf = Draw(c => c.XAxis(new Axis { Min = 0, Max = 4, Labels = ["", "", ""], Grid = true })
            .YAxis(new Axis { Min = 0, Max = 2, Labels = ["", "", ""], Grid = true }), 8, 6);

        // A 7×4 plot from column 1 (the empty X labels keep their row): grid columns at 4 and 7, grid rows at 1 and 0;
        // the first column and the bottom row, beside the axes, have none
        Assert.Equal(["│┈┈┈┼┈┈┼", "│┈┈┈┼┈┈┼", "│   ┊  ┊", "│   ┊  ┊", "└───────", ""], Lines(buf));
    }

    [Fact]
    public void TheGrid_IsBehindTheData_AndMuted()
    {
        var data = new Dataset("", [(0, 1), (4, 1)]) { Marker = Marker.Block };
        var buf = Draw(c => c.Datasets(data).XAxis(new Axis { Min = 0, Max = 4, Labels = ["", "", ""], Grid = true })
            .YAxis(new Axis { Min = 0, Max = 2, Labels = ["", "", ""], Grid = true }), 8, 6);

        // y = 1 on a 4-row plot of 0..2 is row 2 (rounded), over the grid columns
        Assert.Equal("│███████", Lines(buf)[2]);
        Assert.Equal("┊", buf.Get(4, 3).Symbol);
        Assert.Equal(Theme.Current.Border.Patch(Theme.Current.TextMuted).Fg, buf.Get(4, 3).Fg);
    }

    // Areas

    private static CellBuffer Area(IReadOnlyList<(double, double)> points, int width = 6, int height = 4)
    {
        var data = new Dataset("", points) { Type = GraphType.Area, Marker = Marker.Block };
        return Draw(c => c.Datasets(data).XAxis(new Axis { Min = 0, Max = width - 1, Labels = [] }).YAxis(new Axis { Min = 0, Max = height - 1, Labels = [] }), width + 1, height + 1);
    }

    [Fact]
    public void AnArea_FillsUnderARisingLine()
    {
        var lines = Lines(Area([(0, 0), (5, 3)]));

        Assert.Equal(
        [
            "│     █",
            "│   ███",
            "│ █████",
            "│██████",
            "└──────",
        ], lines);
    }

    [Fact]
    public void AnArea_FillsUnderAFallingLine_AndBetweenSeveralPoints()
    {
        var lines = Lines(Area([(0, 3), (2, 0), (5, 3)]));

        Assert.Equal("│█    █", lines[0]);
        Assert.Equal("│██████", lines[3]);
    }

    [Fact]
    public void AnArea_IsClippedAtTheEdges()
    {
        // Above the top until x = 3, at the bottom at x = 4 (where the line itself crosses row 2), below it after
        var lines = Lines(Area([(-5, 10), (2, 10), (10, -30)]));

        Assert.Equal(["│████", "│████", "│█████", "│█████", "└──────"], lines);
    }

    [Fact]
    public void ANaNPoint_BreaksTheArea()
    {
        var lines = Lines(Area([(0, 3), (1, 3), (2, double.NaN), (4, 3), (5, 3)]));

        Assert.Equal("│██  ██", lines[0]);
        Assert.Equal("│██  ██", lines[3]);
    }

    [Fact]
    public void LaterAreas_CoverEarlierOnes()
    {
        var big = new Dataset("", [(0, 3), (5, 3)]) { Type = GraphType.Area, Marker = Marker.Block, Style = new Style(Color.Red) };
        var small = new Dataset("", [(0, 1), (5, 1)]) { Type = GraphType.Area, Marker = Marker.Block, Style = new Style(Color.Blue) };
        var buf = Draw(c => c.Datasets(big, small).XAxis(new Axis { Min = 0, Max = 5, Labels = [] }).YAxis(new Axis { Min = 0, Max = 3, Labels = [] }), 7, 5);

        Assert.Equal(Color.Red, buf.Get(3, 0).Fg);
        Assert.Equal(Color.Blue, buf.Get(3, 3).Fg);
    }

    [Theory]
    [InlineData(Marker.Braille)]
    [InlineData(Marker.HalfBlock)]
    [InlineData(Marker.Dot)]
    public void AnArea_FillsWithEachMarker(Marker marker)
    {
        var data = new Dataset("", [(0, 1), (1, 1)]) { Type = GraphType.Area, Marker = marker };
        var buf = Draw(c => c.Datasets(data).XAxis(new Axis { Min = 0, Max = 1, Labels = [] }).YAxis(new Axis { Min = 0, Max = 1, Labels = [] }), 5, 4);

        Assert.All(Enumerable.Range(1, 4), x => Assert.NotEqual(" ", buf.Get(x, 2).Symbol));
    }

    [Fact]
    public void NoSize_Throws()
    {
        var area = new Dataset("a", [(0, 0), (1, 2), (2, 1)]) { Type = GraphType.Area };
        var axis = new Axis { Ticks = true, Grid = true, LabelCount = 4 };
        for (var w = 0; w < 14; w++)
        for (var h = 0; h < 8; h++)
        {
            var buf = new CellBuffer(Math.Max(1, w), Math.Max(1, h));
            Widget.Chart().Datasets(area).XAxis(axis).YAxis(axis with { Formatter = v => v.ToString("0", CultureInfo.InvariantCulture) }).Draw(buf, new Rect(0, 0, w, h));
        }
    }
}
