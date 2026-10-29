using System.Net;
using System.Net.Http.Json;
using MiniTui;

namespace MiniTui.Example;

// The §3.2 shape from docs/SPEC.md against a fake HTTP API: a Loadable table, Enter for a detail screen with its
// own Loadable, d to delete through ConfirmAsync, and Esc to go back.

internal sealed record ServiceDto(int Id, string Name, string Status, string Version, bool Healthy, int LatencyMs);

/// <summary>A client for the fake API. Every call takes 300–1,500 ms and fails 20% of the time.</summary>
internal sealed class ServicesClient(HttpClient http)
{
    public async Task<IReadOnlyList<ServiceDto>> ListAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<ServiceDto>>("services", ct) ?? [];

    public async Task<ServiceDto> GetAsync(int id, CancellationToken ct) =>
        await http.GetFromJsonAsync<ServiceDto>($"services/{id}", ct) ?? throw new HttpRequestException("Not found");

    public async Task DeleteAsync(int id, CancellationToken ct) =>
        (await http.DeleteAsync(new Uri($"services/{id}", UriKind.Relative), ct)).EnsureSuccessStatusCode();
}

/// <summary>The list of services.</summary>
internal sealed class ServicesScreen : Screen
{
    private readonly HttpClient _http = new(new FakeServicesHandler()) { BaseAddress = new Uri("http://services.local/") };
    private readonly ServicesClient _api;
    private readonly Loadable<IReadOnlyList<ServiceDto>> _services = new();
    private readonly SelectionState _selection = new() { SortColumn = 0 };

    public ServicesScreen() => _api = new ServicesClient(_http);

    public override string Title => "Services";

    protected override void OnMount()
    {
        Keys.Add("r", "Refresh", Refresh)
            .Add("enter", "Open", Open)
            .Add("d", "Delete", () => App.RunTask(_ => DeleteAsync()))
            .Add("s", "Sort", () => _selection.SortDescending = !_selection.SortDescending)
            .Add("c", "Cancel load", () => _services.Cancel());
        Refresh();
    }

    protected override void OnUnmount() => _http.Dispose();

    public override bool HandleKey(ConsoleKeyInfo key) => _selection.HandleKey(key, _services.Value?.Count ?? 0);

    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        _services.Switch(
            idle: () => { },
            loading: stale => DrawTable(buf, area, ctx, stale, loading: true),
            loaded: list => DrawTable(buf, area, ctx, list, loading: false),
            failed: ex => Widget.Paragraph()
                .Text(new Text([
                    new Line(ex.Message, ctx.Theme.Error),
                    "",
                    new Line("Press r to try again. The fake API fails 20% of the time.", ctx.Theme.TextMuted),
                ]))
                .Wrap(WrapMode.Word)
                .Block(Widget.Block().Title(" Error ").BorderStyle(ctx.Theme.Error))
                .Draw(buf, area));

    // A failed load shows in the table area; it also pops a toast
    private void Refresh() => Load(_services, async ct =>
    {
        try
        {
            return await _api.ListAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            App.Toasts.Push(ex.Message, ToastLevel.Error);
            throw;
        }
    });

    private IReadOnlyList<ServiceDto> Sorted(IReadOnlyList<ServiceDto> list) => _selection.SortDescending
        ? [.. list.OrderByDescending(s => s.Name, StringComparer.Ordinal)]
        : [.. list.OrderBy(s => s.Name, StringComparer.Ordinal)];

    private ServiceDto? Selected => _services.Value is { Count: > 0 } list ? Sorted(list)[Math.Min(_selection.Selected, list.Count - 1)] : null;

