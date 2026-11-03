# Async data and HTTP

Load data without blocking the UI: results come back on the UI thread.

Load data with a `Loadable<T>` and draw each state. Loads run off the UI thread; their results, and every
`await` in `RunTask`, come back on it.

```csharp
var services = new Loadable<IReadOnlyList<ServiceDto>>();
var selection = new SelectionState();
var toasts = new ToastQueue();
var app = App.Create();

var keys = new KeyMap()
    .Add("r", "Refresh", () => services.Load(app, ct => api.ListAsync(ct)))   // cancels a load in flight
    .Add("j,down", "Down", () => selection.Next(services.Value?.Count ?? 0))
    .Add("k,up", "Up", () => selection.Previous(services.Value?.Count ?? 0))
    .Add("q,ctrl+c", "Quit", () => app.Quit());

app.OnSetup(a => services.Load(a, ct => api.ListAsync(ct)))
    .OnKey(keys.Handle)
    .OnError(ex => { toasts.Push(ex.Message, ToastLevel.Error); return ErrorAction.Continue; })
    .Render((buf, screen) =>
    {
        var layout = Layout.Vertical(screen).Add("body", Constraint.Fill()).Add("status", Constraint.Length(1)).Build();

        services.Switch(
            idle:    () => Widget.Paragraph().Text("Press r to load").Draw(buf, layout["body"]),
            loading: stale => DrawTable(buf, layout["body"], stale),     // the previous value, if any
            loaded:  list  => DrawTable(buf, layout["body"], list),
            failed:  ex    => Widget.Paragraph().Text(new Line(ex.Message, Theme.Current.Error)).Wrap(WrapMode.Word)
                                  .Block(Widget.Block().Title(" Error ")).Draw(buf, layout["body"]));

        Widget.StatusBar().Left(keys.Hints(screen.Width / 2)).Draw(buf, layout["status"]);
        Widget.Toasts().Queue(toasts).Draw(buf, screen);
    })
    .Run();

void DrawTable(Buffer buf, Rect area, IReadOnlyList<ServiceDto>? list) =>
    Widget.Table()
        .Block(Widget.Block().Title(" Services "))
        .Header("Name", "Status", "Version")
        .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12))
        .Rows(list ?? [], s => new Row(s.Name, new Line(s.Status, s.Healthy ? Theme.Current.Success : Theme.Current.Error), s.Version))
        .State(selection)
        .Focused()
        .Draw(buf, area);
```

`api` above is any client with an async method. To try the sample without a server, a stand-in that's slow and
sometimes fails, as a real API is:

```csharp
var api = new ServicesClient();

record ServiceDto(string Name, string Status, string Version, bool Healthy);

sealed class ServicesClient
{
    public async Task<IReadOnlyList<ServiceDto>> ListAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), ct);   // a real one would call HttpClient here, passing ct
        if (Random.Shared.Next(5) == 0) throw new HttpRequestException("503 Service Unavailable");
        return [new("auth", "healthy", "v1.4", true), new("billing", "degraded", "v2.0", false)];
    }
}
```

`Loadable<T>` holds `Status` (`Idle`, `Loading`, `Loaded`, `Failed`), the last `Value` (kept while
reloading and after a failure), `Error` and `LoadedAt`. State changes happen on the UI thread and redraw.
A newer load replaces an older one, and a failed fetch sets `Failed` instead of going to `OnError`. Pass a
`lifetime` token (or use `Component.Load`) to ignore results once their owner is gone.
