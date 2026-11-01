using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Drag reports, pointer capture and selecting with the mouse (1.x spec §10.7).</summary>
public class DragTests
{
    private sealed class Sink : IInputSink
    {
        public List<object> Events { get; } = [];
        public void Key(ConsoleKeyInfo key) => Events.Add(key);
        public void Paste(string text) => Events.Add(text);
        public void Mouse(MouseEvent mouse) => Events.Add(mouse);
    }

    private static List<object> Decode(string input)
    {
        var decoder = new AnsiInputDecoder(TimeSpan.FromMilliseconds(50));
        var sink = new Sink();
        decoder.Feed(System.Text.Encoding.Latin1.GetBytes(input), TimeSpan.Zero, sink);
        decoder.Expire(TimeSpan.FromSeconds(1), sink);
        return sink.Events;
    }

    private static MouseEvent M(MouseAction action, int x, int y, MouseButton button = MouseButton.Left, ConsoleModifiers modifiers = 0) =>
        new(action, button, x, y, modifiers);

    private static TextPosition P(int line, int index) => new(line, index);

    // Decoding

    [Theory]
    [InlineData("\x1b[<32;5;3M", MouseButton.Left)]
    [InlineData("\x1b[<33;5;3M", MouseButton.Middle)]
    [InlineData("\x1b[<34;5;3M", MouseButton.Right)]
    public void Sgr_MotionWithAButton_IsADrag(string input, MouseButton button)
    {
        Assert.Equal([M(MouseAction.Drag, 4, 2, button)], Decode(input));
    }

    [Fact]
    public void Sgr_Drag_KeepsModifiers()
    {
        Assert.Equal([M(MouseAction.Drag, 0, 0, modifiers: ConsoleModifiers.Shift | ConsoleModifiers.Control)], Decode("\x1b[<52;1;1M"));
    }

    [Fact]
    public void X10_MotionWithAButton_IsADrag_AndWithoutOne_IsDropped()
    {
        static string X10(int b, int x, int y) => "\x1b[M" + (char)(32 + b) + (char)(33 + x) + (char)(33 + y);

        Assert.Equal([M(MouseAction.Drag, 7, 1)], Decode(X10(32, 7, 1)));
        Assert.Equal(['x'], Decode(X10(35, 7, 1) + "x").Cast<ConsoleKeyInfo>().Select(k => k.KeyChar));
    }

    // Routing

    private sealed class Probe(string name, List<string> log) : Component
    {
        public override bool HandleMouse(MouseEvent mouse)
        {
            log.Add($"{name} {mouse.Action} {mouse.X},{mouse.Y}");
            return true;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    private sealed class Panes : Screen
    {
        public Panes(List<string> log)
        {
            Left = AddChild(new Probe("left", log));
            Right = AddChild(new Probe("right", log));
            Focus.Add(Left).Add(Right);
        }

        public Probe Left { get; }
        public Probe Right { get; }

        public bool FocusedLeft => Focus.IsFocused(Left);

        public void RemoveLeft() => RemoveChild(Left);

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            ctx.Render(Left, buf, new Rect(0, 0, 10, 5));
            ctx.Render(Right, buf, new Rect(10, 0, 10, 5));
        }
    }

    [Fact]
    public void Drags_GoToThePressTarget_EvenOverAnotherComponent()
    {
        var log = new List<string>();
        var screen = new Panes(log);
        using var h = new ComponentHarness(screen, 20, 5);
        h.Press("tab");

        h.Mouse(M(MouseAction.Press, 2, 2));
        h.Mouse(M(MouseAction.Drag, 15, 2));
        h.Mouse(M(MouseAction.Release, 15, 2));

        Assert.Equal(["left Press 2,2", "left Drag 15,2", "right Release 15,2"], log);
        Assert.True(screen.FocusedLeft);
    }

    [Fact]
    public void Drags_DoNotMoveFocus()
    {
        var log = new List<string>();
        var screen = new Panes(log);
        using var h = new ComponentHarness(screen, 20, 5);
        h.Mouse(M(MouseAction.Press, 12, 2));
        h.Press("tab");

        h.Mouse(M(MouseAction.Drag, 13, 2));

        Assert.True(screen.FocusedLeft);
        Assert.Equal("right Drag 13,2", log[^1]);
    }

