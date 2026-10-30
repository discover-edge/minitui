using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Suspend, Ctrl+Z and job control (1.x spec §5). Console.Out is swapped, so these don't run in parallel with others that use it.</summary>
[Collection("Console")]
public class SuspendTests
{
    private sealed class PlainScreen : Screen
    {
        public override void Render(Buffer buf, Rect area, RenderContext ctx) => buf.SetString(0, 0, "screen", Style.Default);
    }

    private sealed class Dialog : Overlay
    {
        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    private static App Create(TestBackend backend, ScriptedInputSource input, List<Event> events) =>
        AppHarness.Create(backend, input).Render((buf, _) => buf.SetString(0, 0, "app", Style.Default)).OnEvent(events.Add);

    // Suspend

    [Fact]
    public async Task Suspend_RunsTheActionWithTheTerminalGivenBack_ThenReentersAndRedraws()
    {
        var backend = new TestBackend(20, 5);
        var input = new ScriptedInputSource();
        var events = new List<Event>();
        var app = Create(backend, input, events).CaptureConsoleOutput();
        var original = Console.Out;
        bool? enteredDuring = null;
        TextWriter? outDuring = null;
        var run = AppHarness.Run(app);
        await AppHarness.WaitUntil(app, () => backend.DrawCount > 0);
        var fullRedraws = backend.FullRedrawCount;

        await AppHarness.OnUi(app, () =>
        {
            app.Suspend(() =>
            {
                enteredDuring = backend.IsEntered;
                outDuring = Console.Out;
            });
            return 0;
        });
        await AppHarness.WaitUntil(app, () => backend.FullRedrawCount > fullRedraws);
        var capturedAfter = await AppHarness.OnUi(app, () => !ReferenceEquals(Console.Out, original));
        app.Quit();
        await run;

        Assert.False(enteredDuring);
        Assert.Same(original, outDuring);
        Assert.True(capturedAfter);
        Assert.Equal(2, backend.EnterCount);
        Assert.Single(events.OfType<ResumeEvent>());
        Assert.Equal("app", backend.Lines[0].TrimEnd());
    }

    [Fact]
    public async Task Suspend_InputDuringTheAction_IsHandledAfterIt_BehindKeysReadBefore()
    {
        var backend = new TestBackend(20, 5);
        var input = new ScriptedInputSource().EnqueueText("ab");
        var events = new List<Event>();
        var keys = new List<char>();
        var keysDuringAction = -1;
        var app = Create(backend, input, events);
        app.OnKey(key =>
        {
            keys.Add(key.KeyChar);
            if (key.KeyChar == 'a')
            {
                app.Suspend(() =>
                {
                    input.EnqueueText("c");
                    Thread.Sleep(50);
                    keysDuringAction = keys.Count;
                });
            }
            if (key.KeyChar == 'c') app.Quit();
            return true;
        });

        await AppHarness.Run(app);

        Assert.Equal(1, keysDuringAction);
        Assert.Equal(['a', 'b', 'c'], keys);
    }

    [Fact]
    public async Task Suspend_ActionThrows_Resumes_AndTheExceptionGoesToOnError()
    {
        var backend = new TestBackend(20, 5);
        var events = new List<Event>();
        var errors = new List<Exception>();
        var app = Create(backend, new ScriptedInputSource(), events).OnError(ex =>
        {
            errors.Add(ex);
            return ErrorAction.Continue;
        });
        var run = AppHarness.Run(app);
        await AppHarness.WaitUntil(app, () => backend.DrawCount > 0);

        app.Post(() => app.Suspend(() => throw new InvalidOperationException("boom")));
        await AppHarness.WaitUntil(app, () => errors.Count > 0);
        var entered = backend.IsEntered;
        app.Quit();
        await run;

        Assert.Equal("boom", Assert.Single(errors).Message);
        Assert.True(entered);
        Assert.Single(events.OfType<ResumeEvent>());
    }

    [Fact]
    public async Task Suspend_OffTheUiThread_OrNotRunning_Throws()
    {
        var backend = new TestBackend(20, 5);
        var app = Create(backend, new ScriptedInputSource(), []);
        Assert.Throws<InvalidOperationException>(() => app.Suspend(() => { }));
        Assert.Throws<InvalidOperationException>(() => app.SuspendProcess());

        var run = AppHarness.Run(app);
        await AppHarness.WaitUntil(app, () => backend.DrawCount > 0);
        Assert.Throws<InvalidOperationException>(() => app.Suspend(() => { }));
        app.Quit();
        await run;
    }

