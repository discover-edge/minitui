using System.Net;
using System.Net.Http.Json;
using MiniTui;

// The §3.1 program from docs/SPEC.md against a fake HTTP API: Loadable, cancellation on refresh,
// error toasts, a 500-row table and the KeyMap help modal.
internal static class ServicesDemo
{
    public static void Run()
    {
        using var http = new HttpClient(new FakeServicesHandler()) { BaseAddress = new Uri("http://services.local/") };
        var api = new ServicesClient(http);
        var services = new Loadable<IReadOnlyList<ServiceDto>>();
        var selection = new SelectionState();
        var toasts = new ToastQueue();
        var help = new ScrollState();
        var showHelp = false;
        var app = App.Create();

        // A failed load shows in the table area; it also pops a toast
        void Refresh(App a) => services.Load(a, async ct =>
        {
            try
            {
                return await api.ListAsync(ct);
            }
            catch (HttpRequestException ex)
            {
                toasts.Push(ex.Message, ToastLevel.Error);
                throw;
            }
        });

        var keys = new KeyMap()
            .Add("r", "Refresh", () => Refresh(app))
            .Add("j,down", "Down", () => selection.Next(services.Value?.Count ?? 0), group: "Navigation", showInHints: false)
            .Add("k,up", "Up", () => selection.Previous(services.Value?.Count ?? 0), group: "Navigation", showInHints: false)
            .Add("pgdn", "Page down", () => selection.PageDown(services.Value?.Count ?? 0), group: "Navigation", showInHints: false)
            .Add("pgup", "Page up", () => selection.PageUp(services.Value?.Count ?? 0), group: "Navigation", showInHints: false)
            .Add("s", "Sort", () => selection.SortDescending = !selection.SortDescending)
            .Add("c", "Cancel load", () => services.Cancel())
            .Add("?", "Help", () => showHelp = true)
            .Add("q,esc", "Back", () => app.Quit());

        app.WithTheme(Theme.Default)
            .OnSetup(Refresh)
            .OnKey(key =>
            {
                if (!showHelp) return keys.Handle(key);
                if (key.Key is ConsoleKey.Escape or ConsoleKey.Enter || key.KeyChar is '?' or 'q') showHelp = false;
                else help.HandleKey(key);
                return true;
            })
            .OnError(ex => { toasts.Push(ex.Message, ToastLevel.Error); return ErrorAction.Continue; })
            .Render((buf, screen) =>
            {
                var layout = Layout.Vertical(screen)
                    .Add("body", Constraint.Fill())
                    .Add("status", Constraint.Length(1))
                    .Build();

                services.Switch(
                    idle: () => Widget.Paragraph().Text("Press r to load").Draw(buf, layout["body"]),
                    loading: stale => DrawTable(buf, layout["body"], stale, loading: true),
                    loaded: list => DrawTable(buf, layout["body"], list, loading: false),
                    failed: ex => DrawFailure(buf, layout["body"], ex));

                var loaded = services.LoadedAt is { } at ? $"loaded {at.ToLocalTime():HH:mm:ss}" : "";
                Widget.StatusBar().Left(keys.Hints(screen.Width - 20)).Right(loaded).Draw(buf, layout["status"]);
                if (showHelp)
                    Widget.Modal().Title(" Keys ").Lines([.. keys.HelpLines()]).State(help).Footer(" Esc to close ").Draw(buf);
                Widget.Toasts().Queue(toasts).Draw(buf, screen);
            })
            .Run();

        void DrawTable(MiniTui.Buffer buf, Rect area, IReadOnlyList<ServiceDto>? list, bool loading)
        {
            var rows = list ?? [];
            rows = selection.SortDescending
                ? [.. rows.OrderByDescending(s => s.Name, StringComparer.Ordinal)]
                : [.. rows.OrderBy(s => s.Name, StringComparer.Ordinal)];
            selection.SortColumn = 0;

            var title = loading ? " Services (refreshing…) " : $" Services ({rows.Count}) ";
            Widget.Table()
                .Block(Widget.Block().Title(title).Focused())
                .Header("Name", "Status", "Version", "Latency")
                .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12), Constraint.Length(8))
                .Rows(rows, s => new Row(
                    s.Name,
                    new Line(s.Status, s.Healthy ? Theme.Current.Success : Theme.Current.Error),
                    s.Version,
                    new Line($"{s.LatencyMs} ms") with { Alignment = Alignment.Right }))
                .State(selection)
                .HighlightSymbol("▶ ")
                .Focused()
                .Draw(buf, area);

            if (loading)
                Widget.Spinner().Label("loading").Draw(buf, new Rect(area.Right - 14, area.Bottom - 1, 12, 1));
        }

        void DrawFailure(MiniTui.Buffer buf, Rect area, Exception ex) =>
            Widget.Paragraph()
                .Text(new Text([
                    new Line(ex.Message, Theme.Current.Error),
                    "",
                    new Line("Press r to try again. The fake API fails 20% of the time.", Theme.Current.TextMuted),
                ]))
                .Wrap(WrapMode.Word)
                .Block(Widget.Block().Title(" Error ").BorderStyle(Theme.Current.Error))
                .Draw(buf, area);
    }

    private sealed record ServiceDto(string Name, string Status, string Version, bool Healthy, int LatencyMs);

    private sealed class ServicesClient(HttpClient http)
    {
        public async Task<IReadOnlyList<ServiceDto>> ListAsync(CancellationToken ct) =>
            await http.GetFromJsonAsync<List<ServiceDto>>("services", ct) ?? [];
    }

    // 300–1,500 ms latency, 20% failures, 500 services; honours cancellation like a real request
    private sealed class FakeServicesHandler : HttpMessageHandler
    {
        private static readonly string[] Teams = ["billing", "search", "auth", "catalog", "orders", "media", "mail", "graph"];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Random.Shared.Next(300, 1500), cancellationToken);
            if (Random.Shared.Next(5) == 0)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            var services = Enumerable.Range(1, 500).Select(i =>
            {
                var healthy = Random.Shared.Next(10) != 0;
                return new ServiceDto(
                    $"{Teams[i % Teams.Length]}-{i:000}",
                    healthy ? "healthy" : "degraded",
                    $"v{1 + i % 4}.{i % 13}.{Random.Shared.Next(20)}",
                    healthy,
                    Random.Shared.Next(2, healthy ? 120 : 900));
            }).ToList();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(services) };
        }
    }
}
