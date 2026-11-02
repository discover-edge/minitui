using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>SplitState and Widget.Split (1.x spec §14.3).</summary>
public class SplitTests
{
    private static MouseEvent M(MouseAction action, int x, int y, MouseButton button = MouseButton.Left) => new(action, button, x, y);

    private static (Rect First, Rect Second) Draw(SplitState state, int width = 21, int height = 5, Buffer? buf = null) =>
        Widget.Split(state).Draw(buf ?? new Buffer(width, height), new Rect(0, 0, width, height));

    // Layout

    [Theory]
    [InlineData(0.5, 10)]
    [InlineData(0.25, 5)]
    [InlineData(0.0, 3)]   // the minimum
    [InlineData(1.0, 17)]  // the second pane's minimum
    public void ARatio_SizesTheFirstPane_WithinTheMinimums(double ratio, int first)
    {
        var (a, b) = Draw(new SplitState(Direction.Horizontal, ratio));

        Assert.Equal(new Rect(0, 0, first, 5), a);
        Assert.Equal(new Rect(first + 1, 0, 20 - first, 5), b);
    }

    [Fact]
    public void Vertical_StacksThePanes()
    {
        var (a, b) = Draw(new SplitState(Direction.Vertical, 0.5), 10, 11);

        Assert.Equal(new Rect(0, 0, 10, 5), a);
        Assert.Equal(new Rect(0, 6, 10, 5), b);
    }

    [Theory]
    [InlineData(SplitPane.First, 6)]
    [InlineData(SplitPane.Second, 14)]
    public void AFixedPane_KeepsItsSize(SplitPane pane, int first)
    {
        var state = SplitState.Fixed(Direction.Horizontal, 6, pane);

        Assert.Equal(first, Draw(state).First.Width);

        // A wider terminal: the fixed pane keeps 6 cells, the other takes the rest
        var (a, b) = Draw(state, 41);
        Assert.Equal(pane == SplitPane.First ? 6 : 34, a.Width);
        Assert.Equal(pane == SplitPane.First ? 34 : 6, b.Width);
    }

    [Fact]
    public void AFixedPane_SqueezedByASmallArea_ComesBack()
    {
        var state = SplitState.Fixed(Direction.Horizontal, 30);

        Assert.Equal(17, Draw(state).First.Width);
        Assert.Equal(30, state.FixedSize);
        Assert.Equal(30, Draw(state, 60).First.Width);
    }