    [Fact]
    public async Task Suspend_Nested_RunsTheInnerActionDirectly()
    {
        var backend = new TestBackend(20, 5);
        var events = new List<Event>();
        var app = Create(backend, new ScriptedInputSource(), events);
        var ran = false;
        var run = AppHarness.Run(app);
        await AppHarness.WaitUntil(app, () => backend.DrawCount > 0);

        await AppHarness.OnUi(app, () =>
        {
            app.Suspend(() => app.Suspend(() => ran = true));
            return 0;
        });
        app.Quit();
        await run;

        Assert.True(ran);
        Assert.Equal(2, backend.LeaveCount); // one suspend, then the exit
        Assert.Single(events.OfType<ResumeEvent>());
    }

    [Fact]
    public async Task Suspend_StartingAnAppInsideTheAction_Throws()
    {
        var backend = new TestBackend(20, 5);
        var app = Create(backend, new ScriptedInputSource(), []);
        Exception? error = null;
        var run = AppHarness.Run(app);
        await AppHarness.WaitUntil(app, () => backend.DrawCount > 0);

        await AppHarness.OnUi(app, () =>
        {
            app.Suspend(() =>
            {
                var inner = AppHarness.Create(backend, new ScriptedInputSource()).Render((_, _) => { });
                error = Record.Exception(inner.Run);
            });
            return 0;
        });
        var entered = backend.IsEntered;
        app.Quit();
        await run;

        Assert.IsType<InvalidOperationException>(error);
        Assert.True(entered);
    }

    [Fact]
    public async Task Suspend_FromANestedApp_LeavesOnce_AndTheOuterAppRedrawsFully()
    {
        var backend = new TestBackend(20, 5);
        var input = new ScriptedInputSource().EnqueueText("n");
        bool? enteredDuring = null;
        var app = AppHarness.Create(backend, input).Render((buf, _) => buf.SetString(0, 0, "outer", Style.Default));
        app.OnKey(key =>
        {
            if (key.KeyChar == 'q') app.Quit();
            if (key.KeyChar != 'n') return true;
            var nested = AppHarness.Create(backend, input).Render((buf, _) => buf.SetString(0, 0, "inner", Style.Default));
            nested.OnKey(k =>
            {
                if (k.KeyChar == 's') nested.Suspend(() => enteredDuring = backend.IsEntered);
                if (k.KeyChar == 'x') nested.Quit();
                return true;
            });
            input.EnqueueText("sx");
            nested.Run();
            return true;
        });
        var run = AppHarness.Run(app);
        await AppHarness.WaitUntil(app, () => enteredDuring is not null && backend.Lines[0].StartsWith("outer", StringComparison.Ordinal));
        var leaves = backend.LeaveCount;
        app.Quit();
        await run;

        Assert.False(enteredDuring);
        Assert.Equal(1, leaves);
        Assert.Equal(2, backend.EnterCount);
    }

    // Ctrl+Z

    private static ComponentHarness CtrlZHarness(out List<int> stops, bool suspendKey = true)
    {
        var harness = new ComponentHarness(new PlainScreen());
        var calls = new List<int>();
        stops = calls;
        harness.App.WithSuspendKey(suspendKey);
        harness.App.CanStopProcessOverride = true;
        harness.App.StopProcessAction = () => calls.Add(1);
        return harness;
    }

    [Fact]
    public void CtrlZ_WithoutWithSuspendKey_DoesNothing()
    {
        using var harness = CtrlZHarness(out var stops, suspendKey: false);

        harness.Press("ctrl+z");

        Assert.Empty(stops);
        Assert.True(harness.App.IsRunning);
    }

    [Fact]
    public void CtrlZ_Unhandled_StopsTheProcess_InsideASuspend()
    {
        using var harness = CtrlZHarness(out var stops);
        var resumes = 0;
        harness.App.OnEvent(e => resumes += e is ResumeEvent ? 1 : 0);

        harness.Press("ctrl+z");

        Assert.Single(stops);
        Assert.Equal(1, resumes);
        Assert.Equal("screen", harness.Lines[0].TrimEnd());
    }

    [Fact]
    public void CtrlZ_ABindingForIt_WinsOverSuspending()
    {
        using var harness = CtrlZHarness(out var stops);
        var undos = 0;
        harness.App.Keys.Add("ctrl+z", "Undo", () => undos++);

        harness.Press("ctrl+z");

        Assert.Empty(stops);
        Assert.Equal(1, undos);
    }

    [Fact]
    public void CtrlZ_WithAnOverlayOpen_DoesNothing()
    {
        using var harness = CtrlZHarness(out var stops);
        harness.App.Overlays.Show(new Dialog());
        harness.RunUntilIdle();

        harness.Press("ctrl+z");

        Assert.Empty(stops);
    }

