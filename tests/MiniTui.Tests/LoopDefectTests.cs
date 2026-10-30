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

    // D4 was RunWhile's condition and the loop both polling, so keys were lost. 1.0 has one path for keys
    // (OnKey, then routing), so these check that no key is lost or delivered twice along it.

    [Fact]
    public async Task D4_KeyMapOnAScreen_LosesNoKeys()
    {
        var input = new ScriptedInputSource().Enqueue(Enumerable.Repeat(AppHarness.Key('k'), 999).ToArray()).Enqueue(AppHarness.Key('q'));
        var count = 0;
        var app = AppHarness.Create(new TestBackend(20, 5), input);
        var screen = new KeyScreen();
        screen.Keys.Add("k", "Count", () => count++);
        app.Keys.Add("q", "Quit", app.Quit);
        app.Screens.Push(screen);

        await AppHarness.Run(app);

        Assert.Equal(999, count);
    }

    [Fact]
    public async Task D4_KeysOnKeyDoesNotTake_GoToTheScreen_AndNoneTwice()
    {
        // 1,000 distinct keys, so a key delivered to both OnKey and the screen shows up
        var keys = Enumerable.Range(0, 1000).Select(i => AppHarness.Key((char)(0x100 + i), ConsoleKey.A)).ToArray();
        var input = new ScriptedInputSource().Enqueue(keys);
        var taken = new List<char>();
        var routed = new List<char>();
        var app = AppHarness.Create(new TestBackend(20, 5), input);
        var screen = new KeyScreen
        {
            OnKeyPress = key =>
            {
                routed.Add(key.KeyChar);
                if (key.KeyChar == keys[^1].KeyChar) app.Quit();
                return true;
            },
        };
        app.Screens.Push(screen);
        var take = false;
        app.OnKey(key =>
        {
            take = !take;
            if (!take) return false;
            taken.Add(key.KeyChar);
            if (key.KeyChar == keys[^1].KeyChar) app.Quit();
            return true;
        });

        await AppHarness.Run(app);

        Assert.Empty(taken.Intersect(routed));
        Assert.Equal(keys.Select(k => k.KeyChar).Order(), taken.Concat(routed).Order());
    }

    [Fact]
    public async Task D12_OverlayShownViaPost_IsActiveAndDrawnAtTheNextRender_WithoutAKeyPress()
    {
        var backend = new TestBackend(40, 10);
        bool? activeWhenRendered = null;

        App app = null!;
        app = AppHarness.Create(backend, new ScriptedInputSource())
            .OnSetup(() => app.Post(() => _ = app.Overlays.ShowMessageAsync("Note", "posted")))
            .Render((_, _) =>
            {
                if (!app.Overlays.Any) return;
                activeWhenRendered = true;
                app.Post(app.Quit);
            });

        await AppHarness.Run(app);

        Assert.True(activeWhenRendered);
        Assert.Contains(backend.Lines, l => l.Contains("posted", StringComparison.Ordinal));
    }

    private sealed class KeyScreen : Screen
    {
        public Func<ConsoleKeyInfo, bool>? OnKeyPress { get; init; }
        public override bool HandleKey(ConsoleKeyInfo key) => OnKeyPress?.Invoke(key) == true;
        public override void Render(MiniTui.Buffer buf, Rect area, RenderContext ctx) { }
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