    [Fact]
    public void Drags_AreClampedToTheScreen()
    {
        var log = new List<string>();
        using var h = new ComponentHarness(new Panes(log), 20, 5);

        h.Mouse(M(MouseAction.Press, 2, 2));
        h.Mouse(M(MouseAction.Drag, 40, 9));

        Assert.Equal("left Drag 19,4", log[^1]);
    }

    [Fact]
    public void Drags_WithoutAPress_OrAfterThePressTargetUnmounts_AreDropped()
    {
        var log = new List<string>();
        var screen = new Panes(log);
        using var h = new ComponentHarness(screen, 20, 5);

        h.Mouse(M(MouseAction.Drag, 2, 2));
        Assert.Empty(log);

        h.Mouse(M(MouseAction.Press, 2, 2));
        screen.RemoveLeft();
        h.Mouse(M(MouseAction.Drag, 3, 2));

        Assert.Equal(["left Press 2,2"], log);
    }

    [Fact]
    public void APressOnNothing_ClearsThePressTarget()
    {
        var log = new List<string>();
        var screen = new Panes(log);
        using var h = new ComponentHarness(screen, 30, 5);

        h.Mouse(M(MouseAction.Press, 2, 2));
        h.Mouse(M(MouseAction.Press, 25, 2));
        h.Mouse(M(MouseAction.Drag, 3, 2));

        Assert.Equal(["left Press 2,2"], log);
    }

    [Fact]
    public void OnMouse_SeesDrags()
    {
        var log = new List<string>();
        using var h = new ComponentHarness(new Panes(log), 20, 5);
        var seen = new List<MouseAction>();
        h.App.OnMouse(m =>
        {
            seen.Add(m.Action);
            return false;
        });

        h.Mouse(M(MouseAction.Press, 2, 2));
        h.Mouse(M(MouseAction.Drag, 3, 2));

        Assert.Equal([MouseAction.Press, MouseAction.Drag], seen);
    }

    [Fact]
    public void BuiltInHandlers_IgnoreDrags()
    {
        var selection = new SelectionState();
        var buf = new Buffer(10, 5);
        Widget.List().Items(["a", "b", "c"]).State(selection).Draw(buf, buf.Area);
        var scroll = new ScrollState();

        Assert.False(selection.HandleMouse(M(MouseAction.Drag, 1, 2), 3));
        Assert.False(scroll.HandleMouse(M(MouseAction.Drag, 1, 2)));
        Assert.False(M(MouseAction.Drag, 1, 2).IsClick);
    }

    // Text field

    private static TextFieldState DrawnField(string text, int width = 10)
    {
        var state = new TextFieldState(text);
        var buf = new Buffer(width + 2, 3);
        Widget.TextField().State(state).Block(Widget.Block()).Draw(buf, buf.Area);
        return state;
    }

    [Fact]
    public void Field_Press_PlacesTheCursor_AndShiftPress_Extends()
    {
        var state = DrawnField("hello world");

        Assert.True(state.HandleMouse(M(MouseAction.Press, 3, 1)));
        Assert.False(state.HasSelection);

        // Scrolled: the cursor at the end showed columns 2–11, so the cell at x 1 is column 2
        Assert.Equal(4, state.Cursor);

        Assert.True(state.HandleMouse(M(MouseAction.Press, 8, 1, modifiers: ConsoleModifiers.Shift)));
        Assert.Equal("o wor", state.SelectedText);
    }

    [Fact]
    public void Field_Drag_SelectsInBothDirections()
    {
        var state = DrawnField("abcdef");

        state.HandleMouse(M(MouseAction.Press, 3, 1));
        Assert.True(state.HandleMouse(M(MouseAction.Drag, 6, 1)));
        Assert.Equal("cde", state.SelectedText);

        Assert.True(state.HandleMouse(M(MouseAction.Drag, 1, 1)));
        Assert.Equal("ab", state.SelectedText);
        Assert.Equal(2, state.SelectionAnchor);

        state.HandleMouse(M(MouseAction.Drag, 30, 1));
        Assert.Equal("cdef", state.SelectedText);
    }

