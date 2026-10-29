using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

// The §3.2 program from docs/SPEC.md, run in the ComponentHarness against a fake API (§9.6).
public class LargeAppProgramTests
{
    internal sealed record ServiceDto(int Id, string Name, string Status, string Version);

    internal sealed class ServicesClient
    {
        private readonly List<ServiceDto> _services =
            [.. Enumerable.Range(1, 20).Select(i => new ServiceDto(i, $"svc-{i}", i % 4 == 0 ? "down" : "up", $"1.{i}"))];

        public List<int> Deleted { get; } = [];

        public Task<IReadOnlyList<ServiceDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ServiceDto>>([.. _services]);

        public Task<ServiceDto> GetAsync(int id, CancellationToken ct) => Task.FromResult(_services.First(s => s.Id == id));

        public async Task DeleteAsync(int id, CancellationToken ct)
        {
            await Task.Yield();
            Deleted.Add(id);
            _services.RemoveAll(s => s.Id == id);
        }
    }

    // ---- §3.2, unchanged except for the namespace ----
    internal sealed class ServicesScreen(ServicesClient api) : Screen
    {
        readonly Loadable<IReadOnlyList<ServiceDto>> services = new();
        readonly SelectionState selection = new();
        public override string Title => "Services";

        protected override void OnMount()
        {
            Keys.Add("r", "Refresh", () => Load(services, api.ListAsync))
                .Add("enter", "Open", Open)
                .Add("d", "Delete", () => App.RunTask(_ => DeleteAsync()));
            Load(services, api.ListAsync);                 // cancelled automatically when the screen is popped
        }

        public override bool HandleKey(ConsoleKeyInfo key) =>
            selection.HandleKey(key, services.Value?.Count ?? 0);

        public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
            services.Switch(
                idle:    () => { },
                loading: stale => DrawTable(buf, area, ctx, stale),
                loaded:  list  => DrawTable(buf, area, ctx, list),
                failed:  ex    => Widget.Paragraph().Text(new Line(ex.Message, ctx.Theme.Error)).Draw(buf, area));

        void Open()
        {
            if (services.Value is { Count: > 0 } list)
                App.Screens.Push(new ServiceDetailScreen(api, list[selection.Selected].Id)); // Esc pops back
        }

        async Task DeleteAsync()
        {
            if (services.Value is not { Count: > 0 } list) return;
            var svc = list[selection.Selected];
            if (!await App.Overlays.ConfirmAsync($"Delete {svc.Name}?")) return;
            await api.DeleteAsync(svc.Id, Lifetime);       // continuation resumes on the UI thread
            App.Toasts.Push($"Deleted {svc.Name}", ToastLevel.Success);
            Load(services, api.ListAsync);
        }

        void DrawTable(Buffer buf, Rect area, RenderContext ctx, IReadOnlyList<ServiceDto>? list) =>
            Widget.Table()
                .Header("Name", "Status", "Version")
                .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12))
                .Rows(list ?? [], s => new Row(s.Name, s.Status, s.Version))
                .State(selection)
                .Focused(ctx.Focused)
                .Draw(buf, area);
    }
    // ---- end of §3.2 ----

    internal sealed class ServiceDetailScreen(ServicesClient api, int id) : Screen
    {
        private readonly Loadable<ServiceDto> _service = new();

        public override string Title => _service.Value?.Name ?? "…";

        protected override void OnMount() => Load(_service, ct => api.GetAsync(id, ct));

        public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
            Widget.Paragraph()
                .Text(_service.Value is { } s ? $"Id: {s.Id}\nName: {s.Name}\nStatus: {s.Status}" : "Loading…")
                .Block(Widget.Block().Title($" {Title} ").Focused(ctx.Focused))
                .Draw(buf, area);
    }

    [Fact]
    public void Section32_Program_TableDetailAndBack()
    {
        var api = new ServicesClient();
        using var harness = new ComponentHarness(new ServicesScreen(api), 60, 12);
        harness.App.Keys.Add("q", "Quit", harness.App.Quit);
        harness.RunUntilIdle();

        // The table, with hints and the breadcrumb in the chrome
        Assert.StartsWith("Name", harness.Lines[0], StringComparison.Ordinal);
        Assert.StartsWith("svc-1 ", harness.Lines[1], StringComparison.Ordinal);
        Assert.StartsWith(" r Refresh  enter Open  d Delete  ? Help  q Quit", harness.Lines[^1], StringComparison.Ordinal);
        Assert.EndsWith("Services ", harness.Lines[^1], StringComparison.Ordinal);

        // Enter opens the selected service
        harness.Press("j j enter");
        Assert.IsType<ServiceDetailScreen>(harness.App.Screens.Top);
        Assert.Contains(harness.Lines, l => l.Contains("Name: svc-3", StringComparison.Ordinal));
        Assert.EndsWith("Services › svc-3 ", harness.Lines[^1], StringComparison.Ordinal);

        // Esc returns with the selection unchanged
        harness.Press("esc");
        Assert.IsType<ServicesScreen>(harness.App.Screens.Top);
        Assert.StartsWith("svc-3 ", harness.Lines[3], StringComparison.Ordinal);
        Assert.Equal(Color.Cyan, harness.CellAt(0, 3).Bg);

        // d confirms, deletes, toasts and reloads
        harness.Press("d");
        Assert.IsType<ConfirmDialog>(harness.App.Overlays.Top);
        Assert.Contains(harness.Lines, l => l.Contains("Delete svc-3?", StringComparison.Ordinal));
        harness.Press("y");
        Assert.Equal([3], api.Deleted);
        Assert.Contains(harness.Lines, l => l.Contains("Deleted svc-3", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Lines, l => l.StartsWith("svc-3 ", StringComparison.Ordinal));

        harness.Press("q");
        Assert.False(harness.App.IsRunning);
    }
}
