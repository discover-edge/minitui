namespace MiniTui.Tests;

/// <summary>CheckboxState and ButtonState (1.x spec §23.4).</summary>
public class ControlStateTests
{
    private static ConsoleKeyInfo Key(string binding) => KeyBinding.Parse(binding).ToKeyInfo();

    private static MouseEvent Mouse(MouseAction action, int x, int y, MouseButton button = MouseButton.Left) => new(action, button, x, y);

    // Checkbox: "[ ] Remember me" drawn at (2, 1)
    private static (CheckboxState State, int Changes) Checkbox(Action<CheckboxState> act)
    {
        var state = new CheckboxState();
        var changes = 0;
        state.Changed += (_, _) => changes++;
        Widget.Checkbox().Label("Remember me").State(state).Draw(new CellBuffer(30, 3), new Rect(2, 1, 28, 1));
        act(state);
        return (state, changes);
    }

    [Fact]
    public void Space_TogglesIt_AndRaisesChanged()
    {
        var (state, changes) = Checkbox(s =>
        {
            Assert.True(s.HandleKey(Key("space")));
            Assert.True(s.Checked);
            Assert.True(s.HandleKey(Key("space")));
        });

        Assert.False(state.Checked);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void OtherKeys_AndSpaceWithModifiers_AreLeft()
    {
        var (state, changes) = Checkbox(s =>
        {
            Assert.False(s.HandleKey(Key("enter")));
            Assert.False(s.HandleKey(Key("ctrl+space")));
        });

        Assert.False(state.Checked);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(2)]     // [
    [InlineData(3)]     // the box
    [InlineData(6)]     // R
    [InlineData(16)]    // the label's last letter
    public void AClickOnTheBoxOrLabel_TogglesIt(int x)
    {
        var (state, changes) = Checkbox(s => Assert.True(s.HandleMouse(Mouse(MouseAction.Press, x, 1))));

        Assert.True(state.Checked);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(1, 1)]      // left of it
    [InlineData(17, 1)]     // past the label
    [InlineData(3, 2)]      // the row below
    public void AClickElsewhere_IsLeft(int x, int y)
    {
        var (state, _) = Checkbox(s => Assert.False(s.HandleMouse(Mouse(MouseAction.Press, x, y))));

        Assert.False(state.Checked);
    }

    [Fact]
    public void ARightClick_OrARelease_IsLeft()
    {
        var (state, _) = Checkbox(s =>
        {
            Assert.False(s.HandleMouse(Mouse(MouseAction.Press, 3, 1, MouseButton.Right)));
            Assert.False(s.HandleMouse(Mouse(MouseAction.Release, 3, 1)));
        });

        Assert.False(state.Checked);
    }

    [Fact]
    public void SettingCheckedOrToggle_FromCode_DoesntRaiseChanged()
    {
        var (state, changes) = Checkbox(s =>
        {
            s.Checked = true;
            s.Toggle();
            s.Toggle();
        });

        Assert.True(state.Checked);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void TheCheckbox_DrawsTheStatesValue()
    {
        var state = new CheckboxState { Checked = true };
        var buf = new CellBuffer(20, 1);

        Widget.Checkbox().Label("On").Checked(false).State(state).Draw(buf, buf.Area);

        Assert.StartsWith("[✓] On", buf.ToLines()[0], StringComparison.Ordinal);
    }

    // Button: drawn at (2, 1), 10 × 3

    private static (ButtonState State, Func<int> Presses, Func<string> Drawn) Button()
    {
        var state = new ButtonState();
        var presses = 0;
        state.Pressed += (_, _) => presses++;
        var buf = new CellBuffer(20, 5);
        string Draw()
        {
            Widget.Button().Label("OK").State(state).Draw(buf, new Rect(2, 1, 10, 3));
            return buf.Get(2, 1).Bg == Theme.Current.ButtonFocused.Reversed().Bg && buf.Get(2, 1).Fg == Theme.Current.ButtonFocused.Reversed().Fg ? "down" : "up";
        }
        Draw();
        return (state, () => presses, Draw);
    }

    [Theory]
    [InlineData("enter")]
    [InlineData("space")]
    public void EnterAndSpace_PressIt_AtOnce(string binding)
    {
        var (state, presses, _) = Button();

        Assert.True(state.HandleKey(Key(binding)));

        Assert.Equal(1, presses());
    }

    [Fact]
    public void CtrlEnter_IsLeft_ForTheScreensSubmit()
    {
        var (state, presses, _) = Button();

        Assert.False(state.HandleKey(Key("ctrl+enter")));
        Assert.False(state.HandleKey(Key("a")));

        Assert.Equal(0, presses());
    }

    [Fact]
    public void AClick_PressesItOnRelease_AndItDrawsDownWhileHeld()
    {
        var (state, presses, draw) = Button();

        Assert.True(state.HandleMouse(Mouse(MouseAction.Press, 5, 2)));
        Assert.True(state.IsDown);
        Assert.Equal("down", draw());
        Assert.Equal(0, presses());

        Assert.True(state.HandleMouse(Mouse(MouseAction.Release, 5, 2)));
        Assert.False(state.IsDown);
        Assert.Equal("up", draw());
        Assert.Equal(1, presses());
    }

    [Fact]
    public void APressDraggedOff_IsCancelled_AndBackOnIsDownAgain()
    {
        var (state, presses, _) = Button();

        state.HandleMouse(Mouse(MouseAction.Press, 5, 2));
        Assert.True(state.HandleMouse(Mouse(MouseAction.Drag, 15, 2)));
        Assert.False(state.IsDown);
        state.HandleMouse(Mouse(MouseAction.Drag, 6, 2));
        Assert.True(state.IsDown);
        state.HandleMouse(Mouse(MouseAction.Drag, 6, 4));
        Assert.True(state.HandleMouse(Mouse(MouseAction.Release, 6, 4)));

        Assert.False(state.IsDown);
        Assert.Equal(0, presses());
    }

    [Fact]
    public void APressElsewhere_OrAReleaseAfterIt_IsLeft()
    {
        var (state, presses, _) = Button();

        Assert.False(state.HandleMouse(Mouse(MouseAction.Press, 15, 2)));
        Assert.False(state.HandleMouse(Mouse(MouseAction.Release, 5, 2)));
        Assert.False(state.HandleMouse(Mouse(MouseAction.Press, 5, 2, MouseButton.Right)));

        Assert.Equal(0, presses());
    }

    [Fact]
    public void ThroughAComponent_TheHarnessClickPressesIt()
    {
        var screen = new OneButton();
        using var harness = new ComponentHarness(screen, 20, 5);
        harness.RunUntilIdle();

        harness.Click(4, 2);

        Assert.Equal(1, screen.Presses);
    }

    private sealed class OneButton : Screen
    {
        private readonly ButtonState _ok = new();
        public int Presses { get; private set; }

        public OneButton() => _ok.Pressed += (_, _) => Presses++;

        public override bool HandleKey(ConsoleKeyInfo key) => _ok.HandleKey(key);
        public override bool HandleMouse(MouseEvent mouse) => _ok.HandleMouse(mouse);
        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) =>
            Widget.Button().Label("OK").State(_ok).Draw(buf, new Rect(2, 1, 8, 3));
    }
}
