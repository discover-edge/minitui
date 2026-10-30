using System.Runtime.ExceptionServices;

namespace MiniTui;

// The event loop (§6.3).
public partial class App
{
    /// <summary>Items handled per iteration before the next frame is drawn.</summary>
    private const int MaxItemsPerIteration = 256;

    /// <summary>Windows has no resize signal, so the loop wakes this often to check the size.</summary>
    private const int WindowsResizePollMs = 100;

    // The app running on this thread, for Input.Poll. Nested apps restore the outer one on exit.
    [ThreadStatic] private static App? t_current;

    private volatile bool _running;
    private IInputSource? _activeInput;
    private int _uiThreadId;
    private Terminal? _terminal;
    private ExceptionDispatchInfo? _failure;
    private Task<bool>? _pendingWait;

    // Keys from a batch that didn't fit in the previous iteration's budget
    private ConsoleKeyInfo[]? _pendingKeys;
    private int _pendingIndex;

    private int _tickPending;
    private long _lastTick;
    private ITimer? _ticker;
    private TimeSpan? _tickerPeriod;

    // The fastest tick rate a widget asked for while drawing the current frame (toasts, spinners)
    private TimeSpan? _frameTickRequest;
    private RunScope? _run;
    private Theme? _activeTheme;
    private bool _noColor;
    private bool _basicGlyphs;

    /// <summary>The app whose loop is running on the current thread.</summary>
    internal static App? Current => t_current;

    private void RunCore(CancellationToken cancellationToken)
    {
        StartRun(cancellationToken);
        try
        {
            using (EnterUi())
            {
                Setup();
                while (Step(wait: true)) { }
            }
        }
        finally
        {
            EndRun();
        }

        _failure?.Throw();
    }

    /// <summary>
    /// Enter the terminal and start input, ticks and console capture. Runs no user code. Pair with <see cref="EndRun"/>;
    /// user code runs inside <see cref="EnterUi"/>.
    /// </summary>
    internal void StartRun(CancellationToken cancellationToken)
    {
        if (_onRender is null && Screens.Count == 0 && _onSetup is null)
            throw new InvalidOperationException("Render callback is required. Call Render() or push a screen before Run().");
        if (_running) throw new InvalidOperationException("The app is already running.");

        if (_stopping.IsCancellationRequested) _stopping = new CancellationTokenSource();
        _failure = null;
        _uiThreadId = Environment.CurrentManagedThreadId;
        _running = true;

        var input = _input ?? InputPump.DefaultFor(_backend);
        var run = new RunScope(input, t_current);
        _run = run;
        try
        {
            var backend = _backend ?? Terminal.CreateDefaultBackend(_colorMode);
            run.Terminal = new Terminal(backend);
            _terminal = run.Terminal;
            _noColor = ColorModeOf(backend) == ColorMode.None;
            _activeTheme = ResolveTheme(_theme);
            _basicGlyphs = Glyphs.DetectBasic(backend);
            if (_captureConsole) run.Capture = ConsoleCapture.Start(Log);

            run.Subscription = InputPump.Subscribe(input, keys => _queue.Writer.TryWrite(new KeyBatch(keys)));
            _activeInput = input;

            // An app started from inside another one on the same input gets the keys the outer one hasn't handled yet
            var outer = run.PreviousApp is { _running: true } app && ReferenceEquals(app._activeInput, input) ? app : null;
            if (outer?.RemoveQueuedKeys() is { Count: > 0 } inherited) _pendingKeys = [.. inherited];
            StartTicker();
            run.Cancellation = cancellationToken.Register(Quit);
            TerminalSession.Resized += OnResizeSignal;
            run.ResizeHooked = true;
        }
        catch
        {
            EndRun();
            throw;
        }
    }

    /// <summary>Run <c>OnSetup</c>. Call inside <see cref="EnterUi"/>.</summary>
    internal void Setup()
    {
        Guard(() => _onSetup?.Invoke(this));
        if (_onRender is null && Screens.Count == 0 && _failure is null)
            throw new InvalidOperationException("Render callback is required. Call Render() or push a screen before Run().");
    }

    /// <summary>
    /// Make the calling thread the UI thread until disposed: <see cref="Current"/>, the theme, glyphs and the
    /// synchronization context. Restores the previous values, so it nests.
    /// </summary>
    internal UiBinding EnterUi() => new(this);

