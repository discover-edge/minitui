// Phase 5 regression tests (docs/SPEC.md §2), written against the 0.7 static widgets so they failed on the
// code before the fix. The static functions are internal since 1.0. D8 and D13 were written against the
// removed 0.7 text-input helpers; their 1.0 equivalents are in StateTests.

using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class WidgetPhase5DefectTests
{
    [Fact]
    public void D11_List_ShowsSelectionWhenUnfocused()
    {
        var buf = new Buffer(10, 4);

        Widgets.List(buf, buf.Area, ["a", "b"], selectedIndex: 1, focused: false);

        Snapshot.AssertStyle(buf, 1, 2, Color.Black, Color.DarkGray);
    }

    [Fact]
    public void D15_Table_ScrollsToKeepTheSelectedRowVisible()
    {
        var buf = new Buffer(12, 5);
        var rows = Enumerable.Range(0, 20).Select(i => new[] { $"row{i}" }).ToArray();

        Widgets.Table(buf, buf.Area, ["Name"], rows, selectedRow: 15);

        var lines = buf.ToLines();
        Assert.Contains(lines, l => l.StartsWith("row15", StringComparison.Ordinal));
        Assert.Equal(Color.Cyan, buf.Get(0, Array.FindIndex(lines, l => l.StartsWith("row15", StringComparison.Ordinal))).Bg);
    }

    [Fact]
    public void D15_TableBuilder_SizesColumnsByConstraint()
    {
        var buf = new Buffer(20, 2);

        Widget.Table()
            .Header("Name", "St")
            .Columns(Constraint.Fill(), Constraint.Length(2))
            .ColumnSpacing(1)
            .Rows([new Row("alpha", "ok")])
            .Draw(buf, buf.Area);

        Snapshot.AssertLines(buf, """
            Name              St
            alpha             ok
            """);
    }

    public static TheoryData<string> StaticWidgets => [.. StaticDraws.Keys];

    private static readonly Dictionary<string, Action<Buffer, Rect>> StaticDraws = new()
    {
        ["Button"] = (b, r) => Widgets.Button(b, r, "OK", focused: true),
        ["Checkbox"] = (b, r) => Widgets.Checkbox(b, r.X, r.Y, "Enabled", isChecked: true, focused: true),
        ["List"] = (b, r) => Widgets.List(b, r, ["alpha", "beta", "gamma"], 1, focused: true, title: "Items"),
        ["Table"] = (b, r) => Widgets.Table(b, r, ["Name", "Value"], [["alpha", "1"], ["beta", "2"]], 1),
        ["StatusBar"] = (b, r) => Widgets.StatusBar(b, r, "left text", "right"),
        ["Modal"] = (b, _) => Widgets.Modal(b, "Title", ["a line that is much too long for the box"], "footer"),
        ["Confirm"] = (b, _) => Widgets.Confirm(b, "Delete everything?"),
    };

    [Theory]
    [MemberData(nameof(StaticWidgets))]
    public void D6_StaticWidgets_NeverThrow_OnTinyNegativeOrOffscreenAreas(string widget)
    {
        foreach (var (buf, area) in WidgetSweep.Cases())
            StaticDraws[widget](buf, area);
    }

    [Theory]
    [MemberData(nameof(StaticWidgets))]
    public void D10_StaticWidgets_NeverPaintABlackBackground(string widget)
    {
        var buf = new Buffer(30, 10);

        StaticDraws[widget](buf, buf.Area);

        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
            Assert.NotEqual(Color.Black, buf.Get(x, y).Bg);
    }
}

/// <summary>The W1 sweep: every size from 0×0 to 6×6 at several positions, plus negative and off-screen rects.</summary>
internal static class WidgetSweep
{
    public static IEnumerable<(Buffer Buffer, Rect Area)> Cases()
    {
        for (var w = 0; w <= 6; w++)
        for (var h = 0; h <= 6; h++)
        {
            yield return (new Buffer(w, h), new Rect(0, 0, w, h));
            yield return (new Buffer(6, 6), new Rect(0, 0, w, h));
            yield return (new Buffer(6, 6), new Rect(6 - w, 6 - h, w, h));
            yield return (new Buffer(6, 6), new Rect(3, 3, w, h));
        }

        Rect[] odd =
        [
            new(0, 0, -1, -1), new(-3, -3, 4, 4), new(-10, 2, 5, 2), new(2, -10, 2, 5),
            new(5, 5, 10, 10), new(100, 100, 5, 5), new(-100, -100, 5, 5),
            new(int.MaxValue - 2, 0, 5, 5), new(0, int.MinValue, 5, 5), new(0, 0, int.MaxValue, int.MaxValue),
            new(-5, -5, int.MaxValue, 3),
        ];
        foreach (var area in odd)
        {
            yield return (new Buffer(6, 6), area);
            yield return (new Buffer(0, 0), area);
        }
    }
}
