using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Mouse decoding, routing and the built-in handling (1.x spec §4.6–4.8).</summary>
public class MouseTests
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

    private static MouseEvent M(MouseAction action, MouseButton button, int x, int y, ConsoleModifiers modifiers = 0) =>
        new(action, button, x, y, modifiers);

    // Decoding

    [Theory]
    [InlineData("\x1b[<0;1;1M", MouseAction.Press, MouseButton.Left, 0, 0)]
    [InlineData("\x1b[<1;10;5M", MouseAction.Press, MouseButton.Middle, 9, 4)]
    [InlineData("\x1b[<2;80;24M", MouseAction.Press, MouseButton.Right, 79, 23)]
    [InlineData("\x1b[<0;3;4m", MouseAction.Release, MouseButton.Left, 2, 3)]
    [InlineData("\x1b[<64;3;4M", MouseAction.ScrollUp, MouseButton.None, 2, 3)]
    [InlineData("\x1b[<65;3;4M", MouseAction.ScrollDown, MouseButton.None, 2, 3)]
    [InlineData("\x1b[<66;3;4M", MouseAction.ScrollLeft, MouseButton.None, 2, 3)]
    [InlineData("\x1b[<67;3;4M", MouseAction.ScrollRight, MouseButton.None, 2, 3)]
    [InlineData("\x1b[<0;300;200M", MouseAction.Press, MouseButton.Left, 299, 199)]
    public void Sgr_Decodes(string input, MouseAction action, MouseButton button, int x, int y)
    {
        Assert.Equal([M(action, button, x, y)], Decode(input));
    }

    [Theory]
    [InlineData(4, ConsoleModifiers.Shift)]
    [InlineData(8, ConsoleModifiers.Alt)]
    [InlineData(16, ConsoleModifiers.Control)]
    [InlineData(4 + 8 + 16, ConsoleModifiers.Shift | ConsoleModifiers.Alt | ConsoleModifiers.Control)]
    public void Sgr_Modifiers(int bits, ConsoleModifiers modifiers)
    {
        Assert.Equal([M(MouseAction.Press, MouseButton.Left, 0, 0, modifiers)], Decode($"\x1b[<{bits};1;1M"));
        Assert.Equal([M(MouseAction.ScrollDown, MouseButton.None, 0, 0, modifiers)], Decode($"\x1b[<{65 + bits};1;1M"));
    }

    [Theory]
    [InlineData("\x1b[<32;1;1M")]   // motion
    [InlineData("\x1b[<35;1;1M")]   // motion, no button
    [InlineData("\x1b[<128;1;1M")]  // extra buttons
    [InlineData("\x1b[<0;0;1M")]    // column 0
    [InlineData("\x1b[<0;1M")]      // no row
    [InlineData("\x1b[<;1;1M")]     // no button
    [InlineData("\x1b[<0;1;1;1M")]  // too many numbers
    public void Sgr_MotionAndMalformedReports_AreDropped(string input)
    {
        Assert.Equal(['x'], Decode(input + "x").Cast<ConsoleKeyInfo>().Select(k => k.KeyChar));
    }

    [Fact]
    public void X10_Decodes_IncludingReleaseAndTheWheel()
    {
        static string X10(int b, int x, int y) => "\x1b[M" + (char)(32 + b) + (char)(33 + x) + (char)(33 + y);

        var events = Decode(X10(0, 4, 2) + X10(3, 0, 0) + X10(65 + 16, 222, 222));

        Assert.Equal(
            [
                M(MouseAction.Press, MouseButton.Left, 4, 2),
                M(MouseAction.Release, MouseButton.None, 0, 0),
                M(MouseAction.ScrollDown, MouseButton.None, 222, 222, ConsoleModifiers.Control),
            ],
            events);
    }

    [Fact]
    public void AReportSplitAtEveryOffset_DecodesOnce()
    {
        var bytes = "a\x1b[<0;12;34Mb"u8.ToArray();
        for (var split = 0; split <= bytes.Length; split++)
        {
            var decoder = new AnsiInputDecoder(TimeSpan.FromMilliseconds(50));
            var sink = new Sink();
            decoder.Feed(bytes.AsSpan(0, split), TimeSpan.Zero, sink);
            decoder.Feed(bytes.AsSpan(split), TimeSpan.FromMilliseconds(10), sink);

            Assert.Equal(3, sink.Events.Count);
            Assert.Equal(M(MouseAction.Press, MouseButton.Left, 11, 33), sink.Events[1]);
        }
    }

    [Fact]
    public void Reports_StayInOrderWithKeysAndPastes_AndAreTextInsideAPaste()
    {
        var events = Decode("a\x1b[<0;1;1M\x1b[200~p\x1b[<0;1;1Mq\x1b[201~b");

        Assert.Equal(4, events.Count);
        Assert.Equal('a', ((ConsoleKeyInfo)events[0]).KeyChar);
        Assert.IsType<MouseEvent>(events[1]);
        Assert.Equal("p\x1b[<0;1;1Mq", events[2]);
        Assert.Equal('b', ((ConsoleKeyInfo)events[3]).KeyChar);
    }

    [Fact]
    public void MouseEvent_Helpers()
    {
        var click = M(MouseAction.Press, MouseButton.Left, 3, 4);

        Assert.True(click.IsClick);
        Assert.Equal(new Vec2(3, 4), click.Position);
        Assert.False(M(MouseAction.Release, MouseButton.Left, 0, 0).IsClick);
        Assert.False(M(MouseAction.Press, MouseButton.Right, 0, 0).IsClick);
    }

    // Routing

    /// <summary>Logs mouse events and handles them when told to.</summary>
    private sealed class Probe(string name, List<string> log, bool handles = false) : Component
    {
        public override bool HandleMouse(MouseEvent mouse)
        {
            log.Add($"{name} {mouse.Action}");
            return handles;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    /// <summary>Two probes side by side, rendered with ctx.Render, and an inner probe rendered without it.</summary>
    private sealed class TwoPanes : Screen
    {
        private readonly List<string> _log;

        public TwoPanes(List<string> log, bool leftHandles = false)
        {
            _log = log;
            Left = AddChild(new Probe("left", log, leftHandles));
            Right = AddChild(new Probe("right", log));
            Hidden = AddChild(new Probe("hidden", log));
            Label = AddChild(new Probe("label", log));
            Focus.Add(Left).Add(Right);
        }

        public Probe Left { get; }
        public Probe Right { get; }
        public Probe Hidden { get; }
        public Probe Label { get; } // not focusable
        public bool FocusedRight => Focus.IsFocused(Right);

        public override bool HandleMouse(MouseEvent mouse)
        {
            _log.Add($"screen {mouse.Action}");
            return false;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            ctx.Render(Left, buf, new Rect(0, 0, 10, 5));
            ctx.Render(Right, buf, new Rect(10, 0, 10, 5));
            ctx.Render(Label, buf, new Rect(0, 6, 10, 1));
            Hidden.Render(buf, new Rect(0, 8, 10, 1), ctx.For(Hidden));
        }
    }

    private sealed class LogOverlay(List<string> log) : Overlay
    {
        public override Rect Placement(Rect screen) => new(30, 10, 20, 6);

        public override bool HandleMouse(MouseEvent mouse)
        {
            log.Add($"overlay {mouse.Action}");
            return true;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    [Fact]
    public void TheChildUnderThePointer_GetsTheEvent_ThenItsAncestors()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new TwoPanes(log));

        harness.Click(12, 2);

        Assert.Equal(["right Press", "screen Press", "right Release", "screen Release"], log);
    }

    [Fact]
    public void AHandledEvent_StopsThere()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new TwoPanes(log, leftHandles: true));

        harness.Scroll(2, 2, 1);

        Assert.Equal(["left ScrollDown"], log);
    }

    [Fact]
    public void APress_FocusesTheTarget_ButScrollsAndReleasesDont()
    {
        var log = new List<string>();
        var screen = new TwoPanes(log);
        using var harness = new ComponentHarness(screen);

        harness.Scroll(12, 2, 3);
        harness.Mouse(new MouseEvent(MouseAction.Release, MouseButton.Left, 12, 2));
        Assert.False(screen.FocusedRight);

        harness.Mouse(new MouseEvent(MouseAction.Press, MouseButton.Right, 12, 2));
        Assert.True(screen.FocusedRight);

        harness.Click(2, 2);
        Assert.False(screen.FocusedRight);
    }

    [Fact]
    public void ANonFocusableTarget_LeavesFocusAlone_AndGetsTheEvent()
    {
        var log = new List<string>();
        var screen = new TwoPanes(log);
        using var harness = new ComponentHarness(screen);
        harness.Click(12, 2);

        log.Clear();
        harness.Click(2, 6);

        Assert.True(screen.FocusedRight);
        Assert.Equal("label Press", log[0]);
    }

    [Fact]
    public void AChildRenderedWithoutCtxRender_RoutesToItsParent()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new TwoPanes(log));

        harness.Click(2, 8);

        Assert.Equal(["screen Press", "screen Release"], log);
    }

    [Fact]
    public void CtxRender_SetsArea()
    {
        var screen = new TwoPanes([]);
        using var harness = new ComponentHarness(screen);

        Assert.Equal(new Rect(10, 0, 10, 5), screen.Right.Area);
        Assert.Equal(new Rect(0, 0, 80, 23), screen.Area); // the body, above the status bar
    }

    [Fact]
    public void WithAnOverlayOpen_OnlyTheOverlayCanBeHit()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new TwoPanes(log));
        harness.App.Overlays.Show(new LogOverlay(log));
        harness.RunUntilIdle();

        harness.Click(2, 2);    // on the screen behind: dropped
        harness.Click(30, 10);  // on the overlay's border
        harness.Click(35, 12);

        Assert.Equal(["overlay Press", "overlay Release", "overlay Press", "overlay Release"], log);
    }

    [Fact]
    public void OnMouse_ReturningTrue_StopsTheEvent_AndOnEventSeesIt()
    {
        var log = new List<string>();
        var events = new List<Event>();
        using var harness = new ComponentHarness(new TwoPanes(log));
        harness.App.OnEvent(events.Add).OnMouse(m => m.Action == MouseAction.Press);

        harness.Click(2, 2);

        Assert.Equal(["left Release", "screen Release"], log);
        Assert.Equal(2, events.OfType<MouseEvent>().Count());
    }

    [Fact]
    public void MouseEvents_NeverFireKeyBindings_AndTheStatusBarIsNotATarget()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new TwoPanes(log));
        var fired = 0;
        harness.App.Keys.Add("enter", "Fire", () => fired++);

        harness.Click(2, 23);
        harness.Click(200, 200);

        Assert.Empty(log);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void APoppedScreensChildren_AreNotHit()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new TwoPanes(log));
        harness.App.Screens.Push(new TwoPanes(log));
        harness.RunUntilIdle();
        harness.App.Screens.Pop();
        log.Clear();

        // Before a redraw, the last frame shows the popped screen: its unmounted children aren't hit
        harness.Click(12, 2);
        Assert.Empty(log);

        harness.Click(12, 2);
        Assert.Equal(["right Press", "screen Press", "right Release", "screen Release"], log);
    }

    [Fact]
    public async Task ARenderCallbackApp_GetsOnMouseOnly()
    {
        var input = new ScriptedInputSource()
            .EnqueueMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 1, 1))
            .Enqueue(AppHarness.Key('q'));
        var seen = new List<MouseEvent>();
        var app = AppHarness.Create(new TestBackend(20, 5), input).Render((_, _) => { });
        app.OnMouse(m =>
        {
            seen.Add(m);
            return false;
        });
        app.OnKey(key =>
        {
            if (key.KeyChar == 'q') app.Quit();
            return true;
        });

        await AppHarness.Run(app);

        Assert.Single(seen);
    }

    [Fact]
    public void ScriptedInput_TryRead_SkipsMouseEvents()
    {
        var input = new ScriptedInputSource().EnqueueMouse(M(MouseAction.Press, MouseButton.Left, 0, 0)).EnqueueText("k");

        Assert.True(input.TryRead(out var key));
        Assert.Equal('k', key.KeyChar);
    }

    // Built-in handling

    private sealed class ListScreen : Screen
    {
        public SelectionState Selection { get; } = new();
        public ScrollState Scroll { get; } = new();
        public TabBarState Tabs { get; } = new();
        public IReadOnlyList<string> Items { get; } = [.. Enumerable.Range(0, 50).Select(i => $"item {i}")];

        public override bool HandleMouse(MouseEvent mouse) =>
            Selection.HandleMouse(mouse, Items.Count) || Scroll.HandleMouse(mouse) || Tabs.HandleMouse(mouse);

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            Widget.TabBar().Tabs("One", "Two", "Three").State(Tabs).Draw(buf, new Rect(0, 0, 40, 2));
            Widget.List().Items(Items).State(Selection).Draw(buf, new Rect(0, 2, 20, 12));
            Widget.Paragraph().Text(string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line {i}"))).State(Scroll)
                .Draw(buf, new Rect(30, 2, 20, 10));
        }
    }

    [Fact]
    public void List_ClickSelectsTheRowUnderThePointer_WhenScrolled()
    {
        var screen = new ListScreen();
        using var harness = new ComponentHarness(screen);
        screen.Selection.Selected = 30;
        harness.RunUntilIdle();
        var offset = screen.Selection.Offset;

        harness.Click(5, 3 + 2); // the third row inside the border

        Assert.Equal(offset + 2, screen.Selection.Selected);
    }

    [Fact]
    public void List_BorderClicks_DontSelect_AndTheWheelMovesBySelectionByOne()
    {
        var screen = new ListScreen();
        using var harness = new ComponentHarness(screen);

        harness.Click(5, 2);
        Assert.Equal(0, screen.Selection.Selected);

        harness.Scroll(5, 6, 3);
        Assert.Equal(3, screen.Selection.Selected);
        harness.Scroll(5, 6, -1);
        Assert.Equal(2, screen.Selection.Selected);
    }

    [Fact]
    public void List_AClickPastTheLastItem_DoesNothing()
    {
        var selection = new SelectionState();
        var buf = new Buffer(20, 10);
        Widget.List().Items(["a", "b"]).State(selection).Draw(buf, new Rect(0, 0, 20, 10));

        Assert.False(selection.HandleMouse(M(MouseAction.Press, MouseButton.Left, 2, 5), 2));
        Assert.True(selection.HandleMouse(M(MouseAction.Press, MouseButton.Left, 2, 2), 2));
        Assert.Equal(1, selection.Selected);
    }

    [Fact]
    public void Table_ClickBelowTheHeader_SelectsThatRow_AndHeaderClicksDont()
    {
        var selection = new SelectionState();
        var buf = new Buffer(30, 10);
        Widget.Table().Header("Name").Rows([new Row("r0"), new Row("r1"), new Row("r2")]).State(selection)
            .Draw(buf, new Rect(0, 0, 30, 10));

        Assert.False(selection.HandleMouse(M(MouseAction.Press, MouseButton.Left, 1, 0), 3));
        var firstRow = Enumerable.Range(0, 10).First(y => buf.ToLines()[y].Contains("r0", StringComparison.Ordinal));
        Assert.True(selection.HandleMouse(M(MouseAction.Press, MouseButton.Left, 1, firstRow + 2), 3));
        Assert.Equal(2, selection.Selected);
    }

    [Fact]
    public void Paragraph_TheWheelScrollsThreeRows()
    {
        var screen = new ListScreen();
        using var harness = new ComponentHarness(screen);

        harness.Scroll(35, 5, 2);
        Assert.Equal(6, screen.Scroll.Offset);
        harness.Scroll(35, 5, -1);
        Assert.Equal(3, screen.Scroll.Offset);
    }

    [Fact]
    public void TabBar_ClickSelectsTheTabUnderThePointer()
    {
        var screen = new ListScreen();
        using var harness = new ComponentHarness(screen);
        var line = harness.Lines[0];

        harness.Click(line.IndexOf("Three", StringComparison.Ordinal), 0);
        Assert.Equal(2, screen.Tabs.Selected);
        harness.Click(line.IndexOf("Two", StringComparison.Ordinal), 0);
        Assert.Equal(1, screen.Tabs.Selected);
        harness.Click(line.Length - 1, 0);
        Assert.Equal(1, screen.Tabs.Selected);
    }

    [Fact]
    public void TabBar_ClicksMapToTheTabsDrawn_WhenTheFirstAreScrolledOff()
    {
        var tabs = new TabBarState { Selected = 4 };
        var buf = new Buffer(20, 2);
        Widget.TabBar().Tabs("Alpha", "Bravo", "Charlie", "Delta", "Echo").State(tabs).Draw(buf, new Rect(0, 0, 20, 2));
        var line = buf.ToLines()[0];

        Assert.True(tabs.HandleMouse(M(MouseAction.Press, MouseButton.Left, line.IndexOf("Delta", StringComparison.Ordinal), 0)));
        Assert.Equal(3, tabs.Selected);
    }

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("No", false)]
    public async Task ConfirmDialog_ClickingAButton_Answers(string button, bool expected)
    {
        using var harness = new ComponentHarness(new TwoPanes([]));
        var answer = harness.App.Overlays.ConfirmAsync("Delete it?");
        harness.RunUntilIdle();
        var y = Array.FindIndex(harness.Lines, l => l.Contains(" Yes ", StringComparison.Ordinal));
        var x = harness.Lines[y].IndexOf($" {button} ", StringComparison.Ordinal) + 1;

        harness.Click(x, y);

        Assert.True(answer.IsCompleted);
        Assert.Equal(expected, await answer);
        Assert.False(harness.App.Overlays.Any);
    }

    [Fact]
    public void MessageDialog_TheWheelScrolls()
    {
        using var harness = new ComponentHarness(new TwoPanes([]), height: 12);
        _ = harness.App.Overlays.ShowMessageAsync("Long", string.Join("\n", Enumerable.Range(0, 30).Select(i => $"row {i}")));
        harness.RunUntilIdle();
        Assert.Contains(harness.Lines, l => l.Contains("row 0", StringComparison.Ordinal));

        harness.Scroll(40, 6, 2);

        Assert.DoesNotContain(harness.Lines, l => l.Contains("row 0 ", StringComparison.Ordinal));
        Assert.Contains(harness.Lines, l => l.Contains("row 6", StringComparison.Ordinal));
    }
}