    [Fact]
    public void Field_DragPastTheLeftEdge_ScrollsAColumn()
    {
        var state = new TextFieldState("0123456789abcdef");
        var buf = new Buffer(6, 1);
        Widget.TextField().State(state).Draw(buf, buf.Area);
        Assert.Equal(11, state.ScrollOffset);

        state.HandleMouse(M(MouseAction.Press, 3, 0));
        state.HandleMouse(M(MouseAction.Drag, -1, 0));

        Assert.Equal(10, state.Cursor);
        Assert.Equal("abcd", state.SelectedText);
        Widget.TextField().State(state).Draw(buf, buf.Area);
        Assert.Equal(10, state.ScrollOffset);
    }

    [Fact]
    public void Field_DragAfterAPressOutside_IsNotHandled()
    {
        var state = DrawnField("abc");

        Assert.False(state.HandleMouse(M(MouseAction.Press, 0, 0)));
        Assert.False(state.HandleMouse(M(MouseAction.Drag, 2, 1)));

        state.HandleMouse(M(MouseAction.Press, 1, 1));
        Assert.False(state.HandleMouse(M(MouseAction.Release, 2, 1)));
        Assert.False(state.HandleMouse(M(MouseAction.Drag, 2, 1)));
        Assert.False(state.HandleMouse(M(MouseAction.Drag, 2, 1, MouseButton.Right)));
    }

    [Fact]
    public void Field_Press_OnAWideCharacter_GoesToItsStart()
    {
        var state = new TextFieldState("a中b");
        var buf = new Buffer(10, 1);
        Widget.TextField().State(state).Draw(buf, buf.Area);

        state.HandleMouse(M(MouseAction.Press, 2, 0));
        Assert.Equal(1, state.Cursor);

        state.HandleMouse(M(MouseAction.Press, 9, 0));
        Assert.Equal(3, state.Cursor);
    }

    // Text area

    private static TextAreaState DrawnArea(string text, int width, int height, WrapMode wrap = WrapMode.Word)
    {
        var state = new TextAreaState(text) { Cursor = default };
        Draw(state, width, height, wrap);
        return state;
    }

    private static void Draw(TextAreaState state, int width, int height, WrapMode wrap = WrapMode.Word)
    {
        var buf = new Buffer(width, height);
        Widget.TextArea().State(state).Wrap(wrap).Draw(buf, buf.Area);
    }

    [Fact]
    public void Area_Drag_SelectsAcrossLinesAndWrappedRows()
    {
        var state = DrawnArea("one two three\nfour", 8, 4);

        state.HandleMouse(M(MouseAction.Press, 1, 0));
        Assert.True(state.HandleMouse(M(MouseAction.Drag, 2, 1)));
        Assert.Equal("ne two th", state.SelectedText);

        state.HandleMouse(M(MouseAction.Drag, 7, 2));
        Assert.Equal("ne two three\nfour", state.SelectedText);

        state.HandleMouse(M(MouseAction.Drag, 0, 3));
        Assert.Equal(P(1, 4), state.Cursor);
    }

    [Fact]
    public void Area_ShiftPress_Extends()
    {
        var state = DrawnArea("abc\ndef", 10, 3);

        state.HandleMouse(M(MouseAction.Press, 1, 0));
        state.HandleMouse(M(MouseAction.Press, 2, 1, modifiers: ConsoleModifiers.Shift));

        Assert.Equal("bc\nde", state.SelectedText);
    }

