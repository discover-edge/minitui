namespace MiniTui;

/// <summary>
/// One background reader thread per <see cref="IInputSource"/> (§6.3). Keys go to the most recent
/// subscriber, so an app started from inside another app gets the keys until it exits.
/// </summary>
internal sealed class InputPump
{
    private const int IdleSleepMs = 5;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Lock Gate = new();
    private static readonly Dictionary<IInputSource, InputPump> Pumps = new(ReferenceEqualityComparer.Instance);

    private readonly IInputSource _source;
    private readonly List<Subscription> _subscribers = [];
    private readonly List<ConsoleKeyInfo> _orphans = [];
    private Thread? _thread;

    private InputPump(IInputSource source) => _source = source;

    /// <summary>The 1.0 console input, shared by apps that use it.</summary>
    public static IInputSource Console { get; } = new ConsoleInputSource();

    /// <summary>The default terminal input, shared by every console app that doesn't set its own (1.x spec §2.2).</summary>
    public static IInputSource Terminal { get; } = new TerminalInputSource();

    /// <summary>
    /// The input an app uses when it doesn't call <see cref="App.WithInput"/>. Raw terminal input only goes with the
    /// real console, so an app drawing to a <see cref="TestBackend"/> never changes the terminal's mode.
    /// </summary>
    public static IInputSource DefaultFor(ITerminalBackend? backend)
    {
        var console = backend is null || (backend is IConsoleDevice device && ReferenceEquals(device.DeviceKey, ConsoleDevice.Key));
        if (!console) return Console;
        var setting = Environment.GetEnvironmentVariable("MINITUI_INPUT");
        return string.Equals(setting, "console", StringComparison.OrdinalIgnoreCase) ? Console : Terminal;
    }

    /// <summary>
    /// Deliver keys from <paramref name="source"/> to <paramref name="sink"/> until the subscription is disposed.
    /// The sink is called on the reader thread with each burst of keys.
    /// </summary>
    public static IDisposable Subscribe(IInputSource source, Action<ConsoleKeyInfo[]> sink)
    {
        lock (Gate)
        {
            if (!Pumps.TryGetValue(source, out var pump)) Pumps[source] = pump = new InputPump(source);
            if (pump._subscribers.Count == 0) (source as IInputLifecycle)?.Activate();
            var subscription = new Subscription(pump, sink);
            pump._subscribers.Add(subscription);

            // Keys read while nobody was listening belong to whoever listens next
            if (pump._orphans.Count > 0)
            {
                sink([.. pump._orphans]);
                pump._orphans.Clear();
            }

            if (pump._thread is null)
            {
                pump._thread = new Thread(pump.ReadLoop) { IsBackground = true, Name = "MiniTui input" };
                pump._thread.Start();
            }
            return subscription;
        }
    }


    private void ReadLoop()
    {
        var burst = new List<ConsoleKeyInfo>();
        while (true)
        {
            lock (Gate)
            {
                if (_subscribers.Count == 0)
                {
                    _thread = null;
                    if (_orphans.Count == 0) Pumps.Remove(_source);
                    return;
                }
            }

            burst.Clear();
            while (TryRead(out var key)) burst.Add(key);
            if (burst.Count == 0)
            {
                Wait();
                continue;
            }

            lock (Gate)
            {
                if (_subscribers.Count > 0) _subscribers[^1].Sink([.. burst]);
                else _orphans.AddRange(burst);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The reader thread must not crash the process.")]
    private void Wait()
    {
        if (_source is IWaitableInputSource waitable)
        {
            try
            {
                waitable.WaitForInput(WaitTimeout);
                return;
            }
            catch (Exception) { }
        }
        Thread.Sleep(IdleSleepMs);
    }

    // A source that throws (e.g. the console went away) is treated as having no input.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The reader thread must not crash the process.")]
    private bool TryRead(out ConsoleKeyInfo key)
    {
        try
        {
            return _source.TryRead(out key);
        }
        catch (Exception)
        {
            key = default;
            return false;
        }
    }

    private sealed class Subscription(InputPump pump, Action<ConsoleKeyInfo[]> sink) : IDisposable
    {
        public Action<ConsoleKeyInfo[]> Sink { get; } = sink;

        public void Dispose()
        {
            lock (Gate)
            {
                // Restore the terminal mode now, not when the reader thread next wakes: the process may be exiting
                if (pump._subscribers.Remove(this) && pump._subscribers.Count == 0)
                    (pump._source as IInputLifecycle)?.Deactivate();
            }
        }
    }
}
