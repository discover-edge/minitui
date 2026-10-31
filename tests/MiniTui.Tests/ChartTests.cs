using static MiniTui.Tests.Snapshot;
using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class CanvasTests
{
    private static Buffer Paint(int width, int height, Action<CanvasPainter> paint, Marker marker = Marker.Block,
        double xMin = 0, double xMax = 1, double yMin = 0, double yMax = 1)
    {
        var buf = new Buffer(width, height);
        Widget.Canvas().XBounds(xMin, xMax).YBounds(yMin, yMax).Marker(marker).Paint(paint).Draw(buf, buf.Area);
        return buf;
    }

    [Fact]
    public void Braille_CornersMapToTheCornerDots()
    {
        var buf = Paint(2, 1, p =>
        {
            p.Point(0, 1);
            p.Point(1, 0);
        }, Marker.Braille);

        // (xMin, yMax) is dot 1 of the first cell; (xMax, yMin) dot 8 of the last
        AssertLines(buf, "⠁⢀");
    }

    [Fact]
    public void Braille_EachDotSetsItsBit()
    {
        var buf = Paint(1, 1, p =>
        {
            for (var x = 0; x < 2; x++)
            for (var y = 0; y < 4; y++)
                p.Point(x, y);
        }, Marker.Braille, xMax: 1, yMax: 3);

        AssertLines(buf, "⣿");
    }

    [Fact]
    public void Braille_LeftColumnOnly()
    {
        var buf = Paint(1, 1, p =>
        {
            for (var y = 0; y < 4; y++) p.Point(0, y);
        }, Marker.Braille, xMax: 1, yMax: 3);

        AssertLines(buf, "⡇");
    }

    [Fact]
    public void HalfBlock_HasTwoDotsPerCell()
    {
        Assert.Equal("▀", Paint(1, 1, p => p.Point(0, 1), Marker.HalfBlock).ToLines()[0]);
        Assert.Equal("▄", Paint(1, 1, p => p.Point(0, 0), Marker.HalfBlock).ToLines()[0]);
        Assert.Equal("█", Paint(1, 1, p => { p.Point(0, 0); p.Point(0, 1); }, Marker.HalfBlock).ToLines()[0]);
    }

    [Fact]
    public void BlockAndDot_HaveOneDotPerCell()
    {
        AssertTrimmed(Paint(3, 2, p => { p.Point(0, 0); p.Point(1, 1); }), """
              █
            █
            """);
        AssertTrimmed(Paint(3, 2, p => p.Point(0.5, 1), Marker.Dot), """
             •

            """);
    }

    [Fact]
    public void DotSize_IsTheAreaTimesTheMarker()
    {
        int? width = null, height = null;
        Paint(3, 2, p => (width, height) = (p.DotWidth, p.DotHeight), Marker.Braille);
        Assert.Equal((6, 8), (width, height));

        Paint(3, 2, p => (width, height) = (p.DotWidth, p.DotHeight), Marker.HalfBlock);
        Assert.Equal((3, 4), (width, height));
    }

    [Fact]
    public void PointsOutsideTheBounds_OrNotFinite_AreSkipped()
    {
        var buf = Paint(3, 1, p =>
        {
            p.Points([(-0.1, 0), (1.1, 0), (0.5, 2), (double.NaN, 0), (0, double.PositiveInfinity)]);
            p.Line(double.NaN, 0, 1, 0);
            p.Circle(0.5, 0.5, double.NaN);
            p.Print(2, 0, "x");
        });

        AssertLines(buf, "   ");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    [InlineData(double.NaN, 1)]
    public void BoundsWithMaxNotAboveMin_DrawNothing(double min, double max)
    {
        var buf = new Buffer(3, 1);

        Widget.Canvas().XBounds(min, max).Marker(Marker.Block).Paint(p =>
        {
            p.Point(1, 0);
            p.Line(0, 0, 1, 1);
            p.Print(1, 0, "x");
        }).Draw(buf, buf.Area);

        AssertLines(buf, "   ");
    }

    [Fact]
    public void BasicGlyphs_DrawBrailleAsHalfBlocks()
    {
        var buf = new Buffer(2, 1);

        using (Glyphs.Use(basic: true)) Widget.Canvas().Paint(p => { p.Point(0, 1); p.Point(1, 0); }).Draw(buf, buf.Area);

        AssertLines(buf, "▀▄");
    }

    [Fact]
    public void Lines_HorizontalVerticalAndDiagonal()
    {
        AssertLines(Paint(3, 1, p => p.Line(0, 0, 1, 0)), "███");
        AssertTrimmed(Paint(3, 3, p => p.Line(0, 0, 1, 1)), """
              █
             █
            █
            """);
        AssertTrimmed(Paint(3, 3, p => p.Line(0.5, 0, 0.5, 1)), """
             █
             █
             █
            """);
    }

    [Fact]
    public void Lines_ShallowAndSteep()
    {
        AssertTrimmed(Paint(6, 2, p => p.Line(0, 0, 1, 1)), """
               ███
            ███
            """);
        AssertTrimmed(Paint(2, 6, p => p.Line(0, 0, 1, 1)), """
             █
             █
             █
            █
            █
            █
            """);
    }

    [Fact]
    public void Lines_AreClippedAtEachEdge()
    {
        AssertTrimmed(Paint(3, 3, p =>
        {
            p.Line(-10, 1, 10, 1);
            p.Line(1, -5, 1, 5);
        }, xMax: 2, yMax: 2), """
             █
            ███
             █
            """);

        // Entirely outside
        AssertTrimmed(Paint(3, 3, p => p.Line(-2, -2, -1, 5), xMax: 2, yMax: 2), "\n\n");
    }

    [Fact]
    public void Rectangle_IsAnOutline()
    {
        AssertLines(Paint(3, 3, p => p.Rectangle(0, 0, 2, 2), xMax: 2, yMax: 2), """
            ███
            █ █
            ███
            """);
    }

    [Fact]
    public void Circle_PassesThroughItsFourExtremes()
    {
        var buf = Paint(5, 5, p => p.Circle(0, 0, 2), xMin: -2, xMax: 2, yMin: -2, yMax: 2);

        Assert.Equal("█", buf.Get(4, 2).Symbol);
        Assert.Equal("█", buf.Get(0, 2).Symbol);
        Assert.Equal("█", buf.Get(2, 0).Symbol);
        Assert.Equal("█", buf.Get(2, 4).Symbol);
        Assert.Equal(" ", buf.Get(2, 2).Symbol);
    }

    [Fact]
    public void Circle_WithRadiusZeroIsAPoint_AndNegativeIsNothing()
    {
        AssertLines(Paint(3, 1, p => p.Circle(0.5, 0, 0)), " █ ");
        AssertLines(Paint(3, 1, p => p.Circle(0.5, 0, -1)), "   ");
    }

    [Fact]
    public void TheLaterShapeWins_InASharedCell()
    {
        var buf = Paint(1, 1, p =>
        {
            p.Point(0, 1, new Style(Color.Red));
            p.Point(1, 0, new Style(Color.Green));
        }, Marker.Braille);

        AssertLines(buf, "⢁");
        AssertStyle(buf, 0, 0, Color.Green, Color.Reset);
    }

    [Fact]
    public void Print_IsDrawnOverTheDots_AndClipped()
    {
        AssertLines(Paint(4, 1, p =>
        {
            p.Print(0, 0, "ab");
            p.Line(0, 0, 1, 0);
        }), "ab██");

        AssertLines(Paint(4, 1, p => p.Print(0.7, 0, new Line("xyz\x1b", new Style(Color.Red)))), "  xy");
    }

    [Fact]
    public void CanvasStyle_IsTheBackground_AndTheDefaultDotStyle()
    {
        var buf = new Buffer(2, 1);

        Widget.Canvas().Marker(Marker.Block).Style(new Style(Color.Yellow, Color.Blue)).Paint(p => p.Point(0, 0)).Draw(buf, buf.Area);

        AssertStyle(buf, 0, 0, Color.Yellow, Color.Blue);
        AssertStyle(buf, 1, 0, Color.Yellow, Color.Blue);
    }

    [Fact]
    public void Block_SurroundsTheCanvas()
    {
        var buf = new Buffer(4, 3);

        Widget.Canvas().Marker(Marker.Block).Block(Widget.Block()).Paint(p => p.Line(0, 0, 1, 0)).Draw(buf, buf.Area);

        AssertLines(buf, """
            ┌──┐
            │██│
            └──┘
            """);
    }

    [Fact]
    public void ACanvasPartlyOffTheBuffer_MapsOverItsWholeArea()
    {
        var buf = new Buffer(2, 1);

        // The canvas is four cells wide; x = 1 is its fourth cell, off the buffer
        Widget.Canvas().Marker(Marker.Block).Paint(p => p.Line(0, 0, 1, 0)).Draw(buf, new Rect(-2, 0, 4, 1));

        AssertLines(buf, "██");
    }
}

public class ChartTests
{
    private static readonly Axis X = new() { Min = 0, Max = 10, Labels = ["0", "10"] };
    private static readonly Axis Y = new() { Min = 0, Max = 1, Labels = ["0", "1"] };

    [Fact]
    public void Chart_DrawsAxesLabelsAndALine()
    {
        var buf = new Buffer(12, 6);

        Widget.Chart().Datasets(new Dataset("", [(0, 0), (10, 1)]) { Marker = Marker.Block })
            .XAxis(X).YAxis(Y).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
            1│        ██
             │     ███
             │  ███
            0│██
             └──────────
              0       10
            """);
    }

    [Fact]
    public void Scatter_DrawsThePointsOnly()
    {
        var buf = new Buffer(12, 6);

        Widget.Chart().Datasets(new Dataset("", [(0, 0), (10, 1)]) { Type = GraphType.Scatter, Marker = Marker.Block })
            .XAxis(X).YAxis(Y).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
            1│         █
             │
             │
            0│█
             └──────────
              0       10
            """);
    }

    [Fact]
    public void AutoBounds_AndDefaultLabels()
    {
        var buf = new Buffer(12, 6);

        Widget.Chart().Datasets(new Dataset("", [(0, 2), (4, 6)]), new Dataset("", [(1, 3), (double.PositiveInfinity, double.NaN)])).Draw(buf, buf.Area);

        var lines = buf.ToLines();
        Assert.Equal("6│", lines[0][..2]);
        Assert.Equal("4│", lines[1][..2]);
        Assert.Equal("2│", lines[3][..2]);
        Assert.Equal("  0   2    4", lines[5]);
    }

    [Theory]
    [InlineData(new[] { 5.0 }, "4", "6")]
    [InlineData(new double[0], "0", "1")]
    public void AutoBounds_ForOneValueOrNone(double[] values, string min, string max)
    {
        var buf = new Buffer(12, 6);

        Widget.Chart().Datasets(new Dataset("", [.. values.Select(v => (v, v))])).Draw(buf, buf.Area);

        var lines = buf.ToLines();
        Assert.Equal(max, lines[0].Split('│')[0].Trim());
        Assert.Equal(min, lines[3].Split('│')[0].Trim());
    }

    [Fact]
    public void AnAxisWithOnlyAMin_TakesTheMaxFromTheData()
    {
        var buf = new Buffer(14, 6);

        Widget.Chart().Datasets(new Dataset("", [(0, 3), (1, 7)])).YAxis(new Axis { Min = 0, LabelFormat = "0.0" }).Draw(buf, buf.Area);

        var lines = buf.ToLines();
        Assert.Equal("7.0│", lines[0][..4]);
        Assert.Equal("0.0│", lines[3][..4]);
    }

    [Fact]
    public void Titles_GoAboveTheYAxis_AndAtTheEndOfTheXAxis()
    {
        var buf = new Buffer(12, 7);

        Widget.Chart().XAxis(X with { Title = "time" }).YAxis(Y with { Title = "load" }).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
             load
            1│
             │
             │
            0│      time
             └──────────
              0       10
            """);
    }

    [Fact]
    public void Legend_ListsNamedDatasets_InTheChosenCorner()
    {
        var buf = new Buffer(40, 20);
        var none = new Axis { Labels = [] };

        Widget.Chart().Datasets(new Dataset("a", []), new Dataset("", []), new Dataset("bb", []) { Marker = Marker.Dot })
            .XAxis(none).YAxis(none).Draw(buf, buf.Area);

        // The plot is (1, 0, 39, 19); the box is six wide and four high
        var lines = buf.ToLines();
        Assert.Equal("┌────┐", lines[0][34..]);
        Assert.Equal("│⣿ a │", lines[1][34..]);
        Assert.Equal("│• bb│", lines[2][34..]);
        AssertStyle(buf, 35, 1, Color.Cyan, Color.Reset);
        AssertStyle(buf, 35, 2, Color.Magenta, Color.Reset);
    }

    [Theory]
    [InlineData(LegendPosition.TopLeft, 1, 0)]
    [InlineData(LegendPosition.BottomRight, 35, 16)]
    [InlineData(LegendPosition.BottomLeft, 1, 16)]
    public void Legend_Corners(LegendPosition position, int x, int y)
    {
        var buf = new Buffer(40, 20);
        var none = new Axis { Labels = [] };

        Widget.Chart().Datasets(new Dataset("a", [])).XAxis(none).YAxis(none).Legend(position).Draw(buf, buf.Area);

        Assert.Equal("┌", buf.Get(x, y).Symbol);
        Assert.Equal("a", buf.Get(x + 3, y + 1).Symbol);
    }

    [Fact]
    public void Legend_IsOmitted_WhenNoneOrTooBig()
    {
        var none = new Axis { Labels = [] };
        var big = new Buffer(40, 20);
        Widget.Chart().Datasets(new Dataset("a", [])).XAxis(none).YAxis(none).Legend(LegendPosition.None).Draw(big, big.Area);
        Assert.DoesNotContain(big.ToLines(), l => l.Contains('┌'));

        // The plot is 7×5 and the box 5×3
        var small = new Buffer(8, 6);
        Widget.Chart().Datasets(new Dataset("a", [])).XAxis(none).YAxis(none).Draw(small, small.Area);
        Assert.DoesNotContain(small.ToLines(), l => l.Contains('┌'));

        var fits = new Buffer(11, 7);
        Widget.Chart().Datasets(new Dataset("a", [])).XAxis(none).YAxis(none).Draw(fits, fits.Area);
        Assert.Contains(fits.ToLines(), l => l.Contains('┌'));
        small = new Buffer(10, 5);
        Widget.Chart().Datasets(new Dataset("a", [])).XAxis(none).YAxis(none).Draw(small, small.Area);
        Assert.DoesNotContain(small.ToLines(), l => l.Contains('┌'));
    }

    [Fact]
    public void SeriesStyles_ComeFromTheTheme_WrappingAround()
    {
        var buf = new Buffer(12, 6);
        var datasets = Enumerable.Range(0, 7).Select(i => new Dataset("", i == 6 ? [(5, 0.5)] : [])).ToArray();

        Widget.Chart().Datasets(datasets).XAxis(X).YAxis(Y).Draw(buf, buf.Area);

        var dot = Enumerable.Range(2, 10).SelectMany(x => Enumerable.Range(0, 4).Select(y => (x, y)))
            .Single(c => buf.Get(c.x, c.y).Symbol != " ");
        AssertStyle(buf, dot.x, dot.y, Color.Cyan, Color.Reset);
    }

    [Fact]
    public void ADatasetStyle_OverridesTheSeries()
    {
        var buf = new Buffer(12, 6);

        Widget.Chart().Datasets(new Dataset("", [(5, 0.5)]) { Style = new Style(Color.Red), Marker = Marker.Block })
            .XAxis(X).YAxis(Y).Draw(buf, buf.Area);

        var dot = Enumerable.Range(2, 10).SelectMany(x => Enumerable.Range(0, 4).Select(y => (x, y)))
            .Single(c => buf.Get(c.x, c.y).Symbol == "█");
        AssertStyle(buf, dot.x, dot.y, Color.Red, Color.Reset);
    }

    [Fact]
    public void SmallAreas_DropTheTitlesFirst_ThenTheLabels_ThenTheAxes()
    {
        Axis Titled(Axis a) => a with { Title = "t" };
        string[] Draw(int width, int height)
        {
            var buf = new Buffer(width, height);
            Widget.Chart().Datasets(new Dataset("", [(0, 0), (10, 1)]) { Marker = Marker.Block })
                .XAxis(Titled(X)).YAxis(Titled(Y)).Draw(buf, buf.Area);
            return buf.ToLines();
        }

        // 3×3: no room for the Y title row; the labels and axes stay
        Assert.Equal(["0│█", " └─", "  0"], Draw(3, 3));

        // 2×2: no room for the labels
        Assert.Equal(["│█", "└─"], Draw(2, 2));

        // 1×1: the plot only
        Assert.Equal(["█"], Draw(1, 1));
    }
}

public class BarChartTests
{
    [Fact]
    public void Vertical_BarsGrowUp_WithLabelsBelow()
    {
        var buf = new Buffer(7, 3);

        Widget.BarChart().Bars(new Bar("A", 1) { ValueText = "" }, new Bar("B", 2) { ValueText = "" }).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
                ███
            ███ ███
             A   B
            """);
    }

    [Fact]
    public void Vertical_ValuesAreInTheBottomRow_ReversedOnAFullBar()
    {
        var buf = new Buffer(7, 3);

        Widget.BarChart().Bars(new Bar("A", 1), new Bar("B", 0)).Max(1).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
            ███
            █1█  0
             A   B
            """);
        AssertStyle(buf, 1, 1, Color.Green, Color.Reset, Modifiers.Reversed);
        AssertStyle(buf, 5, 1, Color.Reset, Color.Reset);
    }

    [Fact]
    public void Vertical_ValuesThatDontFit_AreHidden()
    {
        var buf = new Buffer(3, 2);

        Widget.BarChart().Bars(new Bar("A", 1234) { }).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
            ███
             A
            """);
    }

    [Fact]
    public void Vertical_GrowsInEighths()
    {
        var buf = new Buffer(4, 2);

        Widget.BarChart().BarWidth(1).BarGap(0).Max(8)
            .Bars(new Bar("", 1) { ValueText = "" }, new Bar("", 4) { ValueText = "" }, new Bar("", 7) { ValueText = "" }, new Bar("", 8) { ValueText = "" })
            .Draw(buf, buf.Area);

        Assert.Equal("▁▄▇█", buf.ToLines()[0]);
    }

    [Fact]
    public void Vertical_BasicGlyphs_RoundToHalfAndFull()
    {
        var buf = new Buffer(4, 2);

        using (Glyphs.Use(basic: true))
        {
            Widget.BarChart().BarWidth(1).BarGap(0).Max(8)
                .Bars(new Bar("", 2) { ValueText = "" }, new Bar("", 4) { ValueText = "" }, new Bar("", 6) { ValueText = "" }, new Bar("", 8) { ValueText = "" })
                .Draw(buf, buf.Area);
        }

        Assert.Equal(" ▄██", buf.ToLines()[0]);
    }

    [Fact]
    public void Vertical_LabelsAreTruncated_AndBarsThatDontFitAreDropped()
    {
        var buf = new Buffer(6, 2);

        Widget.BarChart().Bars(new Bar("Alpha", 1) { ValueText = "" }, new Bar("Beta", 1) { ValueText = "" }).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
            ███
            Al…
            """);
    }

    [Fact]
    public void NegativeAndNaN_DrawAsEmptyBars()
    {
        var buf = new Buffer(7, 2);

        Widget.BarChart().Bars(new Bar("A", -3), new Bar("B", double.NaN)).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
            -3
             A   B
            """);
    }

    [Fact]
    public void Horizontal_BarsGrowRight_WithValuesAfter()
    {
        var buf = new Buffer(10, 2);

        Widget.BarChart().Direction(Direction.Horizontal).BarGap(0).Bars(new Bar("a", 4), new Bar("bb", 2)).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
            a  █████ 4
            bb ██▌ 2
            """);
    }

    [Fact]
    public void Horizontal_GapsAndTruncatedLabels()
    {
        var buf = new Buffer(9, 3);

        Widget.BarChart().Direction(Direction.Horizontal).ValueFormat("0.0")
            .Bars(new Bar("long label", 1), new Bar("b", 0.5)).Draw(buf, buf.Area);

        AssertTrimmed(buf, """
            lo… █ 1.0

            b   ▌ 0.5
            """);
    }

    [Fact]
    public void Horizontal_BasicGlyphs_RoundToHalfAndFull()
    {
        var buf = new Buffer(4, 1);

        using (Glyphs.Use(basic: true))
            Widget.BarChart().Direction(Direction.Horizontal).Max(16).Bars(new Bar("", 6) { ValueText = "" }).Draw(buf, buf.Area);

        // 12 eighths of a 4-cell bar: one full cell and a half
        Assert.Equal("█▌  ", buf.ToLines()[0]);
    }

    [Fact]
    public void Horizontal_ValuesAreHidden_WhenThereIsNoRoomForThem()
    {
        var buf = new Buffer(6, 1);

        Widget.BarChart().Direction(Direction.Horizontal).Bars(new Bar("a", 1) { ValueText = "1000" }).Draw(buf, buf.Area);

        AssertLines(buf, "a ████");
    }

    [Fact]
    public void ABarStyle_OverridesTheGaugeStyle()
    {
        var buf = new Buffer(3, 2);

        Widget.BarChart().Bars(new Bar("A", 1) { Style = new Style(Color.Red), ValueText = "" }).Draw(buf, buf.Area);

        AssertStyle(buf, 0, 0, Color.Red, Color.Reset);
    }
}

public class ChartThemeTests
{
    [Fact]
    public void ChartSeries_DefaultsToSixColors_AndMonochromeHasNone()
    {
        Assert.Equal([Color.Cyan, Color.Yellow, Color.Magenta, Color.Green, Color.Blue, Color.Red],
            Theme.Default.ChartSeries.Select(s => s.Fg!.Value));
        Assert.All(Theme.Monochrome.ChartSeries, s => Assert.Equal(Style.Default, s));
        Assert.Same(Theme.Default.ChartSeries, new Theme().ChartSeries);
    }

    [Fact]
    public void AnEmptySeriesList_DrawsWithTheTextStyle()
    {
        var buf = new Buffer(12, 6);

        using (Theme.Use(Theme.Default with { ChartSeries = [] }))
        {
            Widget.Chart().Datasets(new Dataset("", [(5, 0.5)]) { Marker = Marker.Block })
                .XAxis(new Axis { Min = 0, Max = 10 }).YAxis(new Axis { Min = 0, Max = 1 }).Draw(buf, buf.Area);
        }

        Assert.Contains(buf.ToLines(), l => l.Contains('█'));
    }
}