    [Fact]
    public void TooSmallForBothMinimums_SharesTheSpaceByRatio()
    {
        var (a, b) = Draw(new SplitState(Direction.Horizontal, 0.5), 5);

        Assert.Equal(2, a.Width);
        Assert.Equal(2, b.Width);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void AnAreaWithoutRoom_GivesTheFirstPaneEverything(int width)
    {
        var buf = new Buffer(Math.Max(1, width), 3);
        var (a, b) = Draw(new SplitState(Direction.Horizontal), width, 3, buf);

        Assert.Equal(new Rect(0, 0, width, 3), a);
        Assert.Equal(0, b.Width);
        Assert.DoesNotContain("│", string.Concat(buf.ToLines()), StringComparison.Ordinal);
    }

    [Fact]
    public void Ratio_IsClamped()
    {
        Assert.Equal(1, new SplitState(Direction.Horizontal, 3).Ratio);
        Assert.Equal(0, new SplitState(Direction.Horizontal, -1).Ratio);
        Assert.Equal(0, new SplitState(Direction.Horizontal) { FixedSize = -4 }.FixedSize);
    }

    // Drawing

    [Fact]
    public void TheDivider_IsDrawnInTheBorderStyle_OrFocusedWhileDragging()
    {
        using var _ = Theme.Use(Theme.Dark);
        var state = new SplitState(Direction.Horizontal);
        var buf = new Buffer(21, 3);
        Draw(state, 21, 3, buf);

        Assert.Equal(["          │          ", "          │          ", "          │          "], buf.ToLines());
        Assert.Equal(Theme.Dark.Border.Fg, buf.Get(10, 0).Fg);

        state.HandleMouse(M(MouseAction.Press, 10, 1));
        Draw(state, 21, 3, buf);
        Assert.Equal(Theme.Dark.BorderFocused.Fg, buf.Get(10, 0).Fg);

        var vertical = new Buffer(5, 7);
        Widget.Split(new SplitState(Direction.Vertical)).Focused().Draw(vertical, new Rect(0, 0, 5, 7));
        Assert.Equal("─────", vertical.ToLines()[3]);
        Assert.Equal(Theme.Dark.BorderFocused.Fg, vertical.Get(0, 3).Fg);
    }

    // The mouse

    [Fact]
    public void DraggingTheDivider_MovesIt_AndRaisesChanged()
    {
        var state = new SplitState(Direction.Horizontal);
        var changes = 0;
        state.Changed += (_, _) => changes++;
        Draw(state);

        Assert.True(state.HandleMouse(M(MouseAction.Press, 10, 2)));
        Assert.True(state.IsDragging);
        Assert.True(state.HandleMouse(M(MouseAction.Drag, 14, 2)));

        Assert.Equal(14, Draw(state).First.Width);
        Assert.Equal(0.7, state.Ratio, 3);
        Assert.Equal(1, changes);

        // The same position again changes nothing
        state.HandleMouse(M(MouseAction.Drag, 14, 4));
        Assert.Equal(1, changes);

        Assert.True(state.HandleMouse(M(MouseAction.Release, 14, 2)));
        Assert.False(state.IsDragging);
        Assert.False(state.HandleMouse(M(MouseAction.Drag, 5, 2)));
    }

    [Fact]
    public void Drags_AreClampedToTheMinimums_EvenOutsideTheArea()
    {
        var state = new SplitState(Direction.Horizontal) { MinFirst = 4, MinSecond = 5 };
        Draw(state);

        state.HandleMouse(M(MouseAction.Press, 10, 0));
        state.HandleMouse(M(MouseAction.Drag, -30, 0));
        Assert.Equal(4, Draw(state).First.Width);
        state.HandleMouse(M(MouseAction.Drag, 99, 0));
        Assert.Equal(15, Draw(state).First.Width);
    }

    [Fact]
    public void DraggingAFixedSplit_ChangesItsSize()
    {
        var state = SplitState.Fixed(Direction.Horizontal, 6, SplitPane.Second);
        Draw(state);

        state.HandleMouse(M(MouseAction.Press, 14, 0));
        state.HandleMouse(M(MouseAction.Drag, 10, 0));

        Assert.Equal(10, state.FixedSize);
        Assert.Equal(0.5, state.Ratio);
        Assert.Equal(10, Draw(state, 41).Second.Width);
    }

    [Fact]
    public void PressesOffTheDivider_AreLeftToThePanes_AndEndADrag()
    {
        var state = new SplitState(Direction.Horizontal);
        Draw(state);

        Assert.False(state.HandleMouse(M(MouseAction.Press, 3, 2)));
        Assert.False(state.HandleMouse(M(MouseAction.Press, 10, 2, MouseButton.Right)));
        Assert.False(state.HandleMouse(M(MouseAction.ScrollDown, 10, 2, MouseButton.None)));
        Assert.False(state.HandleMouse(M(MouseAction.Drag, 12, 2)));

        state.HandleMouse(M(MouseAction.Press, 10, 2));
        state.HandleMouse(M(MouseAction.Press, 3, 2));
        Assert.False(state.IsDragging);
    }

    [Fact]
    public void SettingRatioInCode_DoesntRaiseChanged()
    {
        var state = new SplitState(Direction.Horizontal);
        var changes = 0;
        state.Changed += (_, _) => changes++;

        state.Ratio = 0.2;
        state.FixedSize = 4;

        Assert.Equal(0, changes);
    }

    [Fact]
    public void NestedSplits_EachReactToTheirOwnDivider()
    {
        var outer = new SplitState(Direction.Horizontal);
        var inner = new SplitState(Direction.Vertical);
        var buf = new Buffer(21, 11);
        var (_, right) = Widget.Split(outer).Draw(buf, new Rect(0, 0, 21, 11));
        Widget.Split(inner).Draw(buf, right);

        Assert.False(outer.HandleMouse(M(MouseAction.Press, 15, 5)));
        Assert.True(inner.HandleMouse(M(MouseAction.Press, 15, 5)));
        inner.HandleMouse(M(MouseAction.Drag, 15, 7));

        Assert.Equal(7, Widget.Split(inner).Draw(buf, right).First.Height);
        Assert.Equal(0.5, outer.Ratio);
    }

    // Keys

    [Theory]
    [InlineData(Direction.Horizontal, ConsoleKey.LeftArrow, 9)]
    [InlineData(Direction.Horizontal, ConsoleKey.RightArrow, 11)]
    [InlineData(Direction.Vertical, ConsoleKey.UpArrow, 9)]
    [InlineData(Direction.Vertical, ConsoleKey.DownArrow, 11)]
    public void AltShiftArrows_AlongTheDirection_MoveTheDivider(Direction direction, ConsoleKey key, int first)
    {
        var state = new SplitState(direction);
        var changes = 0;
        state.Changed += (_, _) => changes++;
        Draw(state, 21, 21);

        Assert.True(state.HandleKey(new ConsoleKeyInfo('\0', key, shift: true, alt: true, control: false)));

        var (a, _) = Draw(state, 21, 21);
        Assert.Equal(first, direction == Direction.Horizontal ? a.Width : a.Height);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(ConsoleKey.UpArrow, true, true)]
    [InlineData(ConsoleKey.LeftArrow, true, false)]
    [InlineData(ConsoleKey.LeftArrow, false, true)]
    public void OtherKeys_AreLeftAlone(ConsoleKey key, bool shift, bool alt)
    {
        var state = new SplitState(Direction.Horizontal);
        Draw(state);

        Assert.False(state.HandleKey(new ConsoleKeyInfo('\0', key, shift, alt, control: false)));
        Assert.Equal(10, Draw(state).First.Width);
    }

    [Fact]
    public void MoveBy_StopsAtTheMinimums_AndDoesNothingBeforeTheFirstDraw()
    {
        var state = new SplitState(Direction.Horizontal);
        state.MoveBy(5);
        Assert.Equal(0.5, state.Ratio);

        Draw(state);
        state.MoveBy(-100);
        Assert.Equal(3, Draw(state).First.Width);
    }

    // Through the app: a release over another component still ends the drag

    private sealed class Pane : Component
    {
        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    private sealed class Panes : Screen
    {
        private readonly Pane _left = new();
        private readonly Pane _right = new();

        public Panes()
        {
            AddChild(_left);
            AddChild(_right);
            Focus.Add(_left).Add(_right);
        }

        public SplitState State { get; } = new(Direction.Horizontal);

        public override bool HandleMouse(MouseEvent mouse) => State.HandleMouse(mouse);

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            var (a, b) = Widget.Split(State).Draw(buf, area);
            ctx.Render(_left, buf, a);
            ctx.Render(_right, buf, b);
        }
    }

    [Fact]
    public void InAnApp_TheDividerDrags_AndTheReleaseOverAPaneEndsIt()
    {
        var screen = new Panes();
        using var harness = new ComponentHarness(screen, 41, 6);
        harness.RunUntilIdle();
        Assert.Equal('│', harness.Lines[0][20]);

        harness.Mouse(M(MouseAction.Press, 20, 1));
        harness.Mouse(M(MouseAction.Drag, 30, 1));
        harness.Mouse(M(MouseAction.Release, 30, 1));

        Assert.False(screen.State.IsDragging);
        Assert.Equal('│', harness.Lines[0][30]);
    }
}
