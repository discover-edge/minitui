using Microsoft.Extensions.Time.Testing;

namespace MiniTui.Tests;

/// <summary>Click counting and what double- and triple-clicks do (1.x spec §17.3, §17.4).</summary>
public class DoubleClickTests
{
    private static MouseEvent Press(int x, int y, int count = 1, MouseButton button = MouseButton.Left) =>
        new(MouseAction.Press, button, x, y) { ClickCount = count };

    private sealed class Blank : Screen
    {
        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) { }
    }

    // Counting

    private static (ComponentHarness Harness, FakeTimeProvider Time, List<int> Counts) Counting()
    {
        var time = new FakeTimeProvider();
        var harness = new ComponentHarness(new Blank(), 20, 5, time);
        var counts = new List<int>();
        harness.App.OnMouse(m =>
        {
            if (m.Action == MouseAction.Press) counts.Add(m.ClickCount);
            else counts.Add(-m.ClickCount);
            return false;
        });
        harness.RunUntilIdle();
        return (harness, time, counts);
    }

    [Fact]
    public void QuickPresses_InOneCell_CountToThree_ThenStartAgain()
    {
        var (harness, _, counts) = Counting();
        using var _h = harness;

        for (var i = 0; i < 4; i++) harness.Mouse(Press(3, 2));

        Assert.Equal([1, 2, 3, 1], counts);
    }

    [Fact]
    public void OtherActions_HaveNoCount()
    {
        var (harness, _, counts) = Counting();
        using var _h = harness;

        harness.Click(3, 2);
        harness.Mouse(new MouseEvent(MouseAction.ScrollDown, MouseButton.None, 3, 2));

        Assert.Equal([1, 0, 0], counts);
    }

    [Fact]
    public void AnotherCell_AnotherButton_OrTooLong_StartsAgain()
    {
        var (harness, time, counts) = Counting();
        using var _h = harness;

        harness.Mouse(Press(3, 2));
        harness.Mouse(Press(4, 2));
        harness.Mouse(Press(4, 2, button: MouseButton.Right));
        harness.Mouse(Press(4, 2, button: MouseButton.Right));
        time.Advance(TimeSpan.FromMilliseconds(501));
        harness.Mouse(Press(4, 2, button: MouseButton.Right));
        time.Advance(TimeSpan.FromMilliseconds(500));
        harness.Mouse(Press(4, 2, button: MouseButton.Right));

        Assert.Equal([1, 1, 1, 2, 1, 2], counts);
    }

    [Fact]
    public void WithDoubleClickTime_ChangesTheInterval_WithinLimits()
    {
        var (harness, time, counts) = Counting();
        using var _h = harness;
        harness.App.WithDoubleClickTime(TimeSpan.FromSeconds(1));

        harness.Mouse(Press(3, 2));
        time.Advance(TimeSpan.FromMilliseconds(900));
        harness.Mouse(Press(3, 2));

        Assert.Equal([1, 2], counts);
        Assert.Throws<ArgumentOutOfRangeException>(() => harness.App.WithDoubleClickTime(TimeSpan.FromMilliseconds(99)));
        Assert.Throws<ArgumentOutOfRangeException>(() => harness.App.WithDoubleClickTime(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void TheHarnessDoubleClick_IsTwoPressesAndReleases()
    {
        var (harness, _, counts) = Counting();
        using var _h = harness;

        harness.DoubleClick(3, 2);

        Assert.Equal([1, 0, 2, 0], counts);
    }

    [Fact]
    public void WindowsDoubleClickRecords_ArePlainPresses()
    {
        var translator = new WindowsInputTranslator(() => default);
        var decoder = new AnsiInputDecoder(TimeSpan.Zero);
        var sink = new MouseSink();

        // A press, a release, then the second press marked DOUBLE_CLICK (0x2)
        translator.Translate([InputRecord.Mouse(3, 4, 0x1), InputRecord.Mouse(3, 4, 0), InputRecord.Mouse(3, 4, 0x1, flags: 0x2)], decoder, TimeSpan.Zero, sink, out _);

        Assert.Equal([MouseAction.Press, MouseAction.Release, MouseAction.Press], sink.Events.Select(e => e.Action));
        Assert.All(sink.Events, e => Assert.Equal(0, e.ClickCount));
    }

    private sealed class MouseSink : IInputSink
    {
        public List<MouseEvent> Events { get; } = [];
        public void Key(ConsoleKeyInfo key) { }
        public void Paste(string text) { }
        public void Mouse(MouseEvent mouse) => Events.Add(mouse);
    }

    // Words

    [Theory]
    [InlineData("hello world", 2, 0, 5)]
    [InlineData("hello world", 5, 5, 6)]
    [InlineData("foo_bar1+=x", 3, 0, 8)]
    [InlineData("foo_bar1+=x", 8, 8, 10)]
    [InlineData("a   b", 2, 1, 4)]
    [InlineData("end", 3, 0, 3)]
    [InlineData("", 0, 0, 0)]
    [InlineData("😀😀 x", 2, 0, 4)]
    [InlineData("cafés ok", 1, 0, 6)]
    public void WordAt_FindsTheRun(string text, int index, int start, int end)
    {
        Assert.Equal((start, end), TextFieldState.WordAt(text, index));
    }

    // Text field

    private static TextFieldState Field(string text)
    {
        var state = new TextFieldState { Text = text };
        Widget.TextField().State(state).Draw(new CellBuffer(30, 1), new Rect(0, 0, 30, 1));
        return state;
    }

    [Fact]
    public void ATextField_SelectsTheWord_ThenAll()
    {
        var state = Field("one two three");

        state.HandleMouse(Press(5, 0, 1));
        state.HandleMouse(Press(5, 0, 2));
        Assert.Equal("two", state.SelectedText);

        state.HandleMouse(Press(5, 0, 3));
        Assert.Equal("one two three", state.SelectedText);
    }

    [Fact]
    public void ADragAfterADoubleClick_ExtendsFromTheWordsStart()
    {
        var state = Field("one two three");

        state.HandleMouse(Press(5, 0, 2));
        state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 10, 0));

        Assert.Equal("two th", state.SelectedText);
    }

    // Text area

    private static TextAreaState Area(string text)
    {
        var state = new TextAreaState(text);
        Widget.TextArea().State(state).Draw(new CellBuffer(30, 5), new Rect(0, 0, 30, 5));
        return state;
    }

    [Fact]
    public void ATextArea_SelectsTheWord_ThenTheLine()
    {
        var state = Area("first line\nsecond (line)\n");

        state.HandleMouse(Press(9, 1, 2));
        Assert.Equal("line", state.SelectedText);

        state.HandleMouse(Press(9, 1, 3));
        Assert.Equal("second (line)", state.SelectedText);
        Assert.Equal(new TextPosition(1, 13), state.Cursor);
    }

    [Fact]
    public void ATextArea_DoubleClickPastALinesEnd_SelectsItsLastWord_AndOnAnEmptyLineNothing()
    {
        var state = Area("ab cd\n\nx");

        state.HandleMouse(Press(20, 0, 2));
        Assert.Equal("cd", state.SelectedText);

        state.HandleMouse(Press(3, 1, 2));
        Assert.False(state.HasSelection);
        Assert.Equal(new TextPosition(1, 0), state.Cursor);
    }

    // Lists and tables

    [Fact]
    public void AListRow_DoubleClicked_RaisesActivated_Once()
    {
        var state = new SelectionState();
        var activated = new List<int>();
        state.Activated += (_, _) => activated.Add(state.Selected);
        string[] items = ["a", "b", "c"];
        Widget.List().Items(items).State(state).Draw(new CellBuffer(10, 5), new Rect(0, 0, 10, 5));

        state.HandleMouse(Press(2, 2, 1), items.Length);
        state.HandleMouse(Press(2, 2, 2), items.Length);
        state.HandleMouse(Press(2, 2, 3), items.Length);
        state.HandleMouse(Press(2, 3, 1), items.Length);

        Assert.Equal([1], activated);
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void ADoubleClickInAComponent_GoesThroughTheHarness()
    {
        var list = new Rows();
        using var harness = new ComponentHarness(list, 10, 5);
        harness.RunUntilIdle();

        harness.DoubleClick(2, 2);

        Assert.Equal([1], list.Activated);
    }

    private sealed class Rows : Screen
    {
        private static readonly string[] Items = ["a", "b", "c"];
        private readonly SelectionState _state = new();
        public List<int> Activated { get; } = [];

        public Rows() => _state.Activated += (_, _) => Activated.Add(_state.Selected);

        public override bool HandleMouse(MouseEvent mouse) => _state.HandleMouse(mouse, Items.Length);

        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) =>
            Widget.List().Items(Items).State(_state).Draw(buf, area);
    }

    // Trees

    private sealed record Node(string Name, Node[] Children);

    private static TreeState<Node> Tree(out List<string> activated)
    {
        var leaf = new Node("leaf", []);
        var state = new TreeState<Node>(n => n.Children, n => n.Name) { Roots = [new Node("dir", [new Node("inner", [])]), leaf] };
        var log = new List<string>();
        state.Activated += (_, e) => log.Add(e.Node.Name);
        activated = log;
        Widget.Tree(state).Draw(new CellBuffer(20, 6), new Rect(0, 0, 20, 6));
        return state;
    }

    private static void Redraw(TreeState<Node> state) => Widget.Tree(state).Draw(new CellBuffer(20, 6), new Rect(0, 0, 20, 6));

    [Fact]
    public void ATreeLeaf_DoubleClicked_IsActivated()
    {
        var state = Tree(out var activated);

        state.HandleMouse(Press(8, 2, 1));
        state.HandleMouse(Press(8, 2, 2));

        Assert.Equal(["leaf"], activated);
    }

    [Fact]
    public void ATreeParent_DoubleClicked_Toggles()
    {
        var state = Tree(out _);

        state.HandleMouse(Press(8, 1, 1));
        state.HandleMouse(Press(8, 1, 2));
        Redraw(state);

        Assert.True(state.IsExpanded(state.Roots[0]));
    }

    [Fact]
    public void ADoubleClickOnTheExpander_TogglesOnce()
    {
        var state = Tree(out _);
        var row = Draw(state);
        var expander = Enumerable.Range(1, 18).First(x => row.Get(x, 1).Symbol != " ");

        state.HandleMouse(Press(expander, 1, 1));
        Redraw(state);
        state.HandleMouse(Press(expander, 1, 2));
        Redraw(state);

        Assert.True(state.IsExpanded(state.Roots[0]));
    }

    private static CellBuffer Draw(TreeState<Node> state)
    {
        var buf = new CellBuffer(20, 6);
        Widget.Tree(state).Draw(buf, new Rect(0, 0, 20, 6));
        return buf;
    }
}
