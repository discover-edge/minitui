// Also exercises APIs that are obsolete in 0.9 and removed in 1.0 (§9.7).
#pragma warning disable CS0618

using Microsoft.Extensions.Time.Testing;

namespace MiniTui.Tests;

public class AppLoopTests
{
    [Fact]
    public async Task Post_From8Threads_RunsEveryActionExactlyOnce_OnTheUiThread()
    {
        const int Threads = 8, PerThread = 1000;
        var runs = new int[Threads * PerThread];
        var offThread = 0;
        var done = 0;
        var uiThread = -1;

        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnSetup(() => uiThread = Environment.CurrentManagedThreadId)
            .Render((_, _) => { });
        var run = AppHarness.Run(app);
        await AppHarness.WaitUntil(app, () => uiThread != -1);

        var posters = Enumerable.Range(0, Threads).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < PerThread; i++)
            {
                var id = t * PerThread + i;
                app.Post(() =>
                {
                    runs[id]++;
                    if (Environment.CurrentManagedThreadId != uiThread) offThread++;
                    if (++done == runs.Length) app.Quit();
                });
            }
        })).ToArray();
        await Task.WhenAll(posters);
        await run;

        Assert.All(runs, count => Assert.Equal(1, count));
        Assert.Equal(0, offThread);
    }

    [Fact]
    public async Task AwaitInAPostedAction_ResumesOnTheUiThread()
    {
        var uiThread = -1;
        var resumedOn = -2;

        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnSetup(() =>
            {
                uiThread = Environment.CurrentManagedThreadId;
                app.Post(() => _ = DelayThenRecord());
            })
            .Render((_, _) => { });

        async Task DelayThenRecord()
        {
            await Task.Delay(1);
            resumedOn = Environment.CurrentManagedThreadId;
            app.Quit();
        }

        await AppHarness.Run(app);

        Assert.Equal(uiThread, resumedOn);
    }

    [Fact]
    public async Task Run_RestoresTheSynchronizationContext()
    {
        SynchronizationContext? inside = null;
        SynchronizationContext? after = null;
        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnSetup(() =>
            {
                inside = SynchronizationContext.Current;
                app.Quit();
            })
            .Render((_, _) => { });

        await Task.Factory.StartNew(() =>
        {
            app.Run();
            after = SynchronizationContext.Current;
        }, TaskCreationOptions.LongRunning).WaitAsync(AppHarness.Timeout);

        Assert.NotNull(inside);
        Assert.Null(after);
    }

    [Fact]
    public async Task ThrowingRender_WithDefaultOnError_LeavesTheTerminal_AndRethrows()
    {
        var backend = new TestBackend(20, 5);
        var app = AppHarness.Create(backend, new ScriptedInputSource())
            .Render((_, _) => throw new InvalidOperationException("boom"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AppHarness.Run(app));

        Assert.Equal("boom", error.Message);
        Assert.False(backend.IsEntered);
        Assert.Equal(1, backend.LeaveCount);
    }

    [Fact]
    public async Task OnError_Continue_KeepsRunning()
    {
        var errors = new List<Exception>();
        var renders = 0;
        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnError(ex =>
            {
                errors.Add(ex);
                app.RequestRedraw();
                return ErrorAction.Continue;
            })
            .Render((_, _) =>
            {
                if (++renders == 1) throw new InvalidOperationException("first frame");
                app.Quit();
            });

        await AppHarness.Run(app);

        Assert.Equal("first frame", Assert.Single(errors).Message);
        Assert.Equal(2, renders);
    }

    [Fact]
    public async Task OnError_Quit_ReturnsNormally()
    {
        var app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnError(_ => ErrorAction.Quit)
            .Render((_, _) => throw new InvalidOperationException());

        await AppHarness.Run(app);
    }

    [Fact]
    public async Task ThrowingOnError_IsRethrown()
    {
        var app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnError(_ => throw new ArgumentException("handler"))
            .Render((_, _) => throw new InvalidOperationException());

        var error = await Assert.ThrowsAsync<ArgumentException>(() => AppHarness.Run(app));
        Assert.Equal("handler", error.Message);
    }

    [Fact]
    public async Task RunTask_Exception_GoesToOnError_AndTheTaskCompletes()
    {
        var errors = new List<Exception>();
        Task? task = null;
        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnError(ex => { errors.Add(ex); return ErrorAction.Continue; })
            .OnSetup(() => task = app.RunTask(async ct =>
            {
                await Task.Delay(1, ct);
                throw new InvalidOperationException("work");
            }))
            .Render((_, _) => { });

        var run = AppHarness.Run(app);
        await AppHarness.WaitUntil(app, () => task is { IsCompleted: true });
        app.Quit();
        await run;

        Assert.Equal("work", Assert.Single(errors).Message);
        Assert.True(task!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RunTask_GetsStopping_WhichIsCancelledOnQuit()
    {
        CancellationToken token = default;
        var errors = 0;
        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnError(_ => { errors++; return ErrorAction.Continue; })
            .OnSetup(() => app.RunTask(async ct =>
            {
                token = ct;
                app.Quit();
                await Task.Delay(Timeout.Infinite, ct);
            }))
            .Render((_, _) => { });

        await AppHarness.Run(app);

        Assert.True(token.IsCancellationRequested);
        Assert.True(app.Stopping.IsCancellationRequested);
        Assert.Equal(0, errors);
    }

    [Fact]
    public async Task Ticks_FollowTheTimeProvider_AndRedraw()
    {
        var time = new FakeTimeProvider();
        var ticks = new List<TimeSpan>();
        var backend = new TestBackend(20, 5);
        var app = AppHarness.Create(backend, new ScriptedInputSource())
            .WithTimeProvider(time)
            .WithTickRate(TimeSpan.FromMilliseconds(250))
            .OnEvent(e => { if (e is TickEvent tick) ticks.Add(tick.Elapsed); })
            .Render((_, _) => { });

        var run = AppHarness.Run(app);
        await WaitFor(() => backend.DrawCount == 1);
        time.Advance(TimeSpan.FromMilliseconds(250));
        await WaitFor(() => backend.DrawCount == 2);
        time.Advance(TimeSpan.FromMilliseconds(250));
        await WaitFor(() => backend.DrawCount == 3);
        var tickList = await AppHarness.OnUi(app, () => ticks.ToArray());
        app.Quit();
        await run;

        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250)], tickList);
    }

    [Fact]
    public async Task CtrlC_Quits_UnlessOnKeyTakesIt()
    {
        var input = new ScriptedInputSource().Enqueue(AppHarness.CtrlC);
        await AppHarness.Run(AppHarness.Create(new TestBackend(20, 5), input).Render((_, _) => { }));

        var seen = 0;
        input.Enqueue(AppHarness.CtrlC, AppHarness.CtrlC);
        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), input)
            .OnKey(key =>
            {
                if (++seen == 2) app.Quit();
                return true;
            })
            .Render((_, _) => { });
        await AppHarness.Run(app);

        Assert.Equal(2, seen);
    }

    [Fact]
    public async Task OnEvent_SeesKeys_AndOnKeyHandledKeysSkipTheRouter()
    {
        var input = new ScriptedInputSource().EnqueueText("ab");
        var events = new List<Event>();
        var routed = new List<char>();
        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), input)
            .UseRouter(new InputRouter().Add("main", key =>
            {
                routed.Add(key.KeyChar);
                app.Quit();
            }))
            .OnEvent(events.Add)
            .OnKey(key => key.KeyChar == 'a')
            .Render((_, _) => { });

        await AppHarness.Run(app);

        Assert.Equal(['a', 'b'], events.OfType<KeyEvent>().Select(e => e.Key.KeyChar));
        Assert.Equal(['b'], routed);
    }

    [Fact]
    public async Task Resize_DeliversResizeEvent_AndRedrawsFully()
    {
        var backend = new TestBackend(20, 5);
        ResizeEvent? resized = null;
        App app = null!;
        app = AppHarness.Create(backend, new ScriptedInputSource())
            .OnEvent(e => { if (e is ResizeEvent r) resized = r; })
            .Render((buf, _) => { if (buf.Width == 30) app.Quit(); });

        var run = AppHarness.Run(app);
        await WaitFor(() => backend.DrawCount == 1);
        backend.Resize(30, 8);
        app.RequestRedraw();
        await run;

        Assert.Equal(new ResizeEvent(30, 8), resized);
        Assert.Equal(2, backend.FullRedrawCount);
    }

    [Fact]
    public async Task Size_IsTheTerminalSize_WhileRunning()
    {
        (int, int) size = default;
        App app = null!;
        app = AppHarness.Create(new TestBackend(33, 7), new ScriptedInputSource())
            .OnSetup(() => { size = app.Size; app.Quit(); })
            .Render((_, _) => { });

        await AppHarness.Run(app);

        Assert.Equal((33, 7), size);
        Assert.Equal((0, 0), app.Size);
    }

    [Fact]
    public async Task RunAsync_CancellationQuits()
    {
        using var cts = new CancellationTokenSource();
        var backend = new TestBackend(20, 5);
        var app = AppHarness.Create(backend, new ScriptedInputSource()).Render((_, _) => { });

        var run = app.RunAsync(cts.Token);
        await WaitFor(() => backend.DrawCount == 1);
        await cts.CancelAsync();
        await run.WaitAsync(AppHarness.Timeout);

        Assert.False(backend.IsEntered);
    }

    [Fact]
    public async Task OnSetup_WithApp_ReceivesTheApp()
    {
        App? received = null;
        var app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource()).Render((_, _) => { });
        app.OnSetup(a => { received = a; a.Quit(); });

        await AppHarness.Run(app);

        Assert.Same(app, received);
    }

    [Fact]
    public async Task WithFrameDelay_MapsToTheTickRate()
    {
        var time = new FakeTimeProvider();
        var backend = new TestBackend(20, 5);
        var app = AppHarness.Create(backend, new ScriptedInputSource()).WithTimeProvider(time).WithFrameDelay(50)
            .Render((_, _) => { });

        var run = AppHarness.Run(app);
        await WaitFor(() => backend.DrawCount == 1);
        time.Advance(TimeSpan.FromMilliseconds(50));
        await WaitFor(() => backend.DrawCount == 2);
        app.Quit();
        await run;
    }

    [Fact]
    public async Task NestedApp_GetsTheKeysTheOuterAppHasNotHandled_AndReturnsTheRest()
    {
        // The Example's pattern: the outer menu starts a demo app from its RunWhile condition
        var input = new ScriptedInputSource().EnqueueText("ab\rcdqef").Enqueue(AppHarness.Key('q'));
        var backend = new TestBackend(20, 5);
        var outer = new List<char>();
        var inner = new List<char>();

        var app = AppHarness.Create(backend, input).Render((_, _) => { });
        await AppHarness.RunWhile(app, () =>
        {
            var key = Input.Poll();
            if (key is null) return true;
            if (key.Value.KeyChar == 'q') return false;
            outer.Add(key.Value.KeyChar);
            if (key.Value.Key == ConsoleKey.Enter)
            {
                AppHarness.Create(backend, input).Render((_, _) => { }).RunWhile(() =>
                {
                    var k = Input.Poll();
                    if (k?.KeyChar == 'q') return false;
                    if (k is not null) inner.Add(k.Value.KeyChar);
                    return true;
                });
            }
            return true;
        });

        Assert.Equal(['a', 'b', '\r', 'e', 'f'], outer);
        Assert.Equal(['c', 'd'], inner);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + AppHarness.Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(5);
        }
    }
}
