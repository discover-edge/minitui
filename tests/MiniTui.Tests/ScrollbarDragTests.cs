using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Dragging scrollbars (1.x spec §14.4) and sortable table headers (§14.5).</summary>
public class ScrollbarDragTests
{
    private static MouseEvent M(MouseAction action, int x, int y, MouseButton button = MouseButton.Left) => new(action, button, x, y);

    private static readonly string[] Items = [.. Enumerable.Range(0, 40).Select(i => $"Item {i}")];

    // 40 items in 10 rows: the thumb is 2 cells on a 10-cell track (rows 1–10, column 19), with 8 cells to move over 30 offsets
    private static Buffer DrawList(SelectionState state)
    {
        var buf = new Buffer(20, 12);
        Widget.List().Items(Items).State(state).Draw(buf, new Rect(0, 0, 20, 12));
        return buf;
    }

    [Theory]
    [InlineData(1, 10, 30)]   // the top of the thumb to the bottom
    [InlineData(2, 9, 26)]    // grabbed by its lower half: the thumb starts a row above the pointer
    [InlineData(1, 5, 15)]    // half way
    [InlineData(1, 40, 30)]   // past the end
    public void DraggingTheThumb_ScrollsTheList(int pressY, int dragY, int offset)
    {
        var state = new SelectionState();
        DrawList(state);

        Assert.True(state.HandleMouse(M(MouseAction.Press, 19, pressY), Items.Length));
        Assert.True(state.HandleMouse(M(MouseAction.Drag, 5, dragY), Items.Length));
        DrawList(state);

        Assert.Equal(offset, state.Offset);
    }