    private void Open()
    {
        if (Selected is { } service) App.Screens.Push(new ServiceDetailScreen(_api, service)); // Esc pops back
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } service) return;
        if (!await App.Overlays.ConfirmAsync($"Delete {service.Name}?")) return;
        try
        {
            await _api.DeleteAsync(service.Id, Lifetime);  // the continuation resumes on the UI thread
            App.Toasts.Push($"Deleted {service.Name}", ToastLevel.Success);
            Refresh();
        }
        catch (HttpRequestException ex)
        {
            App.Toasts.Push($"Couldn't delete {service.Name}: {ex.Message}", ToastLevel.Error);
        }
    }

    private void DrawTable(Buffer buf, Rect area, RenderContext ctx, IReadOnlyList<ServiceDto>? list, bool loading)
    {
        var rows = Sorted(list ?? []);
        var title = loading ? " Services (refreshing…) " : $" Services ({rows.Count}) ";
        Widget.Table()
            .Block(Widget.Block().Title(title))
            .Header("Name", "Status", "Version", "Latency")
            .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12), Constraint.Length(8))
            .Rows(rows, s => new Row(
                s.Name,
                new Line(s.Status, s.Healthy ? ctx.Theme.Success : ctx.Theme.Error),
                s.Version,
                new Line($"{s.LatencyMs} ms") with { Alignment = Alignment.Right }))
            .State(_selection)
            .HighlightSymbol("> ")
            .Focused(ctx.Focused)
            .Draw(buf, area);

        if (loading) Widget.Spinner().Label("loading").Draw(buf, new Rect(area.Right - 14, area.Bottom - 1, 12, 1));
    }
}

/// <summary>One service, loaded fresh when opened. Its load is cancelled if you go back first.</summary>
internal sealed class ServiceDetailScreen(ServicesClient api, ServiceDto summary) : Screen
{
    private readonly Loadable<ServiceDto> _service = new();

    public override string Title => summary.Name;

    protected override void OnMount()
    {
        Keys.Add("r", "Reload", () => Load(_service, ct => api.GetAsync(summary.Id, ct)));
        Load(_service, ct => api.GetAsync(summary.Id, ct));
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var inner = Widget.Block().Title($" {summary.Name} ").Padding(new Margin(2, 1)).Focused(ctx.Focused).Draw(buf, area);
        _service.Switch(
            idle: () => { },
            loading: _ => Widget.Spinner().Label("Loading details…").Draw(buf, inner),
            loaded: s => Widget.Paragraph()
                .Text(new Text([
                    new Line($"Id:       {s.Id}"),
                    new Line($"Name:     {s.Name}"),
                    new Line(["Status:   ", new Span(s.Status, s.Healthy ? ctx.Theme.Success : ctx.Theme.Error)]),
                    new Line($"Version:  {s.Version}"),
                    new Line($"Latency:  {s.LatencyMs} ms"),
                    "",
                    new Line("Esc goes back to the list.", ctx.Theme.TextMuted),
                ]))
                .Draw(buf, inner),
            failed: ex => Widget.Paragraph()
                .Text(new Text([new Line(ex.Message, ctx.Theme.Error), new Line("Press r to retry.", ctx.Theme.TextMuted)]))
                .Wrap(WrapMode.Word)
                .Draw(buf, inner));
    }
}

/// <summary>A fake API: 500 services, 300–1,500 ms latency, 20% failures; honours cancellation like a real request.</summary>
internal sealed class FakeServicesHandler : HttpMessageHandler
{
    private static readonly string[] Teams = ["billing", "search", "auth", "catalog", "orders", "media", "mail", "graph"];

    private readonly List<ServiceDto> _services = [.. Enumerable.Range(1, 500).Select(Create)];
    private readonly Lock _lock = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(300, 1500), cancellationToken);
        if (Random.Shared.Next(5) == 0) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var path = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        lock (_lock)
        {
            if (path is ["services"]) return Json(_services);
            if (path is ["services", var id] && int.TryParse(id, out var serviceId) && _services.Find(s => s.Id == serviceId) is { } service)
            {
                if (request.Method == HttpMethod.Delete)
                {
                    _services.Remove(service);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                return Json(service);
            }
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static ServiceDto Create(int i)
    {
        var healthy = Random.Shared.Next(10) != 0;
        return new ServiceDto(i, $"{Teams[i % Teams.Length]}-{i:000}", healthy ? "healthy" : "degraded",
            $"v{1 + i % 4}.{i % 13}.{Random.Shared.Next(20)}", healthy, Random.Shared.Next(2, healthy ? 120 : 900));
    }
}
