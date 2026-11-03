namespace MiniTui.Tests;

/// <summary>Scrolling unwrapped paragraph text sideways (1.x spec §23.3).</summary>
[Collection("Console")]
public sealed class HorizontalScrollTests : IDisposable
{
    private const string Long = "0123456789abcdefghijklmnopqrstuvwxyz";

    public HorizontalScrollTests() => Clipboard.Reset();

    public void Dispose() => Clipboard.Reset();

    private static ConsoleKeyInfo Key(string binding) => KeyBinding.Parse(binding).ToKeyInfo();

    private static MouseEvent Wheel(MouseAction action, ConsoleModifiers modifiers = 0) => new(action, MouseButton.None, 2, 0, modifiers);

    // A 10 × 3 view without a block, so screen cells are text cells
    private static string[] Draw(ScrollState state, Text? text = null, Alignment alignment = Alignment.Left, WrapMode wrap = WrapMode.None)
    {
        var buf = new CellBuffer(10, 3);
        Widget.Paragraph().Text(text ?? Long).State(state).Wrap(wrap).Alignment(alignment).Draw(buf, buf.Area);
        return buf.ToLines();
    }

    private static ScrollState Sideways(Text? text = null)
    {
        var state = new ScrollState { ScrollsHorizontally = true };
        Draw(state, text);
        return state;
    }

    [Fact]
    public void Off_LongRowsAreCut_AndTheArrowsGoOn()
    {
        var state = new ScrollState();

        Assert.Equal("012345678…", Draw(state)[0]);
        Assert.False(state.HandleKey(Key("right")));
        Assert.False(state.HandleKey(Key("left")));
        Assert.Equal(0, state.HorizontalOffset);
        Assert.False(state.HandleMouse(Wheel(MouseAction.ScrollRight)));
    }

    [Fact]
    public void TheArrows_Scroll8Columns_WithAnEllipsisAtEachEdgeTheRowContinuesPast()
    {
        var state = Sideways();
        Assert.Equal(10, state.ViewportWidth);
        Assert.Equal(36, state.ContentWidth);

        Assert.True(state.HandleKey(Key("right")));
        Assert.Equal(8, state.HorizontalOffset);
        Assert.Equal("…9abcdefg…", Draw(state)[0]);

        Assert.True(state.HandleKey(Key("left")));
        Assert.Equal("012345678…", Draw(state)[0]);
    }

    [Fact]
    public void AtTheEdges_TheArrowsAreNotHandled()
    {
        var state = Sideways();

        Assert.False(state.HandleKey(Key("left")));
        for (var i = 0; i < 3; i++) Assert.True(state.HandleKey(Key("right")));
        Assert.True(state.HandleKey(Key("right")));     // 24, then the last 2 columns
        Assert.Equal(26, state.HorizontalOffset);
        Assert.False(state.HandleKey(Key("right")));
        Assert.Equal("…rstuvwxyz", Draw(state)[0]);
    }

    [Fact]
    public void HomeAndEnd_GoToTheFirstAndLastColumn()
    {
        var state = Sideways();

        Assert.True(state.HandleKey(Key("end")));
        Assert.Equal(26, state.HorizontalOffset);
        Assert.True(state.HandleKey(Key("home")));
        Assert.Equal(0, state.HorizontalOffset);
    }

    [Fact]
    public void TheOffset_IsClampedWhenDrawn()
    {
        var state = Sideways();

        state.HorizontalOffset = 100;
        Draw(state);
        Assert.Equal(26, state.HorizontalOffset);

        state.HorizontalOffset = -3;
        Draw(state);
        Assert.Equal(0, state.HorizontalOffset);
    }

    [Fact]
    public void RowsNarrowerThanTheView_KeepTheirAlignment()
    {
        var state = new ScrollState { ScrollsHorizontally = true, HorizontalOffset = 8 };

        var lines = Draw(state, $"{Long}\nabc", Alignment.Right);

        Assert.Equal("…9abcdefg…", lines[0]);
        Assert.Equal("       abc", lines[1]);
    }

    [Fact]
    public void WrappedText_DoesntScrollSideways()
    {
        var state = new ScrollState { ScrollsHorizontally = true, HorizontalOffset = 4 };

        // Four rows in three: the scrollbar takes the last column
        Assert.Equal("012345678█", Draw(state, wrap: WrapMode.Char)[0]);
        Assert.Equal(0, state.HorizontalOffset);
        Assert.False(state.HandleKey(Key("right")));
    }

    [Fact]
    public void TheHorizontalWheel_AndShiftWithTheWheel_Scroll8Columns()
    {
        var state = Sideways();

        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollRight)));
        Assert.Equal(8, state.HorizontalOffset);
        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollDown, ConsoleModifiers.Shift)));
        Assert.Equal(16, state.HorizontalOffset);
        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollLeft)));
        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollUp, ConsoleModifiers.Shift)));
        Assert.Equal(0, state.HorizontalOffset);

        state.HandleMouse(Wheel(MouseAction.ScrollDown));
        Assert.Equal(0, state.HorizontalOffset);
    }

    [Fact]
    public void ASelection_MadeWhileScrolled_KeepsItsText()
    {
        var state = new ScrollState { ScrollsHorizontally = true, Selectable = true, HorizontalOffset = 8 };
        Draw(state);

        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 1, 0) { ClickCount = 1 });
        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 5, 0));
        Assert.Equal("9abc", state.SelectedText);

        state.HandleKey(Key("home"));
        Draw(state);
        Assert.Equal("9abc", state.SelectedText);
    }

    [Fact]
    public void DraggingPastTheRightEdge_ScrollsAColumn_AndSelectsToTheEdge()
    {
        var state = new ScrollState { ScrollsHorizontally = true, Selectable = true };
        Draw(state);

        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 2, 0) { ClickCount = 1 });
        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 14, 0));

        Assert.Equal(1, state.HorizontalOffset);
        Assert.Equal("23456789a", state.SelectedText);

        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, -1, 0));
        Assert.Equal(0, state.HorizontalOffset);
        Assert.Equal("01", state.SelectedText);
    }

    [Fact]
    public void TheSelection_IsDrawnWhereTheTextIs()
    {
        var state = new ScrollState { ScrollsHorizontally = true, Selectable = true, HorizontalOffset = 8 };
        Draw(state);
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 1, 0) { ClickCount = 1 });
        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 5, 0));
        state.HorizontalOffset = 10;

        var buf = new CellBuffer(10, 3);
        Widget.Paragraph().Text(Long).State(state).Draw(buf, buf.Area);

        // "9abc" is columns 9–12; at offset 10, "a" is under the left … and "bc" in cells 1 and 2
        for (var x = 0; x < 3; x++) Assert.Equal(Theme.Current.Selection.Bg, buf.Get(x, 0).Bg);
        Assert.NotEqual(Theme.Current.Selection.Bg, buf.Get(3, 0).Bg);
    }
}
