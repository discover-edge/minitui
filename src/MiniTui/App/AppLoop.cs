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

    // Apps currently reading the console, so Input.Poll on another thread doesn't steal their keys.
    private static int s_consoleApps;

    private volatile bool _running;
    private IInputSource? _activeInput;
    private int _uiThreadId;
    private Terminal? _terminal;
    private ExceptionDispatchInfo? _failure;
    private Task<bool>? _pendingWait;

    // Keys from a batch that didn't fit in the previous iteration's budget
    private ConsoleKeyInfo[]? _pendingKeys;
    private int _pendingIndex;

    // The key RunWhile's condition can take with Input.Poll
    private ConsoleKeyInfo? _offeredKey;

    private int _tickPending;
    private long _lastTick;
    private ITimer? _ticker;
    private TimeSpan? _tickerPeriod;

    // The fastest tick rate a widget asked for while drawing the current frame (toasts, spinners)
    private TimeSpan? _frameTickRequest;
    private bool _modalWasActive;

    /// <summary>The app whose loop is running on the current thread.</summary>
    internal static App? Current => t_current;

    /// <summary>True while any app reads console input.</summary>
    internal static bool ConsoleInputInUse => Volatile.Read(ref s_consoleApps) > 0;

    /// <summary>Take the key offered to RunWhile's condition. Returns null after the first call.</summary>
    internal ConsoleKeyInfo? TakeOfferedKey()
    {
        var key = _offeredKey;
        _offeredKey = null;
        return key;
    }

    private void RunCore(Func<bool>? condition, CancellationToken cancellationToken)
    {
        if (_onRender is null)
            throw new InvalidOperationException("Render callback is required. Call Render() before Run().");
        if (_running) throw new InvalidOperationException("The app is already running.");

        if (_stopping.IsCancellationRequested) _stopping = new CancellationTokenSource();
        _failure = null;
        _modalWasActive = false;
        _uiThreadId = Environment.CurrentManagedThreadId;
        _running = true;

        var input = _input ?? InputPump.Console;
        var readsConsole = ReferenceEquals(input, InputPump.Console);
        var previousApp = t_current;
        var previousContext = SynchronizationContext.Current;
        ConsoleCapture? capture = null;
        Terminal? terminal = null;
        try
        {
            var backend = _backend ?? Terminal.CreateDefaultBackend(_colorMode);
            terminal = new Terminal(backend);
            _terminal = terminal;
            if (_captureConsole) capture = ConsoleCapture.Start(Log);
            using var theme = Theme.Use(_theme ?? DefaultTheme(backend));
            using var glyphs = Glyphs.Use(Glyphs.DetectBasic(backend));

            t_current = this;
            if (readsConsole) Interlocked.Increment(ref s_consoleApps);
            SynchronizationContext.SetSynchronizationContext(new MiniTuiSynchronizationContext(this));

            using var subscription = InputPump.Subscribe(input, keys => _queue.Writer.TryWrite(new KeyBatch(keys)));
            _activeInput = input;

            // An app started from inside another one on the same input gets the keys the outer one hasn't handled yet
            var outer = previousApp is { _running: true } app && ReferenceEquals(app._activeInput, input) ? app : null;
            if (outer?.RemoveQueuedKeys() is { Count: > 0 } inherited) _pendingKeys = [.. inherited];
            StartTicker();
            using var cancellation = cancellationToken.Register(Quit);
            TerminalSession.Resized += OnResizeSignal;
            try
            {
                Loop(terminal, condition);
            }
            finally
            {
                TerminalSession.Resized -= OnResizeSignal;
            }
        }
        finally
        {
            _running = false;
            StopTicker();
            CancelStopping();
            SynchronizationContext.SetSynchronizationContext(previousContext);
            if (ReferenceEquals(t_current, this))
            {
                t_current = previousApp;
                if (readsConsole) Interlocked.Decrement(ref s_consoleApps);
            }

            // Restore the writers before leaving the terminal, and replay stderr after, so it lands on the real screen
            capture?.Restore();
            terminal?.Dispose();
            _terminal = null;
            capture?.ReplayErrors();

            // Keys this app didn't get to go back to the outer app, ahead of anything it received since
            if (previousApp is { _running: true } outerApp && ReferenceEquals(outerApp._activeInput, input))
                outerApp.ReturnKeys(RemoveQueuedKeys());
            _activeInput = null;
            DiscardQueue();
        }

        _failure?.Throw();
    }

    private void Loop(Terminal terminal, Func<bool>? condition)
    {
        Guard(() => _onSetup?.Invoke(this));

        var firstFrame = true;
        while (_running)
        {
            if (!firstFrame) WaitForWork();
            var dirty = firstFrame;

            // 1. Resize
            if (terminal.CheckResize())
            {
                dirty = true;
                var resize = new ResizeEvent(terminal.Width, terminal.Height);
                Guard(() => _onEvent?.Invoke(resize));
            }

            // 2. Drain the queue in FIFO order
            var sawKey = false;
            for (var budget = MaxItemsPerIteration; budget > 0 && _running && TryTake(out var item); budget--)
            {
                switch (item)
                {
                    case ConsoleKeyInfo key:
                        sawKey = dirty = true;
                        Guard(() => HandleKey(key, condition));
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

            // RunWhile's condition runs once per key (in HandleKey), or once per wake-up without one
            if (condition is not null && !sawKey && _running)
                Guard(() => { if (!condition()) Quit(); });
            if (!_running) break;

            // 3–5. Modal state, update, render
            Guard(RefreshRouter);
            Guard(() => _onUpdate?.Invoke());
            if (!_running) break;
            if (dirty)
            {
                _frameTickRequest = null;
                Guard(() => terminal.Draw(buf => _onRender!(buf, new Rect(0, 0, buf.Width, buf.Height))));
                UpdateTicker();
            }
            firstFrame = false;
        }
    }

    private void HandleKey(ConsoleKeyInfo key, Func<bool>? condition)
    {
        _onEvent?.Invoke(new KeyEvent(key));
        var handled = _onKey?.Invoke(key) == true;

        // Ctrl+C always quits unless OnKey took it
        if (!handled && Input.IsCtrlC(key))
        {
            Quit();
            return;
        }

        if (condition is not null)
        {
            _offeredKey = handled ? null : key;
            bool keepRunning;
            try
            {
                keepRunning = condition();
            }
            finally
            {
                // The condition took the key with Input.Poll, so the router mustn't see it too (D4)
                if (!handled && _offeredKey is null) handled = true;
                _offeredKey = null;
            }

            if (!keepRunning)
            {
                Quit();
                return;
            }
        }

        if (!handled) _router?.Handle(key);
    }

    private void RefreshRouter()
    {
        if (_router is null) return;
        _router.Refresh();

        // A dismissed modal leaves nothing behind, but redraw fully to be safe on odd terminals
        var active = _router.IsModalActive;
        if (_modalWasActive && !active) _terminal?.Invalidate();
        _modalWasActive = active;
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
        _offeredKey = null;
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

    private static Theme DefaultTheme(ITerminalBackend backend)
    {
        var mode = backend switch
        {
            AnsiBackend ansi => ansi.ColorMode,
            LegacyConsoleBackend legacy => legacy.ColorMode,
            _ => (ColorMode?)null,
        };
        return mode == ColorMode.None ? Theme.Monochrome : Theme.Default;
    }

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
