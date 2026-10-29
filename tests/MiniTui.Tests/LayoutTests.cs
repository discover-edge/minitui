namespace MiniTui.Tests;

public class LayoutTests
{
    private static int[] Heights(params Constraint[] constraints) =>
        Layout.Split(new Rect(0, 0, 20, 10), Direction.Vertical, constraints).Select(r => r.Height).ToArray();

    // The required examples in §7.2 (vertical, height 10, no spacing)
    [Fact]
    public void ThreeFills_Split4_3_3() =>
        Assert.Equal([4, 3, 3], Heights(Constraint.Fill(), Constraint.Fill(), Constraint.Fill()));

    [Fact]
    public void LengthFillLength_Split3_6_1() =>
        Assert.Equal([3, 6, 1], Heights(Constraint.Length(3), Constraint.Fill(), Constraint.Length(1)));

    [Fact]
    public void OversizedLengths_ShrinkProportionally_5_0_5() =>
        Assert.Equal([5, 0, 5], Heights(Constraint.Length(8), Constraint.Fill(), Constraint.Length(8)));

    [Fact]
    public void PercentageAndFill_Split5_5() =>
        Assert.Equal([5, 5], Heights(Constraint.Percentage(50), Constraint.Fill()));

    [Fact]
    public void MinMaxFill_Split6_2_2() =>
        Assert.Equal([6, 2, 2], Heights(Constraint.Min(3), Constraint.Max(2), Constraint.Fill()));

    [Fact]
    public void Spacing_SeparatesRegions()
    {
        var layout = Layout.Vertical(new Rect(0, 0, 20, 10))
            .Add("a", Constraint.Length(2))
            .Add("b", Constraint.Fill())
            .Spacing(1)
            .Build();

        Assert.Equal(new Rect(0, 0, 20, 2), layout["a"]);
        Assert.Equal(new Rect(0, 3, 20, 7), layout["b"]);
    }

    [Fact]
    public void FillWeights_ShareByWeight() =>
        Assert.Equal([3, 7], Heights(Constraint.Fill(1), Constraint.Fill(2)));  // 3.33 / 6.67

    [Fact]
    public void Ratio_IsAFractionOfTheSpace() =>
        Assert.Equal([3, 7], Heights(Constraint.Ratio(1, 3), Constraint.Fill()));

    [Fact]
    public void MinsAlone_Overflowing_ShrinkProportionally() =>
        Assert.Equal([5, 5], Heights(Constraint.Min(8), Constraint.Min(8)));

    [Fact]
    public void FixedShrinkFirst_ThenMin()
    {
        Assert.Equal([2, 8], Heights(Constraint.Length(6), Constraint.Min(8)));
        Assert.Equal([0, 10], Heights(Constraint.Length(6), Constraint.Min(12)));
    }

    [Fact]
    public void OverflowRounding_TiesGoToTheLaterIndex() =>
        Assert.Equal([3, 3, 4], Heights(Constraint.Length(5), Constraint.Length(5), Constraint.Length(5)));

    [Theory]
    [InlineData(Flex.Start, new[] { 0, 3 })]
    [InlineData(Flex.End, new[] { 4, 7 })]
    [InlineData(Flex.Center, new[] { 2, 5 })]
    [InlineData(Flex.SpaceBetween, new[] { 0, 7 })]
    public void Flex_PlacesTheLeftover(Flex flex, int[] positions)
    {
        var layout = Layout.Vertical(new Rect(0, 0, 20, 10))
            .Add("a", Constraint.Length(3))
            .Add("b", Constraint.Length(3))
            .Flex(flex)
            .Build();

        Assert.Equal(positions, new[] { layout["a"].Y, layout["b"].Y });
    }

    [Fact]
    public void Flex_IsIgnored_WhenSomethingCanGrow()
    {
        var layout = Layout.Vertical(new Rect(0, 0, 20, 10))
            .Add("a", Constraint.Length(3))
            .Add("b", Constraint.Fill())
            .Flex(Flex.End)
            .Build();

        Assert.Equal(0, layout["a"].Y);
        Assert.Equal(7, layout["b"].Height);
    }

    [Fact]
    public void CappedMaxes_LeaveTheRestToFlex()
    {
        var layout = Layout.Horizontal(new Rect(0, 0, 10, 1))
            .Add("a", Constraint.Max(2))
            .Add("b", Constraint.Max(2))
            .Flex(Flex.Center)
            .Build();

        Assert.Equal(new Rect(3, 0, 2, 1), layout["a"]);
        Assert.Equal(new Rect(5, 0, 2, 1), layout["b"]);
    }

    [Fact]
    public void Margin_InsetsTheArea()
    {
        var layout = Layout.Horizontal(new Rect(0, 0, 20, 5))
            .Add("a", Constraint.Fill())
            .Margin(new Margin(1, 1, 2, 1))
            .Build();

        Assert.Equal(new Rect(1, 1, 17, 3), layout["a"]);
    }

