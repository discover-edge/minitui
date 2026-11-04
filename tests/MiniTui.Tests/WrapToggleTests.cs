namespace MiniTui.Tests;

/// <summary>A paragraph keeping its place and its selection when its rows are laid out again (1.x spec §23.3).</summary>
[Collection("Console")]
public sealed class WrapToggleTests : IDisposable
{
    // Ten lines of 23 columns: "line 0 has some words ."
    private static readonly string Lines = string.Join("\n", Enumerable.Range(0, 10).Select(i => $"line {i} has some words ."));

    public WrapToggleTests() => Clipboard.Reset();

    public void Dispose() => Clipboard.Reset();

    private static string[] Draw(ScrollState state, WrapMode wrap, int width = 12, int height = 4)
    {
        var buf = new CellBuffer(width, height);
        Widget.Paragraph().Text(Lines).Wrap(wrap).State(state).Draw(buf, buf.Area);
        return buf.ToLines();
    }

    [Fact]
    public void TogglingWrap_KeepsTheTopLine()
    {
        var state = new ScrollState { Offset = 5 };
        Assert.StartsWith("line 5", Draw(state, WrapMode.None)[0], StringComparison.Ordinal);

        Assert.StartsWith("line 5", Draw(state, WrapMode.Word)[0], StringComparison.Ordinal);
        Assert.True(state.Offset > 5);

        Assert.StartsWith("line 5", Draw(state, WrapMode.None)[0], StringComparison.Ordinal);
        Assert.Equal(5, state.Offset);
    }

    [Fact]
    public void InsideAWrappedLine_TheRowThatHoldsIt_GoesToTheTop()
    {
        var state = new ScrollState();
        Draw(state, WrapMode.Word, width: 12);
        state.Offset = 7;                   // line 2's second row, "some words ."
        Draw(state, WrapMode.Word, width: 12);

        Assert.StartsWith("line 2", Draw(state, WrapMode.Word, width: 30)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AResize_KeepsTheTopLine()
    {
        var state = new ScrollState();
        Draw(state, WrapMode.Word, width: 12);
        state.Offset = 9;
        Assert.StartsWith("line 3", Draw(state, WrapMode.Word, width: 12)[0], StringComparison.Ordinal);

        Assert.StartsWith("line 3", Draw(state, WrapMode.Word, width: 8)[0], StringComparison.Ordinal);
        Assert.StartsWith("line 3", Draw(state, WrapMode.Word, width: 30)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AtTheEnd_ItStaysAtTheEnd()
    {
        var state = new ScrollState { Offset = int.MaxValue };
        Draw(state, WrapMode.None);
        Draw(state, WrapMode.None);

        var lines = Draw(state, WrapMode.Word);

        Assert.Equal(state.ContentHeight - state.ViewportHeight, state.Offset);
        Assert.Equal(".", lines[^1].Trim(' ', '█', '░'));        // line 9's last row: "some words" / "."
    }

    [Fact]
    public void ScrollingAndTogglingInOneFrame_TheScrollWins()
    {
        var state = new ScrollState();
        Draw(state, WrapMode.None);

        state.Offset = 2;
        Draw(state, WrapMode.Word);

        Assert.Equal(2, state.Offset);
    }

    [Fact]
    public void ASelection_SurvivesTogglingWrap()
    {
        var state = new ScrollState { Selectable = true };
        Draw(state, WrapMode.None, width: 30);
        // "some" in line 1
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 11, 1) { ClickCount = 1 });
        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 15, 1));
        Assert.Equal("some", state.SelectedText);

        Draw(state, WrapMode.Word);
        Assert.Equal("some", state.SelectedText);
        Draw(state, WrapMode.None, width: 30);
        Assert.Equal("some", state.SelectedText);
    }

    [Fact]
    public void ASelectionAcrossAWrapPoint_CopiesAsTheRowsShown_AndWholeWhenUnwrapped()
    {
        var state = new ScrollState { Selectable = true };
        Draw(state, WrapMode.None, width: 30);
        // "has some" in line 0
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 7, 0) { ClickCount = 1 });
        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 15, 0));
        Assert.Equal("has some", state.SelectedText);

        Draw(state, WrapMode.Word, width: 8);      // "line 0" / "has" / "some" beside the scrollbar
        Assert.Equal("has\nsome", state.SelectedText);

        Draw(state, WrapMode.None, width: 30);
        Assert.Equal("has some", state.SelectedText);
    }

    [Fact]
    public void ASelectionOnLinesThatAreGone_IsCleared()
    {
        var state = new ScrollState { Selectable = true, Offset = 6 };
        Draw(state, WrapMode.None, width: 30);
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 0, 3) { ClickCount = 3 });
        Assert.StartsWith("line 9", state.SelectedText, StringComparison.Ordinal);

        var buf = new CellBuffer(30, 4);
        Widget.Paragraph().Text("one\ntwo").State(state).Draw(buf, buf.Area);

        Assert.False(state.HasSelection);
    }
}
