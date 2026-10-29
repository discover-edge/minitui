namespace MiniTui.Tests;

// Regression tests for the loop defects in docs/SPEC.md §2 (D3, D4, D12, D16) and the §6.3 acceptance list.
public class LoopDefectTests
{
    [Fact]
    public async Task D3_ThousandKeys_AreDeliveredInOrder_WithinFourFrames()
    {
        var keys = Enumerable.Range(0, 1000).Select(i => AppHarness.Key((char)('a' + i % 26))).ToArray();
        var input = new ScriptedInputSource().Enqueue(keys);
        var backend = new TestBackend(20, 5);
        var received = new List<ConsoleKeyInfo>();
        var sinceLastFrame = 0;
        var framesWithKeys = 0;

        App app = null!;
        app = AppHarness.Create(backend, input)
            .OnKey(key =>
            {
                received.Add(key);
                sinceLastFrame++;
                return true;
            })
            .Render((_, _) =>
            {
                if (sinceLastFrame > 0) framesWithKeys++;
                sinceLastFrame = 0;
                if (received.Count == keys.Length) app.Quit();
            });

        await AppHarness.Run(app);

        Assert.Equal(keys, received);
        Assert.InRange(framesWithKeys, 1, 4);
    }

    [Fact]
    public async Task D4_RunWhile_ConditionPollingEveryKey_LosesNone()
    {
        // The README pattern: poll in the condition, quit on q
        var input = new ScriptedInputSource().Enqueue(Enumerable.Repeat(AppHarness.Key('k'), 999).ToArray()).Enqueue(AppHarness.Key('q'));
        var count = 0;
        var app = AppHarness.Create(new TestBackend(20, 5), input).Render((_, _) => { });

        await AppHarness.RunWhile(app, () =>
        {
            var key = Input.Poll();
            if (Input.IsQuit(key)) return false;
            if (key?.KeyChar == 'k') count++;
            return true;
        });

        Assert.Equal(999, count);
    }

    [Fact]
    public async Task D4_RunWhile_KeysTheConditionDoesNotTake_GoToTheRouter_AndNoneTwice()
    {
        // 1,000 distinct keys, so a key delivered to both the condition and the router shows up
        var keys = Enumerable.Range(0, 1000).Select(i => AppHarness.Key((char)(0x100 + i), ConsoleKey.A)).ToArray();
        var input = new ScriptedInputSource().Enqueue(keys);
        var taken = new List<char>();
        var routed = new List<char>();
        var poll = false;
        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), input)
            .UseRouter(new InputRouter().Add("main", key =>
            {
                routed.Add(key.KeyChar);
                if (key.KeyChar == keys[^1].KeyChar) app.Quit();
            }))
            .Render((_, _) => { });

        // The condition takes every other key it sees
        await AppHarness.RunWhile(app, () =>
        {
            poll = !poll;
            if (poll && Input.Poll() is { } key)
            {
                taken.Add(key.KeyChar);
                if (key.KeyChar == keys[^1].KeyChar) return false;
            }
            return true;
        });

        Assert.Empty(taken.Intersect(routed));
        Assert.Equal(keys.Select(k => k.KeyChar).Order(), taken.Concat(routed).Order());
        Assert.NotEmpty(taken);
        Assert.NotEmpty(routed);
    }

    [Fact]
    public async Task D12_ModalOpenedViaPost_IsActiveAtTheNextRender_WithoutAKeyPress()
    {
        var showModal = false;
        var router = new InputRouter().Add("main", _ => { }).WithModal(() => showModal, _ => { });
        bool? activeWhenRendered = null;

        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .UseRouter(router)
            .OnSetup(() => app.Post(() => showModal = true))
            .Render((_, _) =>
            {
                if (!showModal) return;
                activeWhenRendered = router.IsModalActive;
                app.Quit();
            });

        await AppHarness.Run(app);

        Assert.True(activeWhenRendered);
    }

    [Fact]
    public async Task D16_NoInputAndNoTicks_RendersOnlyTheFirstFrame()
    {
        var backend = new TestBackend(20, 5);
        var app = AppHarness.Create(backend, new ScriptedInputSource()).Render((_, _) => { });

        var run = AppHarness.Run(app);
        var deadline = DateTime.UtcNow + AppHarness.Timeout;
        while (backend.DrawCount == 0 && DateTime.UtcNow < deadline) await Task.Delay(5);
        await Task.Delay(300);
        var framesWhileIdle = backend.DrawCount;
        app.Quit();
        await run;

        Assert.Equal(1, framesWhileIdle);
        Assert.Equal(1, backend.DrawCount);
    }
}