    [Fact]
    public void Area_DragPastTheBottom_ScrollsARow()
    {
        var state = DrawnArea(string.Join('\n', Enumerable.Range(0, 10).Select(i => $"l{i}")), 10, 3);

        state.HandleMouse(M(MouseAction.Press, 1, 1));
        state.HandleMouse(M(MouseAction.Drag, 1, 5));

        Assert.Equal(1, state.ScrollOffset);
        Assert.Equal(P(3, 1), state.Cursor);
        Assert.Equal("1\nl2\nl", state.SelectedText);

        state.HandleMouse(M(MouseAction.Drag, 1, 5));
        Assert.Equal(P(4, 1), state.Cursor);
        Draw(state, 10, 3);
        Assert.Equal(2, state.ScrollOffset);
    }

    [Fact]
    public void Area_DragPastTheTop_ScrollsARow()
    {
        var state = new TextAreaState(string.Join('\n', Enumerable.Range(0, 10).Select(i => $"l{i}")));
        Draw(state, 10, 3);
        Assert.Equal(7, state.ScrollOffset);

        state.HandleMouse(M(MouseAction.Press, 2, 1));
        state.HandleMouse(M(MouseAction.Drag, 0, -1));

        Assert.Equal(6, state.ScrollOffset);
        Assert.Equal(P(6, 0), state.Cursor);
        Assert.Equal("l6\nl7\nl8", state.SelectedText);
    }

    [Fact]
    public void Area_DragWithoutWrapping_ScrollsSideways()
    {
        var state = new TextAreaState("0123456789");
        Draw(state, 5, 1, WrapMode.None);
        Assert.Equal(6, state.HorizontalOffset);

        state.HandleMouse(M(MouseAction.Press, 4, 0));
        state.HandleMouse(M(MouseAction.Drag, -1, 0));

        Assert.Equal(P(0, 5), state.Cursor);
        Assert.Equal("56789", state.SelectedText);
    }

    [Fact]
    public void Area_DragAfterAPressOnTheBorder_IsNotHandled()
    {
        var state = new TextAreaState("abc");
        var buf = new Buffer(10, 4);
        Widget.TextArea().State(state).Block(Widget.Block()).Draw(buf, buf.Area);

        Assert.False(state.HandleMouse(M(MouseAction.Press, 0, 0)));
        Assert.False(state.HandleMouse(M(MouseAction.Drag, 2, 1)));
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void Area_WheelOutsideTheArea_IsNotHandled()
    {
        var state = DrawnArea("a\nb\nc\nd", 5, 2);

        Assert.False(state.HandleMouse(M(MouseAction.ScrollDown, 1, 5, MouseButton.None)));
        Assert.True(state.HandleMouse(M(MouseAction.ScrollDown, 1, 1, MouseButton.None)));
    }

    // Through the app

    private sealed class FieldScreen : Screen
    {
        public FieldScreen()
        {
            Field = AddChild(new FieldComponent());
            Other = AddChild(new FieldComponent());
            Focus.Add(Other).Add(Field);
        }

        public FieldComponent Field { get; }
        public FieldComponent Other { get; }
        public bool FocusedField => Focus.IsFocused(Field);

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            ctx.Render(Other, buf, new Rect(0, 0, 10, 1));
            ctx.Render(Field, buf, new Rect(0, 2, 10, 1));
        }
    }

    private sealed class FieldComponent : Component
    {
        public TextFieldState State { get; } = new("abcdef");
        public override bool CapturesText => true;
        public override bool HandleMouse(MouseEvent mouse) => State.HandleMouse(mouse);
        public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
            Widget.TextField().State(State).Focused(ctx.Focused).Draw(buf, area);
    }

    [Fact]
    public void Dragging_ThroughTheApp_FocusesAndSelects_ThenCopies()
    {
        var screen = new FieldScreen();
        using var h = new ComponentHarness(screen, 20, 4);

        h.Mouse(M(MouseAction.Press, 1, 2));
        h.Mouse(M(MouseAction.Drag, 2, 1));
        h.Mouse(M(MouseAction.Drag, 4, 3));
        h.Mouse(M(MouseAction.Release, 4, 3));

        Assert.True(screen.FocusedField);
        Assert.Equal("bcd", screen.Field.State.SelectedText);
        Assert.False(screen.Other.State.HasSelection);
        Assert.Equal(Theme.Default.Selection.Bg, h.CellAt(2, 2).Bg);
    }
}
