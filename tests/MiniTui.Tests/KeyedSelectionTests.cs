namespace MiniTui.Tests;

/// <summary>A list or table's selection following its item by key (1.x spec §24.3).</summary>
public class KeyedSelectionTests
{
    private sealed record Item(int Id, string Name);

    private static List<Item> Items(params string[] names) => [.. names.Select((n, i) => new Item(i, n))];

    // A 10-row table without a block: the header, then 9 rows
    private static string[] Draw(SelectionState state, IReadOnlyList<Item> items, bool keyed = true)
    {
        var buf = new CellBuffer(12, 10);
        var table = Widget.Table().Header("Name").State(state);
        if (keyed) table.Rows(items, i => new Row(i.Name), i => i.Id);
        else table.Rows(items, i => new Row(i.Name));
        table.Draw(buf, buf.Area);
        return buf.ToLines();
    }

    [Fact]
    public void ARowInsertedAbove_LeavesTheSelectionOnItsItem()
    {
        var items = Items("a", "b", "c");
        var state = new SelectionState { Selected = 1 };
        Draw(state, items);
        Assert.Equal(1, state.SelectedKey);

        items.Insert(0, new Item(9, "new"));
        Draw(state, items);

        Assert.Equal(2, state.Selected);
        Assert.Equal(1, state.SelectedKey);
    }

    [Fact]
    public void AReSort_LeavesTheSelectionOnItsItem()
    {
        var items = Items("delta", "alpha", "charlie", "bravo");
        var state = new SelectionState { Selected = 0 };
        Draw(state, items);

        items.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        Draw(state, items);

        Assert.Equal("delta", items[state.Selected].Name);
    }

    [Fact]
    public void AFilterThatKeepsTheItem_FollowsIt_AndOneThatDropsIt_KeepsTheIndex()
    {
        var items = Items("a", "b", "c", "d");
        var state = new SelectionState { Selected = 2 };
        Draw(state, items);

        Draw(state, [.. items.Where(i => i.Name != "a")]);
        Assert.Equal(1, state.Selected);
        Assert.Equal(2, state.SelectedKey);

        Draw(state, [.. items.Where(i => i.Name is "a" or "b" or "d")]);
        Assert.Equal(1, state.Selected);
        Assert.Equal(1, state.SelectedKey);       // "b" took the place
    }

    [Fact]
    public void AMoveInTheSameFrame_Wins()
    {
        var items = Items("a", "b", "c");
        var state = new SelectionState { Selected = 1 };
        Draw(state, items);

        items.Insert(0, new Item(9, "new"));
        state.HandleKey(KeyBinding.Parse("down").ToKeyInfo(), items.Count);
        Draw(state, items);

        Assert.Equal(2, state.Selected);
        Assert.Equal(1, state.SelectedKey);       // by index: row 2 now holds "b"
    }

    [Fact]
    public void AClickInTheSameFrame_Wins()
    {
        var items = Items("a", "b", "c");
        var state = new SelectionState { Selected = 0 };
        Draw(state, items);

        items.Reverse();
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 2, 2) { ClickCount = 1 }, items.Count);
        Draw(state, items);

        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void TheItem_StaysOnItsScreenRow()
    {
        var items = Enumerable.Range(0, 50).Select(i => new Item(i, $"n{i}")).ToList();
        var state = new SelectionState { Selected = 20 };
        Draw(state, items);
        var row = state.Selected - state.Offset;

        items.InsertRange(0, Enumerable.Range(100, 5).Select(i => new Item(i, $"n{i}")));
        var lines = Draw(state, items);

        Assert.Equal(25, state.Selected);
        Assert.Equal(row, state.Selected - state.Offset);
        Assert.StartsWith("n20", lines[1 + row], StringComparison.Ordinal);
    }

    [Fact]
    public void SelectKey_SelectsTheItem_AndShowsIt()
    {
        var items = Enumerable.Range(0, 50).Select(i => new Item(i, $"n{i}")).ToList();
        var state = new SelectionState();
        Draw(state, items);

        state.SelectKey(40);
        Draw(state, items);
        Assert.Equal(40, state.Selected);
        Assert.InRange(state.Selected - state.Offset, 0, state.ViewportHeight - 1);

        state.SelectKey(999);
        Draw(state, items);
        Assert.Equal(40, state.Selected);
    }

    [Fact]
    public void WithoutAKey_TheSelectionStaysOnItsIndex()
    {
        var items = Items("a", "b", "c");
        var state = new SelectionState { Selected = 1 };
        Draw(state, items, keyed: false);
        Assert.Null(state.SelectedKey);

        items.Insert(0, new Item(9, "new"));
        Draw(state, items, keyed: false);

        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void AList_FollowsItsItemToo()
    {
        var items = Items("a", "b", "c");
        var state = new SelectionState { Selected = 2 };
        void Draw() => Widget.List().Items(items, i => new Line(i.Name), i => i.Id).State(state).Draw(new CellBuffer(10, 6), new Rect(0, 0, 10, 6));
        Draw();

        items.RemoveAt(0);
        Draw();

        Assert.Equal(1, state.Selected);
        Assert.Equal(2, state.SelectedKey);
    }

    [Fact]
    public void WhileNothingChanges_KeysAreReadOncePerDraw()
    {
        var items = Enumerable.Range(0, 1000).Select(i => new Item(i, $"n{i}")).ToList();
        var state = new SelectionState { Selected = 500 };
        var reads = 0;
        void Draw() => Widget.Table().Rows(items, i => new Row(i.Name), i => { reads++; return i.Id; }).State(state)
            .Draw(new CellBuffer(10, 5), new Rect(0, 0, 10, 5));
        Draw();
        reads = 0;

        Draw();

        Assert.Equal(2, reads);       // the item at Selected, to compare, and again to remember
    }
}
