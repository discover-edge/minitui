using System.Text;
using Microsoft.Extensions.Time.Testing;
using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class DebugOverlayTests
{
    private sealed class Probe(char fill, bool capturesText = false) : Component
    {
        public List<ConsoleKey> Got { get; } = [];
        public int Clicks { get; private set; }
        public override bool CapturesText => capturesText;

        public override bool HandleKey(ConsoleKeyInfo key)
        {
            Got.Add(key.Key);
            return true;
        }

        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.IsClick) Clicks++;
            return true;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
            buf.FillRect(area, new Cell(fill.ToString(), Color.Reset, Color.Reset, Modifiers.None));
    }

    private sealed class Panes : Screen
    {
        public Panes(bool textField = false)
        {
            A = AddChild(new Probe('a', textField));
            B = AddChild(new Probe('b'));
            Focus.Add(A).Add(B);
        }

        public Probe A { get; }
        public Probe B { get; }
        public Action? OnRender { get; set; }
        public override string Title => "Panes";

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            OnRender?.Invoke();
            var (left, right) = area.SplitVertical(area.Width / 2);
            ctx.Render(A, buf, left);
            ctx.Render(B, buf, right);
        }
    }

    // A run driven by hand, for backends and environments ComponentHarness doesn't take
    private sealed class ManualRun : IDisposable
    {
        public ManualRun(ITerminalBackend backend, Screen screen, TimeProvider? time = null, Func<string, string?>? env = null,
            Action<App>? configure = null)
        {
            App = App.Create().WithBackend(backend).WithInput(new ScriptedInputSource()).WithTickRate(null).CaptureConsoleOutput(false);
            if (time is not null) App.WithTimeProvider(time);
            App.GetEnvironment = env ?? (_ => null);
            configure?.Invoke(App);
            App.Screens.Push(screen);
            App.StartRun(CancellationToken.None);
            using var ui = App.EnterUi();
            App.Setup();
            App.Step(wait: false);
        }

        public App App { get; }

        public void Redraw()
        {
            App.RequestRedraw();
            using var ui = App.EnterUi();
            App.Step(wait: false);
        }

        public void Press(string key)
        {
            App.InjectKeys([KeyBinding.Parse(key).ToKeyInfo()]);
            using var ui = App.EnterUi();
            App.Step(wait: false);
        }

        public void Dispose() => App.EndRun();
    }

    // Advances the clock while drawing, so the flush takes a known time
    private sealed class SlowBackend(TestBackend inner, FakeTimeProvider time, TimeSpan delay) : ITerminalBackend
    {
        public (int Width, int Height) GetSize() => inner.GetSize();
        public void Enter() => inner.Enter();
        public void Leave() => inner.Leave();
        public void Dispose() => inner.Dispose();

        public void Draw(IReadOnlyList<CellUpdate> updates, (int X, int Y)? cursor, bool fullRedraw)
        {
            time.Advance(delay);
            inner.Draw(updates, cursor, fullRedraw);
        }
    }

    private static string Screen(ComponentHarness h) => string.Join("\n", h.Lines);

    [Fact]
    public void DebugKey_TogglesTheOverlay()
    {
        using var h = new ComponentHarness(new Panes(), 60, 20);
        h.App.WithDebugKey();

        h.Press("f12");
        Assert.True(h.App.ShowDebug);
        Assert.Contains("frame ", Screen(h));

        h.Press("f12");
        Assert.False(h.App.ShowDebug);
        Assert.DoesNotContain("frame ", Screen(h));
    }

    [Fact]
    public void WithoutTheDebugKey_F12ReachesTheFocusChain()
    {
        var screen = new Panes();
        using var h = new ComponentHarness(screen, 60, 20);

        h.Press("f12");

        Assert.False(h.App.ShowDebug);
        Assert.Equal([ConsoleKey.F12], screen.A.Got);
    }

    [Fact]
    public void DebugKey_WorksOverADialog_AndBeforeATextField()
    {
        var screen = new Panes(textField: true);
        using var h = new ComponentHarness(screen, 60, 20);
        h.App.WithDebugKey();

        h.Press("f12");
        Assert.True(h.App.ShowDebug);
        Assert.Empty(screen.A.Got);

        _ = h.App.Overlays.ConfirmAsync("Sure?");
        h.RunUntilIdle();
        h.Press("f12");
        Assert.False(h.App.ShowDebug);
        Assert.True(h.App.Overlays.Any);
    }

    [Fact]
    public void DebugKey_CanBeChosen_OrTurnedOff()
    {
        var screen = new Panes();
        using var h = new ComponentHarness(screen, 60, 20);

        h.App.WithDebugKey("ctrl+d");
        h.Press("ctrl+d");
        Assert.True(h.App.ShowDebug);

        h.App.WithDebugKey(null);
        h.Press("ctrl+d");
        Assert.True(h.App.ShowDebug);
        Assert.Equal([ConsoleKey.D], screen.A.Got);

        Assert.Throws<FormatException>(() => h.App.WithDebugKey("ctrl+"));
    }

    [Fact]
    public void ShowDebug_FromCode_Redraws()
    {
        using var h = new ComponentHarness(new Panes(), 60, 20);
        var before = h.App.LastFrame.Frame;

        h.App.ShowDebug = true;
        Assert.True(h.App.HasQueuedWork);
        h.RunUntilIdle();

        Assert.Contains("frame ", Screen(h));
        Assert.True(h.App.LastFrame.Frame > before);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData(null, false)]
    public void Environment_MinituiDebug1_ShowsTheOverlay_AndTurnsOnF12(string? value, bool on)
    {
        using var run = new ManualRun(new TestBackend(60, 20), new Panes(), env: n => n == "MINITUI_DEBUG" ? value : null);

        Assert.Equal(on, run.App.ShowDebug);
        run.Press("f12");
        Assert.False(run.App.ShowDebug);
    }

    [Fact]
    public void Environment_KeepsAKeyTheAppChose()
    {
        using var run = new ManualRun(new TestBackend(60, 20), new Panes(), env: _ => "1", configure: a => a.WithDebugKey("f2"));

        run.Press("f12");
        Assert.True(run.App.ShowDebug);
        run.Press("f2");
        Assert.False(run.App.ShowDebug);
    }

    [Fact]
    public void LastFrame_MeasuresRenderAndFlush()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var screen = new Panes { OnRender = () => time.Advance(TimeSpan.FromMilliseconds(3)) };
        using var run = new ManualRun(new SlowBackend(new TestBackend(10, 4), time, TimeSpan.FromMilliseconds(2)), screen, time);

        var first = run.App.LastFrame;
        Assert.Equal(new FrameStats(1, TimeSpan.FromMilliseconds(3), TimeSpan.FromMilliseconds(2), 40, null, true), first);

        run.Redraw();
        Assert.Equal(new FrameStats(2, TimeSpan.FromMilliseconds(3), TimeSpan.FromMilliseconds(2), 0, null, false), run.App.LastFrame);
    }

    [Fact]
    public void LastFrame_RenderTime_LeavesOutTheOverlay()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var screen = new Panes { OnRender = () => time.Advance(TimeSpan.FromMilliseconds(1)) };
        using var run = new ManualRun(new TestBackend(60, 20), screen, time);

        run.App.ShowDebug = true;
        run.Redraw();

        Assert.Equal(TimeSpan.FromMilliseconds(1), run.App.LastFrame.RenderTime);
    }

    [Fact]
    public void BytesWritten_ComeFromTheAnsiBackend()
    {
        var host = new FakeConsoleHost(10, 4);
        using var run = new ManualRun(new AnsiBackend(ColorMode.TrueColor, host), new Panes());

        // Tab moves focus to B, which redraws nothing; showing the overlay changes cells
        var start = host.Output.Length;
        run.App.ShowDebug = true;
        run.Redraw();

        Assert.Equal(host.Output.Length - start, run.App.LastFrame.BytesWritten);
        Assert.True(run.App.LastFrame.BytesWritten > 0);
    }

    [Fact]
    public void Outlines_PatchEdgeBackgrounds_KeepingSymbols()
    {
        using var h = new ComponentHarness(new Panes(), 20, 6);
        h.App.ShowDebug = true;
        h.RunUntilIdle();

        // B is plain, the screen is on the focus chain, A is focused
        Assert.Equal(("b", Color.DarkBlue), (h.CellAt(10, 2).Symbol, h.CellAt(10, 2).Bg));
        Assert.Equal(Color.DarkMagenta, h.CellAt(19, 2).Bg);
        Assert.Equal(Color.DarkMagenta, h.CellAt(15, 0).Bg);
        Assert.Equal(("a", Color.Black, Color.Yellow), (h.CellAt(0, 2).Symbol, h.CellAt(0, 2).Fg, h.CellAt(0, 2).Bg));
        Assert.Equal(Color.Yellow, h.CellAt(5, 4).Bg);
        Assert.Equal(Color.Yellow, h.CellAt(9, 2).Bg);
        Assert.Equal(Color.Reset, h.CellAt(5, 2).Bg);
        Assert.Equal(Color.Reset, h.CellAt(12, 2).Bg);
    }

    [Fact]
    public void Outlines_IncludeOverlays_AndFollowTheirFocus()
    {
        using var h = new ComponentHarness(new Panes(), 60, 20);
        h.App.ShowDebug = true;
        _ = h.App.Overlays.ConfirmAsync("Sure?");
        h.RunUntilIdle();

        var dialog = h.App.Overlays.Top!.Area;
        Assert.Contains("focus: ConfirmDialog", Screen(h));
        Assert.Equal(Color.Yellow, h.CellAt(dialog.X - 2, dialog.Y).Bg);
        Assert.Equal(Color.DarkBlue, h.CellAt(30, 19 - 1).Bg);
    }

    [Fact]
    public void Monochrome_UsesModifiers()
    {
        var host = new FakeConsoleHost(20, 6);
        using var run = new ManualRun(new AnsiBackend(ColorMode.None, host), new Panes());
        run.App.ShowDebug = true;
        run.Redraw();

        var vt = new VirtualTerminal(20, 6);
        vt.Feed(host.Output.ToArray());
        Assert.Equal((Color.Reset, Modifiers.Underlined), (vt.CellAt(10, 2).Bg, vt.CellAt(10, 2).Modifiers));
        Assert.Equal(Modifiers.Reversed, vt.CellAt(0, 2).Modifiers & Modifiers.Reversed);
    }

    [Fact]
    public void Panel_ShowsTheNumbers()
    {
        using var h = new ComponentHarness(new Panes(), 60, 20);
        h.App.ShowDebug = true;
        h.Press("f5");
        h.RunUntilIdle();

        var screen = Screen(h);
        Assert.Matches(@"frame \d+  render \d+\.\d ms  flush \d+\.\d ms", screen);
        Assert.Matches(@"avg \d+\.\d ms  max \d+\.\d ms  fps \d+", screen);
        Assert.Matches(@"cells \d+  bytes –", screen);
        Assert.Contains("60×20  colors unknown  braille", screen);
        Assert.Contains("focus: Panes › Probe", screen);
        Assert.Contains("screens 1  overlays 0  toasts 0", screen);
        Assert.DoesNotContain("under pointer", screen);

        // Top-right
        Assert.Equal("┐", h.CellAt(59, 0).Symbol);
    }

    [Fact]
    public void Panel_ShowsTheComponentUnderThePointer_AndMovesAwayFromIt()
    {
        var screen = new Panes();
        using var h = new ComponentHarness(screen, 60, 20);
        h.App.WithMouse();
        h.App.ShowDebug = true;
        h.RunUntilIdle();
        Assert.Contains("under pointer: –", Screen(h));

        h.Click(5, 10);
        Assert.Contains("under pointer: Probe (0, 0, 30×19)", Screen(h));

        // A click on the panel goes to the component below, and the panel moves to the bottom-left
        h.Click(58, 2);
        Assert.Equal(1, screen.B.Clicks);
        Assert.Contains("under pointer: Probe (30, 0, 30×19)", Screen(h));
        Assert.Equal("└", h.CellAt(0, 19).Symbol);
        Assert.NotEqual("┐", h.CellAt(59, 0).Symbol);
    }

    [Fact]
    public void Panel_TruncatesTheFocusChainFromTheLeft()
    {
        using var h = new ComponentHarness(new Panes(), 20, 20);
        h.App.ShowDebug = true;
        h.RunUntilIdle();

        Assert.Contains("focus: … › Probe", Screen(h));
    }

    [Fact]
    public void TheOverlay_NeverAsksForAFrame()
    {
        using var h = new ComponentHarness(new Panes(), 60, 20);
        h.App.ShowDebug = true;
        h.RunUntilIdle();
        var frame = h.App.LastFrame.Frame;

        using (h.App.EnterUi()) h.App.Step(wait: false);

        Assert.Equal(frame, h.App.LastFrame.Frame);
        Assert.False(h.App.HasQueuedWork);
    }

    [Fact]
    public void TheOverlay_NeverThrows_AtAnySize()
    {
        foreach (var width in new[] { 1, 2, 11, 12, 13, 30, 45, 120 })
        foreach (var height in new[] { 1, 2, 8, 9, 10, 40 })
        {
            using var h = new ComponentHarness(new Panes(), width, height);
            h.App.WithMouse();
            h.App.ShowDebug = true;
            h.RunUntilIdle();
            h.Click(width - 1, 0);
        }
    }

    [Fact]
    public void ARenderCallbackApp_GetsThePanel()
    {
        var backend = new TestBackend(60, 20);
        var app = App.Create().WithBackend(backend).WithInput(new ScriptedInputSource()).WithTickRate(null).CaptureConsoleOutput(false)
            .Render((buf, area) => buf.SetString(0, 0, "hi", Style.Default));
        app.GetEnvironment = _ => "1";
        app.StartRun(CancellationToken.None);
        try
        {
            using (app.EnterUi())
            {
                app.Setup();
                app.Step(wait: false);
            }
            Assert.Contains("focus: ", string.Join("\n", backend.Lines));
            Assert.Contains("screens 0", string.Join("\n", backend.Lines));
        }
        finally
        {
            app.EndRun();
        }
    }
}