    [Fact]
    public void TooMuchSpacing_IsReduced_SoRegionsStayInside()
    {
        var rects = Layout.Horizontal(new Rect(0, 0, 5, 1))
            .Add("a", Constraint.Fill()).Add("b", Constraint.Fill()).Add("c", Constraint.Fill())
            .Spacing(10)
            .Build();

        Assert.All(new[] { rects["a"], rects["b"], rects["c"] }, r => Assert.True(r.Right <= 5));
    }

    [Fact]
    public void Indexer_NamesTheMissingRegion()
    {
        var layout = Layout.Vertical(new Rect(0, 0, 10, 10)).Add("body", Constraint.Fill());

        var error = Assert.Throws<KeyNotFoundException>(() => layout["bdy"]);
        Assert.Contains("'bdy'", error.Message);
    }

    [Fact]
    public void Indexer_BuildsIfNeeded_AndBuildIsIdempotent()
    {
        var layout = Layout.Vertical(new Rect(0, 0, 10, 10)).Add("a", Constraint.Length(4)).Add("b", Constraint.Fill());
        var before = layout["b"];

        Assert.Equal(before, layout.Build().Build()["b"]);
        Assert.Equal(new Rect(0, 4, 10, 6), before);
    }

    [Fact]
    public void DuplicateName_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Layout.Vertical(new Rect(0, 0, 10, 10)).Add("a", Constraint.Fill()).Add("a", Constraint.Fill()));

    [Fact]
    public void Constraint_Validation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Constraint.Ratio(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Constraint.Fill(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Constraint.Length(-1));
        Assert.Equal(Constraint.Percentage(100), Constraint.Percentage(150));
        Assert.Equal(Constraint.Percentage(0), Constraint.Percentage(-5));
        Assert.Equal("Fill(2)", Constraint.Fill(2).ToString());
    }

    [Fact]
    public void Split_EmptyConstraintsOrArea_IsSafe()
    {
        Assert.Empty(Layout.Split(new Rect(0, 0, 10, 10), Direction.Vertical));
        Assert.Equal([0, 0], Heights2(new Rect(0, 0, 0, 0), Constraint.Length(3), Constraint.Fill()));

        static int[] Heights2(Rect area, params Constraint[] c) =>
            Layout.Split(area, Direction.Vertical, c).Select(r => r.Height).ToArray();
    }

    [Fact]
    public void Properties_Hold_For10000RandomLayouts()
    {
        var random = new Random(20261029);
        for (var run = 0; run < 10_000; run++)
        {
            var length = random.Next(0, 200);
            var spacing = random.Next(0, 4) == 0 ? random.Next(0, 6) : 0;
            var flex = (Flex)random.Next(0, 4);
            var constraints = Enumerable.Range(0, random.Next(1, 8)).Select(_ => RandomConstraint(random)).ToArray();
            var area = new Rect(random.Next(0, 50), 3, length, 2);

            var layout = Layout.Horizontal(area).Spacing(spacing).Flex(flex);
            for (var i = 0; i < constraints.Length; i++) layout.Add($"r{i}", constraints[i]);
            var rects = Enumerable.Range(0, constraints.Length).Select(i => layout[$"r{i}"]).ToArray();
            var again = Layout.Horizontal(area).Spacing(spacing).Flex(flex);
            for (var i = 0; i < constraints.Length; i++) again.Add($"r{i}", constraints[i]);

            var context = $"run {run}: length {length}, spacing {spacing}, {flex}, [{string.Join(", ", constraints.AsEnumerable())}]";
            var effectiveSpacing = constraints.Length > 1 ? Math.Min(spacing, length / (constraints.Length - 1)) : 0;
            var used = rects.Sum(r => r.Width) + effectiveSpacing * (constraints.Length - 1);

            Assert.True(rects.All(r => r.Width >= 0), context);
            Assert.True(used <= length, context);
            if (constraints.Any(c => c.ToString().StartsWith("Fill") || c.ToString().StartsWith("Min")))
                Assert.True(used == length, context);
            for (var i = 0; i < rects.Length; i++)
            {
                Assert.True(rects[i].X >= area.X && rects[i].Right <= area.Right, context);
                Assert.Equal(area.Y, rects[i].Y);
                if (i > 0) Assert.True(rects[i].X >= rects[i - 1].Right, context);
                Assert.Equal(rects[i], again[$"r{i}"]);
            }
        }
    }

    private static Constraint RandomConstraint(Random random) => random.Next(0, 6) switch
    {
        0 => Constraint.Length(random.Next(0, 120)),
        1 => Constraint.Percentage(random.Next(0, 101)),
        2 => Constraint.Ratio(random.Next(0, 5), random.Next(1, 5)),
        3 => Constraint.Min(random.Next(0, 60)),
        4 => Constraint.Max(random.Next(0, 60)),
        _ => Constraint.Fill(random.Next(1, 4)),
    };
}