    [Fact]
    public void SuspendProcess_WithoutJobControl_ReturnsFalse_AndDoesNothing()
    {
        using var harness = CtrlZHarness(out var stops);
        harness.App.CanStopProcessOverride = false;

        harness.Press("ctrl+z");

        Assert.Empty(stops);
        harness.App.CanStopProcessOverride = null; // a TestBackend isn't the console
        using (harness.App.EnterUi()) Assert.False(harness.App.SuspendProcess());
        Assert.Empty(stops);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task CtrlZ_InARenderCallbackApp_SuspendsAfterOnKey(bool onKeyHandles, int expectedStops)
    {
        var backend = new TestBackend(20, 5);
        var input = new ScriptedInputSource().Enqueue(new ConsoleKeyInfo('\x1a', ConsoleKey.Z, false, false, true), AppHarness.Key('q'));
        var stops = 0;
        var app = AppHarness.Create(backend, input).Render((_, _) => { }).WithSuspendKey();
        app.CanStopProcessOverride = true;
        app.StopProcessAction = () => stops++;
        app.OnKey(key =>
        {
            if (key.KeyChar == 'q') app.Quit();
            return key.KeyChar == 'q' || onKeyHandles;
        });

        await AppHarness.Run(app);

        Assert.Equal(expectedStops, stops);
    }

    // Signals

    [Fact]
    public void StopSignal_GoesToTheMostRecentListener_AndIsCancelledOnlyWhenOneListens()
    {
        var calls = new List<string>();
        Assert.False(TerminalSession.HandleStopSignal());

        using (TerminalSession.ListenForJobControl(() => calls.Add("outer stop"), () => calls.Add("outer cont")))
        {
            using (TerminalSession.ListenForJobControl(() => calls.Add("inner stop"), () => calls.Add("inner cont")))
            {
                Assert.True(TerminalSession.HandleStopSignal());
                TerminalSession.HandleContinueSignal(suspended: false);
                TerminalSession.HandleContinueSignal(suspended: true);
            }
            Assert.True(TerminalSession.HandleStopSignal());
        }

        Assert.Equal(["inner stop", "inner cont", "outer stop"], calls);
        Assert.False(TerminalSession.HandleStopSignal());
    }

    [Fact]
    public void OnStopSignal_SuspendsTheProcess_EvenWithoutWithSuspendKey()
    {
        using var harness = CtrlZHarness(out var stops, suspendKey: false);

        harness.App.OnStopSignal();
        harness.RunUntilIdle();

        Assert.Single(stops);
    }

    [Fact]
    public void OnContinueSignal_SendsAResumeEvent()
    {
        using var harness = CtrlZHarness(out _);
        var resumes = 0;
        harness.App.OnEvent(e => resumes += e is ResumeEvent ? 1 : 0);

        harness.App.OnContinueSignal();
        harness.RunUntilIdle();

        Assert.Equal(1, resumes);
    }

    // Input pause

    private sealed class CountingSource : IInputSource, IInputLifecycle
    {
        private int _reads;
        public int Suspends, Resumes;
        public ManualResetEventSlim? Block { get; set; }
        public ManualResetEventSlim Reading { get; } = new();
        public int Reads => Volatile.Read(ref _reads);
        public void Activate() { }
        public void Deactivate() { }
        public void Suspend() => Suspends++;
        public void Resume() => Resumes++;
        public bool TryRead(out ConsoleKeyInfo key)
        {
            Interlocked.Increment(ref _reads);
            Reading.Set();
            Block?.Wait();
            key = default;
            return false;
        }
        public void Dispose() { }
    }

    [Fact]
    public void InputPause_StopsReading_Nests_AndResumes()
    {
        var source = new CountingSource();
        using var subscription = InputPump.Subscribe(source, _ => { });
        Assert.True(SpinWait.SpinUntil(() => source.Reads > 0, AppHarness.Timeout));

        var outer = InputPump.Pause(source);
        var inner = InputPump.Pause(source);
        var paused = source.Reads;
        Thread.Sleep(50);
        Assert.Equal(paused, source.Reads);
        Assert.Equal(1, source.Suspends);

        inner.Dispose();
        Thread.Sleep(30);
        Assert.Equal(paused, source.Reads);
        Assert.Equal(0, source.Resumes);

        outer.Dispose();
        outer.Dispose();
        Assert.Equal(1, source.Resumes);
        Assert.True(SpinWait.SpinUntil(() => source.Reads > paused, AppHarness.Timeout));
    }

    [Fact]
    public async Task InputPause_WaitsForAReadInProgress()
    {
        using var block = new ManualResetEventSlim();
        var source = new CountingSource { Block = block };
        using var subscription = InputPump.Subscribe(source, _ => { });
        Assert.True(source.Reading.Wait(AppHarness.Timeout));

        var pausing = Task.Run(() => InputPump.Pause(source));
        await Task.Delay(50);
        Assert.False(pausing.IsCompleted);
        source.Block = null;
        block.Set();
        var pause = await pausing.WaitAsync(AppHarness.Timeout);

        pause.Dispose();
    }
}
