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
    private static string[] Draw(ScrollState state, Text? text = null, Alignment alignment = Alignment.Left, WrapMode wrap = WrapMode.None,
        int height = 3, BlockBuilder? block = null)
    {
        var buf = new CellBuffer(10, height);
        var paragraph = Widget.Paragraph().Text(text ?? Long).State(state).Wrap(wrap).Alignment(alignment);
        if (block is not null) paragraph.Block(block);
        paragraph.Draw(buf, buf.Area);
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
    public void TheArrows_Scroll8Columns()
    {
        var state = Sideways();
        Assert.Equal(10, state.ViewportWidth);
        Assert.Equal(36, state.ContentWidth);

        Assert.True(state.HandleKey(Key("right")));
        Assert.Equal(8, state.HorizontalOffset);
        Assert.Equal("89abcdefgh", Draw(state)[0]);

        Assert.True(state.HandleKey(Key("left")));
        Assert.Equal("0123456789", Draw(state)[0]);
    }

    [Fact]
    public void WithoutRoomForAScrollbar_AnEllipsisMarksEachEdgeTheRowContinuesPast()
    {
        var state = new ScrollState { ScrollsHorizontally = true };

        Assert.Equal("012345678…", Draw(state, height: 1)[0]);
        state.HorizontalOffset = 8;
        Assert.Equal("…9abcdefg…", Draw(state, height: 1)[0]);
        state.HorizontalOffset = 26;
        Assert.Equal("…rstuvwxyz", Draw(state, height: 1)[0]);
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
        Assert.Equal("qrstuvwxyz", Draw(state)[0]);
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

        Assert.Equal("89abcdefgh", lines[0]);
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
    public void TheHorizontalWheel_AndShiftOrAltWithTheWheel_Scroll8Columns()
    {
        var state = Sideways();

        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollRight)));
        Assert.Equal(8, state.HorizontalOffset);
        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollDown, ConsoleModifiers.Shift)));
        Assert.Equal(16, state.HorizontalOffset);
        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollLeft)));
        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollUp, ConsoleModifiers.Shift)));
        Assert.Equal(0, state.HorizontalOffset);

        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollDown, ConsoleModifiers.Alt)));
        Assert.Equal(8, state.HorizontalOffset);
        Assert.True(state.HandleMouse(Wheel(MouseAction.ScrollUp, ConsoleModifiers.Alt)));
        Assert.Equal(0, state.HorizontalOffset);

        state.HandleMouse(Wheel(MouseAction.ScrollDown));
        state.HandleMouse(Wheel(MouseAction.ScrollDown, ConsoleModifiers.Control));
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

        // "9abc" is columns 9–12; at offset 10, "abc" is in cells 0 to 2
        for (var x = 0; x < 3; x++) Assert.Equal(Theme.Current.Selection.Bg, buf.Get(x, 0).Bg);
        Assert.NotEqual(Theme.Current.Selection.Bg, buf.Get(3, 0).Bg);
    }

    // The scrollbar

    [Fact]
    public void AHorizontalScrollbar_TakesTheLastRow_WhenARowIsWiderThanTheView()
    {
        var state = new ScrollState { ScrollsHorizontally = true };

        var lines = Draw(state, $"short\n{Long}");

        Assert.Equal("short", lines[0].TrimEnd());
        Assert.Matches("^█+░+$", lines[2]);
        Assert.Equal(2, state.ViewportHeight);

        state.HorizontalOffset = 26;
        Assert.Matches("^░+█+$", Draw(state, $"short\n{Long}")[2]);
    }

    [Fact]
    public void NoScrollbar_WhenEveryRowFits()
    {
        var state = new ScrollState { ScrollsHorizontally = true };

        var lines = Draw(state, "short\nrows");

        Assert.Equal("", lines[2].TrimEnd());
        Assert.Equal(3, state.ViewportHeight);
    }

    [Fact]
    public void TheScrollbar_GoesOnAFreeBottomBorder_OrInsideWhenItHasATitle()
    {
        var state = new ScrollState { ScrollsHorizontally = true };

        var lines = Draw(state, height: 4, block: Widget.Block());
        Assert.Matches("^└█+░+┘$", lines[3]);
        Assert.Equal(2, state.ViewportHeight);

        lines = Draw(state, height: 4, block: Widget.Block().TitleBottom("t"));
        Assert.Matches("^│█+░+│$", lines[2]);
        Assert.Equal(1, state.ViewportHeight);
    }

    [Fact]
    public void WithBothScrollbars_AndNoBorders_TheCornerIsBlank()
    {
        var state = new ScrollState { ScrollsHorizontally = true };

        var lines = Draw(state, $"{Long}\n2\n3\n4");

        Assert.Matches("^[█░]{9} $", lines[2]);
        Assert.Contains(lines[0][^1], "█░");
        Assert.Equal(9, state.ViewportWidth);
        Assert.Equal(2, state.ViewportHeight);
    }

    [Fact]
    public void TheScrollbar_PagesOnTheTrack_AndItsThumbDrags()
    {
        var state = Sideways();

        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 9, 2)));
        Assert.Equal(10, state.HorizontalOffset);
        Draw(state);

        var thumb = Draw(state)[2].IndexOf('█', StringComparison.Ordinal);
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, thumb, 2));
        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 0, 2));
        Assert.Equal(0, state.HorizontalOffset);
        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 9, 2));
        Assert.Equal(26, state.HorizontalOffset);
        state.HandleMouse(new MouseEvent(MouseAction.Release, MouseButton.Left, 9, 2));
    }
}
