using System.Threading.Channels;

namespace MiniTui;

/// <summary>
/// Fluent builder for creating TUI applications. Builder methods mutate the app and return it.
/// </summary>
/// <remarks>
/// All callbacks run on the thread that called <see cref="Run"/> (the UI thread). Other threads talk
/// to the app through <see cref="Post"/>, <see cref="RequestRedraw"/> and <see cref="Quit"/>.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The Stopping source has no timer or links; it's replaced when cancelled and needs no disposal.")]
public partial class App
{
    private static readonly TimeSpan DefaultTickRate = TimeSpan.FromMilliseconds(250);

    private readonly Channel<object> _queue = Channel.CreateUnbounded<object>(
        new UnboundedChannelOptions { SingleReader = true });

    private InputRouter? _router;
    private Action<App>? _onSetup;
    private Action<Buffer, Rect>? _onRender;
    private Action? _onUpdate;
    private Func<ConsoleKeyInfo, bool>? _onKey;
    private Action<Event>? _onEvent;
    private Func<Exception, ErrorAction>? _onError;
    private TimeSpan? _tickRate = DefaultTickRate;
    private TimeProvider _time = TimeProvider.System;
    private ITerminalBackend? _backend;
    private IInputSource? _input;
    private ColorMode? _colorMode;
    private bool _captureConsole = true;
    private Theme? _theme;
    private CancellationTokenSource _stopping = new();

    private App() { }

    /// <summary>
    /// Create a new App builder.
    /// </summary>
    public static App Create() => new();

    /// <summary>
    /// Configure the input router for handling keyboard input.
    /// </summary>
    public App UseRouter(InputRouter router)
    {
        _router = router;
        return this;
    }

    /// <summary>
    /// Set up code to run once on the UI thread before the first frame.
    /// </summary>
    public App OnSetup(Action setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _onSetup = _ => setup();
        return this;
    }

    /// <summary>
    /// Set up code to run once on the UI thread before the first frame. It receives the app,
    /// for example to start loads with <see cref="Loadable{T}.Load"/>.
    /// </summary>
    public App OnSetup(Action<App> setup)
    {
        _onSetup = setup;
        return this;
    }

    /// <summary>
    /// Configure the render callback, called with the buffer and screen rect whenever a frame is drawn.
    /// </summary>
    public App Render(Action<Buffer, Rect> render)
    {
        _onRender = render;
        return this;
    }

    /// <summary>
    /// Configure update logic that runs on every loop iteration, before rendering.
    /// </summary>
    public App OnUpdate(Action update)
    {
        _onUpdate = update;
        return this;
    }

    /// <summary>
    /// Handle a key before the router sees it. Return true if the key was handled, so the router doesn't get it.
    /// </summary>
    public App OnKey(Func<ConsoleKeyInfo, bool> handler)
    {
        _onKey = handler;
        return this;
    }

    /// <summary>
    /// Observe every event (keys, resizes and ticks) before it's handled.
    /// </summary>
    public App OnEvent(Action<Event> handler)
    {
        _onEvent = handler;
        return this;
    }

    /// <summary>
    /// Decide what happens when a callback, posted action or <see cref="RunTask"/> throws.
    /// Without a handler, the terminal is restored and the exception is rethrown from <see cref="Run"/>.
    /// </summary>
    public App OnError(Func<Exception, ErrorAction> handler)
    {
        _onError = handler;
        return this;
    }

