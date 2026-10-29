namespace MiniTui;

/// <summary>
/// Where a <see cref="Loadable{T}"/> is in its load cycle.
/// </summary>
public enum LoadStatus
{
    /// <summary>Never loaded.</summary>
    Idle,

    /// <summary>A load is in flight. <see cref="Loadable{T}.Value"/> still holds the previous value, if any.</summary>
    Loading,

    /// <summary>The last load succeeded.</summary>
    Loaded,

    /// <summary>The last load failed. <see cref="Loadable{T}.Error"/> says why; the previous value is kept.</summary>
    Failed,
}

/// <summary>
/// The state of an async load, for rendering loading, loaded and failed states (§6.4).
/// State only changes on the app's UI thread, and every change requests a redraw.
/// </summary>
public sealed class Loadable<T>
{
    private App? _app;
    private CancellationTokenSource? _cts;
    private int _generation;
    private LoadStatus _statusBeforeLoad;

    /// <summary>Where the load cycle is.</summary>
    public LoadStatus Status { get; private set; }

    /// <summary>The last successfully loaded value. Kept while reloading and after a failure.</summary>
    public T? Value { get; private set; }

    /// <summary>The exception from the last load, while <see cref="Status"/> is <see cref="LoadStatus.Failed"/>.</summary>
    public Exception? Error { get; private set; }

    /// <summary>When the value was loaded, from the app's <see cref="TimeProvider"/>.</summary>
    public DateTimeOffset? LoadedAt { get; private set; }

    /// <summary>
    /// Start loading, cancelling any load in flight. The fetch token is cancelled when the app stops, when
    /// <paramref name="lifetime"/> is cancelled, or when this load is replaced or cancelled.
    /// A fetch exception sets <see cref="LoadStatus.Failed"/>; it doesn't go to <see cref="App.OnError"/>.
    /// </summary>
    public void Load(App app, Func<CancellationToken, Task<T>> fetch, CancellationToken lifetime = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(fetch);
        app.InvokeOnUiThread(() => Start(app, fetch, lifetime));
    }

    /// <summary>
    /// Cancel the load in flight, if any. The status goes back to what it was before the load started.
    /// </summary>
    public void Cancel()
    {
        if (_app is { } app) app.InvokeOnUiThread(CancelCore);
        else CancelCore();
    }

    /// <summary>Map the current state to a result.</summary>
    public TResult Match<TResult>(Func<TResult> idle, Func<T?, TResult> loading,
                                  Func<T, TResult> loaded, Func<Exception, TResult> failed)
    {
        ArgumentNullException.ThrowIfNull(idle);
        ArgumentNullException.ThrowIfNull(loading);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(failed);
        return Status switch
        {
            LoadStatus.Loading => loading(Value),
            LoadStatus.Loaded => loaded(Value!),
            LoadStatus.Failed => failed(Error!),
            _ => idle(),
        };
    }

    /// <summary>Run the action for the current state.</summary>
    public void Switch(Action idle, Action<T?> loading, Action<T> loaded, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(idle);
        ArgumentNullException.ThrowIfNull(loading);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(failed);
        switch (Status)
        {
            case LoadStatus.Loading: loading(Value); break;
            case LoadStatus.Loaded: loaded(Value!); break;
            case LoadStatus.Failed: failed(Error!); break;
            default: idle(); break;
        }
    }

    private void Start(App app, Func<CancellationToken, Task<T>> fetch, CancellationToken lifetime)
    {
        CancelInFlight();
        if (lifetime.IsCancellationRequested) return;

        _app = app;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(app.Stopping, lifetime);
        _cts = cts;
        var generation = ++_generation;
        if (Status != LoadStatus.Loading) _statusBeforeLoad = Status;
        Status = LoadStatus.Loading;
        app.RequestRedraw();
        _ = Fetch(app, fetch, cts, generation, lifetime);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A failed fetch is a modeled state (Failed), not an app error.")]
    private async Task Fetch(App app, Func<CancellationToken, Task<T>> fetch, CancellationTokenSource cts,
                             int generation, CancellationToken lifetime)
    {
        T? value = default;
        Exception? error = null;
        try
        {
            value = await fetch(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        app.Post(() => Complete(generation, cts, value, error, lifetime));
    }

    private void Complete(int generation, CancellationTokenSource cts, T? value, Exception? error, CancellationToken lifetime)
    {
        // Last write wins: a replaced or cancelled load, or one whose owner went away, changes nothing
        if (generation != _generation || lifetime.IsCancellationRequested) return;
        if (error is OperationCanceledException && cts.IsCancellationRequested) return;

        _cts = null;
        cts.Dispose();
        if (error is null)
        {
            Value = value;
            Error = null;
            LoadedAt = _app?.Time.GetUtcNow();
            Status = LoadStatus.Loaded;
        }
        else
        {
            Error = error;
            Status = LoadStatus.Failed;
        }
        _app?.RequestRedraw();
    }

    private void CancelCore()
    {
        var wasLoading = Status == LoadStatus.Loading;
        CancelInFlight();
        if (!wasLoading) return;
        Status = _statusBeforeLoad;
        _app?.RequestRedraw();
    }

    private void CancelInFlight()
    {
        _generation++;
        if (_cts is not { } cts) return;
        _cts = null;
        cts.Cancel();
        cts.Dispose();
    }
}
