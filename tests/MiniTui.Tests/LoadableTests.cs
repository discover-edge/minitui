using Microsoft.Extensions.Time.Testing;

namespace MiniTui.Tests;

public class LoadableTests
{
    // A running app for the loads to post to; disposed by quitting
    private sealed class Running : IAsyncDisposable
    {
        private readonly Task _run;

        public Running(FakeTimeProvider? time = null, Func<Exception, ErrorAction>? onError = null)
        {
            App = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
                .WithTimeProvider(time ?? new FakeTimeProvider())
                .Render((_, _) => { });
            if (onError is not null) App.OnError(onError);
            _run = AppHarness.Run(App);
        }

        public App App { get; }

        public Task<T> OnUi<T>(Func<T> read) => AppHarness.OnUi(App, read);

        public Task WaitUntil(Func<bool> condition) => AppHarness.WaitUntil(App, condition);

        public async ValueTask DisposeAsync()
        {
            App.Quit();
            await _run;
        }
    }

    [Fact]
    public async Task Load_GoesLoading_ThenLoaded_WithTheValueAndTime()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        await using var app = new Running(time);
        var loadable = new Loadable<int>();
        var fetch = new TaskCompletionSource<int>();

        var status = await app.OnUi(() =>
        {
            loadable.Load(app.App, _ => fetch.Task);
            return loadable.Status;
        });
        Assert.Equal(LoadStatus.Loading, status);

        fetch.SetResult(42);
        await app.WaitUntil(() => loadable.Status == LoadStatus.Loaded);

        Assert.Equal(42, loadable.Value);
        Assert.Null(loadable.Error);
        Assert.Equal(time.GetUtcNow(), loadable.LoadedAt);
    }

    [Fact]
    public async Task FailedFetch_SetsFailed_KeepsTheValue_AndDoesNotGoToOnError()
    {
        var errors = 0;
        await using var app = new Running(onError: _ => { errors++; return ErrorAction.Continue; });
        var loadable = new Loadable<string>();

        await app.OnUi(() => { loadable.Load(app.App, _ => Task.FromResult("first")); return 0; });
        await app.WaitUntil(() => loadable.Status == LoadStatus.Loaded);
        await app.OnUi(() => { loadable.Load(app.App, _ => throw new InvalidOperationException("down")); return 0; });
        await app.WaitUntil(() => loadable.Status == LoadStatus.Failed);

        Assert.Equal("down", loadable.Error!.Message);
        Assert.Equal("first", loadable.Value);
        Assert.Equal(0, errors);
    }

    [Fact]
    public async Task ReplacedLoad_IsIgnored_AndItsTokenIsCancelled()
    {
        await using var app = new Running();
        var loadable = new Loadable<int>();
        var slow = new TaskCompletionSource<int>();
        CancellationToken slowToken = default;

        await app.OnUi(() =>
        {
            loadable.Load(app.App, ct => { slowToken = ct; return slow.Task; });
            loadable.Load(app.App, _ => Task.FromResult(2));
            return 0;
        });
        await app.WaitUntil(() => loadable.Status == LoadStatus.Loaded);
        slow.SetResult(1);
        await app.OnUi(() => 0);  // let the stale result arrive
        await app.OnUi(() => 0);

        Assert.True(slowToken.IsCancellationRequested);
        Assert.Equal(2, loadable.Value);
    }

    [Fact]
    public async Task Cancel_RestoresThePreviousStatus_AndIgnoresTheResult()
    {
        await using var app = new Running();
        var loadable = new Loadable<int>();
        var fetch = new TaskCompletionSource<int>();

        var status = await app.OnUi(() =>
        {
            loadable.Load(app.App, _ => fetch.Task);
            loadable.Cancel();
            return loadable.Status;
        });
        fetch.SetResult(5);
        await app.OnUi(() => 0);
        await app.OnUi(() => 0);

        Assert.Equal(LoadStatus.Idle, status);
        Assert.Equal(LoadStatus.Idle, await app.OnUi(() => loadable.Status));
        Assert.Equal(0, loadable.Value);
    }

    [Fact]
    public async Task CancelledLifetime_IgnoresTheResult()
    {
        await using var app = new Running();
        var loadable = new Loadable<int>();
        var fetch = new TaskCompletionSource<int>();
        using var lifetime = new CancellationTokenSource();
        CancellationToken token = default;

        await app.OnUi(() => { loadable.Load(app.App, ct => { token = ct; return fetch.Task; }, lifetime.Token); return 0; });
        await lifetime.CancelAsync();
        fetch.SetResult(5);
        await app.OnUi(() => 0);
        await app.OnUi(() => 0);

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(LoadStatus.Loading, await app.OnUi(() => loadable.Status));
        Assert.Equal(0, loadable.Value);
    }

    [Fact]
    public async Task LoadFromAnotherThread_ChangesStateOnTheUiThread()
    {
        await using var app = new Running();
        var loadable = new Loadable<int>();
        var uiThread = await app.OnUi(() => Environment.CurrentManagedThreadId);
        var fetchThread = -1;

        await Task.Run(() => loadable.Load(app.App, _ =>
        {
            fetchThread = Environment.CurrentManagedThreadId;
            return Task.FromResult(1);
        }));
        await app.WaitUntil(() => loadable.Status == LoadStatus.Loaded);

        Assert.Equal(uiThread, fetchThread);
    }

    [Fact]
    public void MatchAndSwitch_PickTheBranchForTheStatus()
    {
        var loadable = new Loadable<int>();

        Assert.Equal("idle", loadable.Match(() => "idle", _ => "loading", _ => "loaded", _ => "failed"));
        var hit = "";
        loadable.Switch(() => hit = "idle", _ => hit = "loading", _ => hit = "loaded", _ => hit = "failed");
        Assert.Equal("idle", hit);
    }
}
