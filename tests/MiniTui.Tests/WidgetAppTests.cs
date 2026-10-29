using Microsoft.Extensions.Time.Testing;
using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class WidgetAppTests
{
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + AppHarness.Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task Spinner_RequestsTicks_OnlyWhileDrawn()
    {
        var time = new FakeTimeProvider();
        var backend = new TestBackend(20, 2);
        var spinning = true;
        var ticks = 0;
        var app = AppHarness.Create(backend, new ScriptedInputSource())
            .WithTimeProvider(time)
            .OnEvent(e => { if (e is TickEvent) ticks++; })
            .Render((buf, area) => { if (spinning) Widget.Spinner().Frames(SpinnerFrames.Line).Draw(buf, area); });

        var run = AppHarness.Run(app);

        // A round trip through the UI thread means the frame's tick request has been applied
        await WaitFor(() => backend.DrawCount >= 1);
        await AppHarness.OnUi(app, () => 0);
        Assert.Equal("-", backend.Lines[0][..1]);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await WaitFor(() => backend.Lines[0].StartsWith('\\'));

        // OnUi completes before that iteration's frame, so a second round trip means the spinner-less frame was drawn.
        // Count ticks rather than frames: posted actions redraw too, but only the timer ticks.
        await AppHarness.OnUi(app, () => spinning = false);
        var before = await AppHarness.OnUi(app, () => ticks);
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        var after = await AppHarness.OnUi(app, () => ticks);
        app.Quit();
        await run;

        // With no tick rate configured, the ticks stop once the spinner is gone
        Assert.Equal(1, before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Toast_DisappearsOnItsOwn_WithoutConfiguredTicks()
    {
        var time = new FakeTimeProvider();
        var backend = new TestBackend(30, 5);
        var toasts = new ToastQueue();
        toasts.Push("Saved", ToastLevel.Success, TimeSpan.FromSeconds(1));
        var app = AppHarness.Create(backend, new ScriptedInputSource())
            .WithTimeProvider(time)
            .Render((buf, area) => Widget.Toasts().Queue(toasts).Draw(buf, area));

        var run = AppHarness.Run(app);
        await WaitFor(() => backend.Lines[1].Contains("Saved", StringComparison.Ordinal));
        await AppHarness.OnUi(app, () => 0);
        for (var i = 0; i < 5; i++) time.Advance(TimeSpan.FromMilliseconds(250));
        await WaitFor(() => !backend.Lines[1].Contains("Saved", StringComparison.Ordinal));
        app.Quit();
        await run;

        Assert.Equal(0, toasts.Count);
    }
}

// The §3.1 program from docs/SPEC.md, against a fake API, must compile and work.
public class SmallAppProgramTests
{
    private sealed record ServiceDto(string Name, string Status, string Version, bool Healthy);

    private sealed class ServicesClient(int count)
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ServiceDto>> ListAsync(CancellationToken ct)
        {
            Calls++;
            IReadOnlyList<ServiceDto> list = [.. Enumerable.Range(1, count).Select(i =>
                new ServiceDto($"svc-{i}", i % 5 == 0 ? "down" : "up", $"1.{i}", i % 5 != 0))];
            return Task.FromResult(list);
        }
    }

    [Fact]
    public async Task Section31_Program_LoadsNavigatesAndQuits()
    {
        var backend = new TestBackend(50, 10);
        var input = new ScriptedInputSource();
        var api = new ServicesClient(30);

        // ---- §3.1, unchanged except for the harness backend/input ----
        var services = new Loadable<IReadOnlyList<ServiceDto>>();
        var selection = new SelectionState();
        var toasts = new ToastQueue();
        var app = AppHarness.Create(backend, input);

        var keys = new KeyMap()
            .Add("r", "Refresh", () => services.Load(app, ct => api.ListAsync(ct)))
            .Add("j,down", "Down", () => selection.Next(services.Value?.Count ?? 0))
            .Add("k,up", "Up", () => selection.Previous(services.Value?.Count ?? 0))
            .Add("q,ctrl+c", "Quit", () => app.Quit());

        app.WithTheme(Theme.Default)
            .OnSetup(a => services.Load(a, ct => api.ListAsync(ct)))
            .OnKey(key => keys.Handle(key))
            .OnError(ex => { toasts.Push(ex.Message, ToastLevel.Error); return ErrorAction.Continue; })
            .Render((buf, screen) =>
            {
                var layout = Layout.Vertical(screen)
                    .Add("body", Constraint.Fill())
                    .Add("status", Constraint.Length(1))
                    .Build();

                services.Switch(
                    idle:    () => Widget.Paragraph().Text("Press r to load").Draw(buf, layout["body"]),
                    loading: stale => DrawTable(buf, layout["body"], stale, loading: true),
                    loaded:  list  => DrawTable(buf, layout["body"], list, loading: false),
                    failed:  ex    => Widget.Paragraph()
                                          .Text(new Line(ex.Message, Theme.Current.Error))
                                          .Wrap(WrapMode.Word)
                                          .Block(Widget.Block().Title(" Error "))
                                          .Draw(buf, layout["body"]));

                Widget.StatusBar().Left(keys.Hints(screen.Width / 2)).Draw(buf, layout["status"]);
                Widget.Toasts().Queue(toasts).Draw(buf, screen);
            });

        var run = AppHarness.Run(app);

        void DrawTable(Buffer buf, Rect area, IReadOnlyList<ServiceDto>? list, bool loading) =>
            Widget.Table()
                .Block(Widget.Block().Title(loading ? " Services (refreshing…) " : " Services "))
                .Header("Name", "Status", "Version")
                .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12))
                .Rows(list ?? [], s => new Row(
                    s.Name,
                    new Line(s.Status, s.Healthy ? Theme.Current.Success : Theme.Current.Error),
                    s.Version))
                .State(selection)
                .Focused()
                .Draw(buf, area);
        // ---- end of §3.1 ----

        await AppHarness.WaitUntil(app, () => services.Status == LoadStatus.Loaded);
        input.EnqueueText("jjjjjjjjjj");
        await AppHarness.WaitUntil(app, () => selection.Selected == 10);
        input.EnqueueText("r");
        await AppHarness.WaitUntil(app, () => api.Calls == 2 && services.Status == LoadStatus.Loaded);
        await AppHarness.WaitUntil(app, () => backend.Lines.Any(l => l.Contains("svc-11", StringComparison.Ordinal)));
        var screenLines = backend.Lines;
        input.EnqueueText("q");
        await run;

        Assert.StartsWith("┌ Services ", screenLines[0], StringComparison.Ordinal);
        Assert.Contains("Name", screenLines[1], StringComparison.Ordinal);
        Assert.StartsWith(" r Refresh", screenLines[9], StringComparison.Ordinal);
        var selected = Array.FindIndex(screenLines, l => l.Contains("svc-11", StringComparison.Ordinal));
        Assert.Equal(Color.Cyan, backend.CellAt(1, selected).Bg);
    }
}
