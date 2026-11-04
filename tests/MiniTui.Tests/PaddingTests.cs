namespace MiniTui.Tests;

/// <summary>Blocks pad their content inside side borders by default (1.x spec §24.2).</summary>
public class PaddingTests
{
    private static readonly Rect Area = new(0, 0, 10, 4);

    [Fact]
    public void ABorderedBlock_PadsAColumnInsideEachSide()
    {
        Assert.Equal(new Rect(2, 1, 6, 2), Widget.Block().Inner(Area));
    }

    [Theory]
    [InlineData(Borders.None, 0, 0, 10, 4)]
    [InlineData(Borders.Top, 0, 1, 10, 3)]
    [InlineData(Borders.Top | Borders.Bottom, 0, 1, 10, 2)]
    [InlineData(Borders.Left, 2, 0, 8, 4)]
    [InlineData(Borders.Right, 0, 0, 8, 4)]
    public void OnlyASideWithABorder_IsPadded(Borders borders, int x, int y, int width, int height)
    {
        Assert.Equal(new Rect(x, y, width, height), Widget.Block().Borders(borders).Inner(Area));
    }

    [Theory]
    [InlineData(6, 2)]  // 4 inside the borders: padded would leave 2
    [InlineData(4, 2)]
    public void ANarrowBlock_IsntPadded(int width, int inside)
    {
        Assert.Equal(new Rect(1, 1, inside + width - 4, 2), Widget.Block().Inner(new Rect(0, 0, width, 4)));
    }

    [Fact]
    public void FiveInsideTheBorders_IsPadded()
    {
        Assert.Equal(new Rect(2, 1, 3, 2), Widget.Block().Inner(new Rect(0, 0, 7, 4)));
    }

    [Fact]
    public void Padding_ReplacesTheDefault()
    {
        Assert.Equal(new Rect(1, 1, 8, 2), Widget.Block().Padding(new Margin(0)).Inner(Area));
        Assert.Equal(new Rect(3, 2, 4, 0), Widget.Block().Padding(new Margin(2, 1)).Inner(Area));
    }

    // Row widgets

    private static void AssertHighlightedRow(CellBuffer buf, int y, int from, int to)
    {
        var bg = Theme.Current.Selection.Bg;
        Assert.NotEqual(bg, buf.Get(from - 1, y).Bg);
        for (var x = from; x < to; x++) Assert.Equal(bg, buf.Get(x, y).Bg);
        Assert.NotEqual(bg, buf.Get(to, y).Bg);
    }

    [Fact]
    public void AListsHighlight_SpansThePadding_BorderToBorder()
    {
        var buf = new CellBuffer(10, 4);
        var state = new SelectionState();

        Widget.List().Items(["one", "two"]).State(state).Focused().Draw(buf, buf.Area);

        Assert.Equal("│ one    │", buf.ToLines()[1]);
        AssertHighlightedRow(buf, 1, 1, 9);
    }

    [Fact]
    public void ATablesHighlight_SpansThePadding_AndClicksThereSelect()
    {
        var buf = new CellBuffer(12, 5);
        var state = new SelectionState();

        Widget.Table().Header("Name").Rows([new Row("a"), new Row("b")]).State(state).Block(Widget.Block()).Focused().Draw(buf, buf.Area);

        Assert.Equal("│ Name     │", buf.ToLines()[1]);
        AssertHighlightedRow(buf, 2, 1, 11);
        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 1, 3) { ClickCount = 1 }, 2));
        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void ATableWithoutABlock_IsntPadded()
    {
        var buf = new CellBuffer(10, 3);

        Widget.Table().Header("Name").Rows([new Row("a")]).Draw(buf, buf.Area);

        Assert.StartsWith("Name", buf.ToLines()[0], StringComparison.Ordinal);
    }

    private sealed record Node(string Name);

    [Fact]
    public void ATreesHighlight_SpansThePadding()
    {
        var buf = new CellBuffer(12, 4);
        var state = new TreeState<Node>(_ => [], n => n.Name) { Roots = [new Node("a"), new Node("b")] };

        Widget.Tree(state).Focused().Draw(buf, buf.Area);

        AssertHighlightedRow(buf, 1, 1, 11);
    }

    // Text inputs

    [Fact]
    public void ATextFieldsStyle_SpansThePadding_AndAClickThereGoesToTheNearestColumn()
    {
        var state = new TextFieldState("hello");
        var buf = new CellBuffer(12, 3);

        Widget.TextField().State(state).Block(Widget.Block()).Style(new Style(Bg: Color.Blue)).Draw(buf, buf.Area);

        Assert.Equal("│ hello    │", buf.ToLines()[1]);
        Assert.Equal(Color.Blue, buf.Get(1, 1).Bg);
        Assert.Equal(Color.Blue, buf.Get(10, 1).Bg);
        Assert.NotEqual(Color.Blue, buf.Get(0, 1).Bg);
        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 1, 1) { ClickCount = 1 }));
        Assert.Equal(0, state.Cursor);
        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 10, 1) { ClickCount = 1 }));
        Assert.Equal(5, state.Cursor);
        Assert.False(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 0, 1) { ClickCount = 1 }));
    }

    [Fact]
    public void ATextArea_AClickInTheLeftPadding_GoesToTheRowsStart()
    {
        var state = new TextAreaState("one\ntwo") { Cursor = new TextPosition(0, 3) };
        var buf = new CellBuffer(12, 4);

        Widget.TextArea().State(state).Block(Widget.Block()).Draw(buf, buf.Area);

        Assert.Equal("│ two      │", buf.ToLines()[2]);
        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 1, 2) { ClickCount = 1 }));
        Assert.Equal(new TextPosition(1, 0), state.Cursor);
    }
}