    /// <summary>
    /// One loop iteration: optionally wait for work, handle resizes and up to 256 queued items, then update and
    /// draw if anything happened. Call inside <see cref="EnterUi"/>.
    /// </summary>
    /// <returns>True while the app is still running.</returns>
    internal bool Step(bool wait)
    {
        if (!_running || _run is not { Terminal: { } terminal } run) return false;
        if (!run.FirstFrame && wait) WaitForWork();
        var dirty = run.FirstFrame;

        // 1. Resize
        if (terminal.CheckResize())
        {
            dirty = true;
            var resize = new ResizeEvent(terminal.Width, terminal.Height);
            Guard(() => _onEvent?.Invoke(resize));
        }

        // 2. Drain the queue in FIFO order
        for (var budget = MaxItemsPerIteration; budget > 0 && _running && TryTake(out var item); budget--)
        {
            switch (item)
            {
                case ConsoleKeyInfo key:
                    dirty = true;
                    Guard(() => HandleKey(key));
                    break;
                case Action action:
                    dirty = true;
                    Guard(action);
                    break;
                case TickEvent tick:
                    Interlocked.Exchange(ref _tickPending, 0);
                    dirty = true;
                    Guard(() => _onEvent?.Invoke(tick));
                    break;
                case RedrawRequest:
                    dirty = true;
                    break;
            }
        }

        if (!_running) return false;

        // 3–4. Update, render
        Guard(() => _onUpdate?.Invoke());
        if (!_running) return false;
        if (dirty)
        {
            _frameTickRequest = null;
            Guard(() => terminal.Draw(buf => RenderFrame(buf, new Rect(0, 0, buf.Width, buf.Height))));
            UpdateTicker();
        }
        run.FirstFrame = false;
        return _running;
    }

    /// <summary>Leave the terminal and stop input, ticks and capture. Idempotent.</summary>
    internal void EndRun()
    {
        if (_run is not { } run) return;
        _run = null;
        _running = false;
        StopTicker();
        CancelStopping();
        if (run.ResizeHooked) TerminalSession.Resized -= OnResizeSignal;
        run.Cancellation.Dispose();
        run.Subscription?.Dispose();

        // Restore the writers before leaving the terminal, and replay stderr after, so it lands on the real screen
        run.Capture?.Restore();
        run.Terminal?.Dispose();
        _terminal = null;
        run.Capture?.ReplayErrors();

        // Keys this app didn't get to go back to the outer app, ahead of anything it received since
        if (run.PreviousApp is { _running: true } outerApp && ReferenceEquals(outerApp._activeInput, run.Input))
            outerApp.ReturnKeys(RemoveQueuedKeys());
        _activeInput = null;
        DiscardQueue();
    }

    /// <summary>True while a run is started and hasn't stopped.</summary>
    internal bool IsRunning => _running;

    /// <summary>Take the exception a run stopped with, if any.</summary>
    internal ExceptionDispatchInfo? TakeFailure()
    {
        var failure = _failure;
        _failure = null;
        return failure;
    }

    /// <summary>True when keys or other items are waiting to be handled.</summary>
    internal bool HasQueuedWork => _pendingKeys is not null || _queue.Reader.TryPeek(out _);

    /// <summary>Queue keys as if the input source had read them.</summary>
    internal void InjectKeys(ConsoleKeyInfo[] keys) => _queue.Writer.TryWrite(new KeyBatch(keys));

    /// <summary>Wait up to <paramref name="timeout"/> for something to be queued.</summary>
    internal void WaitForQueue(TimeSpan timeout)
    {
        if (HasQueuedWork) return;
        _pendingWait ??= _queue.Reader.WaitToReadAsync().AsTask();
        if (_pendingWait.Wait(timeout)) _pendingWait = null;
    }

    /// <summary>What a run started and must undo.</summary>
    private sealed class RunScope(IInputSource input, App? previousApp)
    {
        public IInputSource Input { get; } = input;
        public App? PreviousApp { get; } = previousApp;
        public Terminal? Terminal { get; set; }
        public ConsoleCapture? Capture { get; set; }
        public IDisposable? Subscription { get; set; }
        public CancellationTokenRegistration Cancellation { get; set; }
        public bool ResizeHooked { get; set; }
        public bool FirstFrame { get; set; } = true;
    }