    /// <summary>
    /// How often a <see cref="TickEvent"/> is delivered, which also redraws. The default is 250 ms.
    /// Null means no ticks: the app only redraws after input, posted actions and redraw requests.
    /// </summary>
    public App WithTickRate(TimeSpan? rate)
    {
        if (rate is { } value) ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(rate));
        _tickRate = rate;
        return this;
    }

    /// <summary>
    /// Set the time between frames in milliseconds.
    /// </summary>
    [Obsolete("Use WithTickRate instead.")]
    public App WithFrameDelay(int milliseconds) =>
        WithTickRate(TimeSpan.FromMilliseconds(Math.Max(1, milliseconds)));

    /// <summary>
    /// The clock for ticks, <see cref="Loadable{T}.LoadedAt"/> and log timestamps. Tests can pass a fake one.
    /// </summary>
    public App WithTimeProvider(TimeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _time = provider;
        Log.Time = provider;
        return this;
    }

    /// <summary>
    /// Draw through a specific backend, such as <see cref="TestBackend"/>, instead of the console.
    /// </summary>
    public App WithBackend(ITerminalBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        return this;
    }

    /// <summary>
    /// Read keys from a specific source, such as <see cref="ScriptedInputSource"/>, instead of the console.
    /// The app doesn't dispose it.
    /// </summary>
    public App WithInput(IInputSource input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
        return this;
    }

    /// <summary>
    /// Override the detected color mode of the default console backend.
    /// </summary>
    public App WithColorMode(ColorMode mode)
    {
        _colorMode = mode;
        return this;
    }

    /// <summary>
    /// The theme widgets draw with while the app runs (<see cref="Theme.Current"/>). Without one, the app uses
    /// <see cref="Theme.Default"/>, or <see cref="Theme.Monochrome"/> when the terminal has no colors.
    /// Can be called while running, from any thread; the next frame uses the new theme.
    /// </summary>
    public App WithTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        if (_running) InvokeOnUiThread(() => Theme.ReplaceCurrent(theme));
        return this;
    }

    /// <summary>
    /// While running, redirect <see cref="Console.Out"/> and <see cref="Console.Error"/> into <see cref="Log"/>
    /// so stray writes can't corrupt the screen. On by default. Captured error lines are written to stderr on exit.
    /// </summary>
    public App CaptureConsoleOutput(bool capture = true)
    {
        _captureConsole = capture;
        return this;
    }

    /// <summary>
    /// Lines written to the console while the app runs (see <see cref="CaptureConsoleOutput"/>).
    /// </summary>
    public LogBuffer Log { get; } = new();

    /// <summary>
    /// Cancelled when <see cref="Quit"/> is called or the loop exits. Pass it to background work.
    /// </summary>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>
    /// The terminal size in cells, or (0, 0) when the app isn't running.
    /// </summary>
    public (int Width, int Height) Size => _terminal is { } terminal ? (terminal.Width, terminal.Height) : (0, 0);

    /// <summary>
    /// Run <paramref name="action"/> on the UI thread. Thread-safe. If it throws, <see cref="OnError"/> decides what happens.
    /// </summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.Writer.TryWrite(action);
    }

    /// <summary>
    /// Start async work on the UI thread, so its continuations resume there. The token is <see cref="Stopping"/>.
    /// Exceptions go to <see cref="OnError"/>; cancellation after <see cref="Quit"/> is ignored.
    /// </summary>
    /// <returns>A task that completes when the work finishes. It never faults.</returns>
    public Task RunTask(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        InvokeOnUiThread(() => _ = RunTaskCore(work, completion));
        return completion.Task;
    }

    /// <summary>
    /// Draw a frame soon, even if nothing else happened. Thread-safe.
    /// </summary>
    public void RequestRedraw() => _queue.Writer.TryWrite(RedrawRequest.Instance);

    /// <summary>
    /// Stop the loop after the current item. Thread-safe.
    /// </summary>
    public void Quit()
    {
        _running = false;
        CancelStopping();
        _queue.Writer.TryWrite(Wake.Instance);
    }

    /// <summary>
    /// Force a full screen redraw on the next frame.
    /// </summary>
    public void Invalidate() => _terminal?.Invalidate();

    /// <summary>
    /// Run the application on this thread. Blocks until <see cref="Quit"/> is called.
    /// </summary>
    public void Run() => RunCore(condition: null, CancellationToken.None);

    /// <summary>
    /// Run the application on a new UI thread. Cancelling <paramref name="cancellationToken"/> quits it.
    /// </summary>
    /// <returns>A task that completes when the app exits, or faults with the exception <see cref="Run"/> would throw.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The exception is handed to the returned task.")]
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                RunCore(condition: null, cancellationToken);
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }) { Name = "MiniTui UI" };
        thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// Run the application with a custom run condition. <paramref name="condition"/> is called once per key
    /// (which <see cref="Input.Poll"/> returns while it runs) and once per wake-up without a key.
    /// The app stops when it returns false.
    /// </summary>
    public void RunWhile(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        RunCore(condition, CancellationToken.None);
    }

    /// <summary>The clock the app runs on.</summary>
    internal TimeProvider Time => _time;

    /// <summary>True on the thread running this app's loop.</summary>
    internal bool IsUiThread => _running && Environment.CurrentManagedThreadId == _uiThreadId;

    /// <summary>Run inline on the UI thread, otherwise post.</summary>
    internal void InvokeOnUiThread(Action action)
    {
        if (IsUiThread) action();
        else Post(action);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Every task failure goes to OnError.")]
    private async Task RunTaskCore(Func<CancellationToken, Task> work, TaskCompletionSource completion)
    {
        var stopping = Stopping;
        try
        {
            await work(stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void CancelStopping()
    {
        try
        {
            _stopping.Cancel();
        }
        catch (AggregateException ex)
        {
            // A Stopping callback threw; report it like any other callback failure
            if (IsUiThread) HandleError(ex);
        }
    }

    /// <summary>Asks the loop to redraw.</summary>
    private sealed class RedrawRequest
    {
        public static readonly RedrawRequest Instance = new();
    }

    /// <summary>Wakes the loop without doing anything else, e.g. to notice <see cref="Quit"/> or a resize signal.</summary>
    private sealed class Wake
    {
        public static readonly Wake Instance = new();
    }

    /// <summary>Keys read in one burst by the input thread.</summary>
    private sealed record KeyBatch(ConsoleKeyInfo[] Keys);
}