    [Fact]
    public void Dragging_KeepsTheSelection_UntilAKeyMovesIt()
    {
        var state = new SelectionState { Selected = 3 };
        DrawList(state);

        state.HandleMouse(M(MouseAction.Press, 19, 1), Items.Length);
        state.HandleMouse(M(MouseAction.Drag, 19, 9), Items.Length);
        DrawList(state);
        Assert.Equal(30, state.Offset);
        Assert.Equal(3, state.Selected);

        // A key moves the selection and brings it back into view
        state.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false), Items.Length);
        DrawList(state);
        Assert.Equal(4, state.Selected);
        Assert.Equal(4, state.Offset);
    }

    [Fact]
    public void TheRelease_EndsTheDrag()
    {
        var state = new SelectionState();
        DrawList(state);

        state.HandleMouse(M(MouseAction.Press, 19, 1), Items.Length);
        Assert.True(state.HandleMouse(M(MouseAction.Release, 19, 1), Items.Length));
        Assert.False(state.HandleMouse(M(MouseAction.Drag, 19, 9), Items.Length));
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void APressOnTheTrack_PagesTowardsIt()
    {
        var state = new SelectionState();
        DrawList(state);

        Assert.True(state.HandleMouse(M(MouseAction.Press, 19, 8), Items.Length));
        DrawList(state);
        Assert.Equal(10, state.Offset);
        Assert.Equal(0, state.Selected);

        // The thumb is now at row 3; a press above it pages back
        Assert.True(state.HandleMouse(M(MouseAction.Press, 19, 1), Items.Length));
        DrawList(state);
        Assert.Equal(0, state.Offset);
        Assert.False(state.HandleMouse(M(MouseAction.Drag, 19, 9), Items.Length));
    }

    [Fact]
    public void AScrollbarInsideTheArea_DragsToo()
    {
        // A paragraph without a block draws its bar in its own last column
        var state = new ScrollState();
        var text = string.Join("\n", Items);
        void Draw() => Widget.Paragraph().Text(text).State(state).Draw(new Buffer(20, 12), new Rect(0, 1, 20, 10));
        Draw();

        Assert.True(state.HandleMouse(M(MouseAction.Press, 19, 1)));
        state.HandleMouse(M(MouseAction.Drag, 19, 9));
        Draw();

        Assert.Equal(30, state.Offset);
    }

    [Fact]
    public void TheWheelScrolls_AndRowClicksSelect()
    {
        var state = new SelectionState();
        DrawList(state);

        state.HandleMouse(M(MouseAction.ScrollDown, 5, 5, MouseButton.None), Items.Length);
        DrawList(state);
        Assert.Equal(1, state.Offset);
        Assert.Equal(0, state.Selected);

        state.HandleMouse(M(MouseAction.Press, 5, 4), Items.Length);
        DrawList(state);
        Assert.Equal(4, state.Selected);
        Assert.Equal(1, state.Offset);

        // Past the end, the wheel stops
        for (var i = 0; i < 50; i++) state.HandleMouse(M(MouseAction.ScrollDown, 5, 5, MouseButton.None), Items.Length);
        Assert.Equal(30, state.Offset);
    }

    [Fact]
    public void WithoutAScrollbar_PressesOnTheEdgeDoNothingNew()
    {
        var state = new SelectionState();
        var buf = new Buffer(20, 12);
        Widget.List().Items(Items[..5]).State(state).Block(Widget.Block()).Draw(buf, new Rect(0, 0, 20, 12));

        Assert.False(state.HandleMouse(M(MouseAction.Press, 19, 1), 5));
    }

    // Paragraph

    [Fact]
    public void AParagraphsThumb_Drags_AndItsTrackPages()
    {
        var state = new ScrollState();
        var text = string.Join("\n", Items);
        void Draw() => Widget.Paragraph().Text(text).State(state).Block(Widget.Block()).Draw(new Buffer(20, 12), new Rect(0, 0, 20, 12));
        Draw();

        state.HandleMouse(M(MouseAction.Press, 19, 1));
        state.HandleMouse(M(MouseAction.Drag, 19, 5));
        Draw();
        Assert.Equal(15, state.Offset);

        state.HandleMouse(M(MouseAction.Release, 19, 5));
        state.HandleMouse(M(MouseAction.Press, 19, 10));
        Draw();
        Assert.Equal(25, state.Offset);
    }

    // Tree

    private sealed record Node(string Name);

    [Fact]
    public void ATreesThumb_Drags_AndTheSelectionStays()
    {
        var state = new TreeState<Node>(_ => [], n => n.Name) { Roots = [.. Items.Select(i => new Node(i))] };
        var changes = 0;
        state.SelectionChanged += (_, _) => changes++;
        void Draw() => Widget.Tree(state).Draw(new Buffer(20, 12), new Rect(0, 0, 20, 12));
        Draw();

        Assert.True(state.HandleMouse(M(MouseAction.Press, 19, 1)));
        state.HandleMouse(M(MouseAction.Drag, 19, 9));
        Draw();

        Assert.Equal(30, state.Offset);
        Assert.Equal("Item 0", state.Selected?.Name);
        Assert.Equal(0, changes);
    }

    // Table

    private static readonly (string Name, int Size)[] Files = [.. Enumerable.Range(0, 40).Select(i => ($"f{i}", i * 7 % 13))];

    // Header on row 1, 10 body rows from row 2 (the bar on the border, rows 2–11)
    private static void DrawTable(SelectionState state, Func<TableBuilder, TableBuilder>? configure = null)
    {
        var table = Widget.Table().Header("Name", "Size").Columns(Constraint.Length(8), Constraint.Length(8))
            .Rows(Files, f => new Row(f.Name, f.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .State(state).Block(Widget.Block());
        (configure?.Invoke(table) ?? table).Draw(new Buffer(20, 13), new Rect(0, 0, 20, 13));
    }

    [Fact]
    public void ATablesThumb_Drags()
    {
        var state = new SelectionState();
        DrawTable(state);

        Assert.True(state.HandleMouse(M(MouseAction.Press, 19, 2), Files.Length));
        state.HandleMouse(M(MouseAction.Drag, 19, 11), Files.Length);
        DrawTable(state);

        Assert.Equal(30, state.Offset);
    }

    [Fact]
    public void ASortableHeaderClick_SortsAndFlips()
    {
        var state = new SelectionState();
        var sorts = new List<string>();
        state.SortChanged += (_, _) => sorts.Add($"{state.SortColumn}{(state.SortDescending ? "v" : "^")}");
        DrawTable(state, t => t.Sortable());

        Assert.True(state.HandleMouse(M(MouseAction.Press, 11, 1), Files.Length));
        DrawTable(state, t => t.Sortable());
        state.HandleMouse(M(MouseAction.Press, 11, 1), Files.Length);
        DrawTable(state, t => t.Sortable());
        state.HandleMouse(M(MouseAction.Press, 2, 1), Files.Length);

        Assert.Equal(["1^", "1v", "0^"], sorts);
        Assert.Equal(0, state.Selected);
    }

    [Fact]
    public void OnlyTheListedColumns_AreSortable()
    {
        var state = new SelectionState();
        DrawTable(state, t => t.Sortable(1));

        Assert.False(state.HandleMouse(M(MouseAction.Press, 2, 1), Files.Length));
        Assert.Null(state.SortColumn);
        Assert.True(state.HandleMouse(M(MouseAction.Press, 11, 1), Files.Length));
        Assert.Equal(1, state.SortColumn);
    }

    [Fact]
    public void WithoutSortable_HeaderClicksDoNothing()
    {
        var state = new SelectionState();
        var sorts = 0;
        state.SortChanged += (_, _) => sorts++;
        DrawTable(state);

        Assert.False(state.HandleMouse(M(MouseAction.Press, 2, 1), Files.Length));
        Assert.Null(state.SortColumn);
        Assert.Equal(0, sorts);
    }

    [Fact]
    public void TheMarker_ShowsTheSort()
    {
        var state = new SelectionState();
        var buf = new Buffer(20, 13);
        state.ToggleSort(1);
        state.ToggleSort(1);
        Widget.Table().Header("Name", "Size").Columns(Constraint.Length(8), Constraint.Length(8))
            .Rows(Files, f => new Row(f.Name)).State(state).Block(Widget.Block()).Sortable().Draw(buf, new Rect(0, 0, 20, 13));

        Assert.Contains("Size ▼", buf.ToLines()[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ToggleSort_AscendsOnANewColumn()
    {
        var state = new SelectionState();

        state.ToggleSort(2);
        Assert.Equal((2, false), (state.SortColumn, state.SortDescending));
        state.ToggleSort(2);
        Assert.Equal((2, true), (state.SortColumn, state.SortDescending));
        state.ToggleSort(0);
        Assert.Equal((0, false), (state.SortColumn, state.SortDescending));
    }
}