    /// <summary>The thread-local state of the UI thread, restored on dispose.</summary>
    internal readonly struct UiBinding : IDisposable
    {
        private readonly App? _previousApp;
        private readonly SynchronizationContext? _previousContext;
        private readonly Theme.Scope _theme;
        private readonly Glyphs.Scope _glyphs;

        public UiBinding(App app)
        {
            app._uiThreadId = Environment.CurrentManagedThreadId;
            _previousApp = t_current;
            _previousContext = SynchronizationContext.Current;
            t_current = app;
            _theme = Theme.Use(app._activeTheme ?? app._theme ?? Theme.Default);
            _glyphs = Glyphs.Use(app._basicGlyphs);
            SynchronizationContext.SetSynchronizationContext(new MiniTuiSynchronizationContext(app));
        }

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(_previousContext);
            _glyphs.Dispose();
            _theme.Dispose();
            t_current = _previousApp;
        }
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        _onEvent?.Invoke(new KeyEvent(key));
        if (_onKey?.Invoke(key) == true) return;

        // Ctrl+C always quits unless OnKey took it
        if (KeyInput.IsCtrlC(key))
        {
            Quit();
            return;
        }

        KeyRouter.Route(this, key);
    }

    /// <summary>
    /// Draw a frame: the <see cref="Render"/> callback, or the top screen and the chrome (§9.3); then overlays,
    /// bottom to top, and toasts.
    /// </summary>
    private void RenderFrame(Buffer buf, Rect area)
    {
        var body = area;
        if (_onRender is not null)
        {
            _onRender(buf, area);
        }
        else
        {
            var chrome = _chromeStatusBar && area.Height >= 2;
            if (chrome) body = area with { Height = area.Height - 1 };
            if (Screens.Top is { } top) top.Render(buf, body, new RenderContext(this, top, focused: !Overlays.Any));
            if (chrome) DrawStatusBar(buf, new Rect(area.X, area.Bottom - 1, area.Width, 1));
        }

        var theme = Theme.Current;
        foreach (var overlay in Overlays.All)
        {
            var rect = overlay.Placement(body);
            WidgetText.Clear(buf, rect);
            var inner = Widget.Block()
                .Title(overlay.Title is { } title ? new Line(title) : null)
                .BorderStyle(theme.ModalBorder)
                .TitleStyle(theme.ModalTitle)
                .Padding(new Margin(1, 0))
                .Draw(buf, rect);
            overlay.Render(buf, inner, new RenderContext(this, overlay, focused: ReferenceEquals(overlay, Overlays.Top)));
        }

        Widget.Toasts().Queue(Toasts).Draw(buf, area);
    }

    // Hints on the left; the breadcrumb (screen titles, truncated from the left) on the right
    private void DrawStatusBar(Buffer buf, Rect row)
    {
        var crumb = _chromeBreadcrumb ? string.Join(" › ", Screens.All.Select(s => s.Title)) : "";
        crumb = WidgetText.TruncateStart(crumb, row.Width / 2);
        var crumbWidth = UnicodeWidth.Of(crumb);
        var hints = KeyRouter.Hints(this, row.Width - 2 - crumbWidth - (crumbWidth > 0 ? 1 : 0));
        Widget.StatusBar().Left(hints).Right(crumb).Draw(buf, row);
    }

    private void WaitForWork()
    {
        if (_pendingKeys is not null) return;
        _pendingWait ??= _queue.Reader.WaitToReadAsync().AsTask();
        if (_pendingWait.Wait(OperatingSystem.IsWindows() ? WindowsResizePollMs : Timeout.Infinite))
            _pendingWait = null;
    }

    private bool TryTake(out object? item)
    {
        while (true)
        {
            if (_pendingKeys is { } keys)
            {
                item = keys[_pendingIndex++];
                if (_pendingIndex == keys.Length)
                {
                    _pendingKeys = null;
                    _pendingIndex = 0;
                }
                return true;
            }

            if (!_queue.Reader.TryRead(out item)) return false;
            if (item is not KeyBatch batch) return true;
            if (batch.Keys.Length > 0) _pendingKeys = batch.Keys;
        }
    }

    /// <summary>Take this app's unhandled keys out of its queue, keeping everything else queued.</summary>
    private List<ConsoleKeyInfo> RemoveQueuedKeys()
    {
        var keys = new List<ConsoleKeyInfo>();
        if (_pendingKeys is { } pending) keys.AddRange(pending.AsSpan(_pendingIndex));
        _pendingKeys = null;
        _pendingIndex = 0;

        var others = new List<object>();
        while (_queue.Reader.TryRead(out var item))
        {
            if (item is KeyBatch batch) keys.AddRange(batch.Keys);
            else others.Add(item);
        }
        foreach (var item in others) _queue.Writer.TryWrite(item);
        return keys;
    }

    /// <summary>Put keys back so they're handled before any this app already has queued.</summary>
    private void ReturnKeys(List<ConsoleKeyInfo> keys)
    {
        if (keys.Count == 0) return;
        if (_pendingKeys is { } pending) keys.AddRange(pending.AsSpan(_pendingIndex));
        _pendingKeys = [.. keys];
        _pendingIndex = 0;
    }

    private void DiscardQueue()
    {
        while (_queue.Reader.TryRead(out _)) { }
        _pendingKeys = null;
        _pendingIndex = 0;
    }

    /// <summary>
    /// Called by widgets while drawing (toasts, spinners) to get ticks at least this often until a frame
    /// is drawn without them. Only has an effect on the UI thread.
    /// </summary>
    internal void RequestTicks(TimeSpan rate)
    {
        if (!IsUiThread) return;
        if (_frameTickRequest is not { } current || rate < current) _frameTickRequest = rate;
    }

    private static ColorMode? ColorModeOf(ITerminalBackend backend) => backend switch
    {
        AnsiBackend ansi => ansi.ColorMode,
        LegacyConsoleBackend legacy => legacy.ColorMode,
        _ => null,
    };

    /// <summary>
    /// The theme to draw with. On a terminal without colors (<c>NO_COLOR</c>), <see cref="Theme.Default"/>, chosen or
    /// not, becomes <see cref="Theme.Monochrome"/>, since its selection would otherwise be invisible.
    /// A custom theme is used as is.
    /// </summary>
    private Theme ResolveTheme(Theme? theme) =>
        _noColor && (theme is null || ReferenceEquals(theme, Theme.Default)) ? Theme.Monochrome : theme ?? Theme.Default;

    private void StartTicker()
    {
        _lastTick = _time.GetTimestamp();
        Interlocked.Exchange(ref _tickPending, 0);
        _frameTickRequest = null;
        UpdateTicker();
    }

    // The tick period is the configured rate, or faster while a widget asks for it
    private void UpdateTicker()
    {
        var period = _tickRate;
        if (_frameTickRequest is { } requested && (period is not { } p || requested < p)) period = requested;
        if (period == _tickerPeriod && (_ticker is not null || period is null)) return;

        _tickerPeriod = period;
        if (period is not { } value)
        {
            StopTicker();
            return;
        }
        if (_ticker is null) _ticker = _time.CreateTimer(_ => OnTimer(), null, value, value);
        else _ticker.Change(value, value);
    }

    private void StopTicker()
    {
        _ticker?.Dispose();
        _ticker = null;
        _tickerPeriod = null;
    }

    private void OnTimer()
    {
        // At most one tick waits in the queue; a slow frame doesn't pile them up
        if (Interlocked.Exchange(ref _tickPending, 1) == 1) return;
        var now = _time.GetTimestamp();
        var elapsed = _time.GetElapsedTime(Interlocked.Exchange(ref _lastTick, now), now);
        _queue.Writer.TryWrite(new TickEvent(elapsed));
    }

    private void OnResizeSignal() => _queue.Writer.TryWrite(Wake.Instance);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Every callback failure goes to OnError.")]
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A throwing OnError handler is treated as Rethrow.")]
    private void HandleError(Exception error)
    {
        ErrorAction decision;
        try
        {
            decision = _onError?.Invoke(error) ?? ErrorAction.Rethrow;
        }
        catch (Exception handlerError)
        {
            error = handlerError;
            decision = ErrorAction.Rethrow;
        }

        switch (decision)
        {
            case ErrorAction.Continue:
                return;
            case ErrorAction.Quit:
                Quit();
                return;
            default:
                _failure ??= ExceptionDispatchInfo.Capture(error);
                Quit();
                return;
        }
    }
}
