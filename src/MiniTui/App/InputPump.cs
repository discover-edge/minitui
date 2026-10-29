namespace MiniTui;

/// <summary>
/// One background reader thread per <see cref="IInputSource"/> (§6.3). Keys go to the most recent
/// subscriber, so an app started from inside another app gets the keys until it exits.
/// </summary>
internal sealed class InputPump
{
    private const int IdleSleepMs = 5;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<IInputSource, InputPump> Pumps = new(ReferenceEqualityComparer.Instance);

    private readonly IInputSource _source;
    private readonly List<Subscription> _subscribers = [];
    private readonly List<ConsoleKeyInfo> _orphans = [];
    private Thread? _thread;

    private InputPump(IInputSource source) => _source = source;

    /// <summary>The default console input, shared by every app that doesn't set its own.</summary>
    public static IInputSource Console { get; } = new ConsoleInputSource();

    /// <summary>
    /// Deliver keys from <paramref name="source"/> to <paramref name="sink"/> until the subscription is disposed.
    /// The sink is called on the reader thread with each burst of keys.
    /// </summary>
    public static IDisposable Subscribe(IInputSource source, Action<ConsoleKeyInfo[]> sink)
    {
        lock (Gate)
        {
            if (!Pumps.TryGetValue(source, out var pump)) Pumps[source] = pump = new InputPump(source);
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

    /// <summary>Take a key read by the pump after its last subscriber left.</summary>
    public static bool TryTakeOrphan(IInputSource source, out ConsoleKeyInfo key)
    {
        lock (Gate)
        {
            if (Pumps.TryGetValue(source, out var pump) && pump._orphans.Count > 0)
            {
                key = pump._orphans[0];
                pump._orphans.RemoveAt(0);
                return true;
            }
        }
        key = default;
        return false;
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
                Thread.Sleep(IdleSleepMs);
                continue;
            }

            lock (Gate)
            {
                if (_subscribers.Count > 0) _subscribers[^1].Sink([.. burst]);
                else _orphans.AddRange(burst);
            }
        }
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
            lock (Gate) pump._subscribers.Remove(this);
        }
    }
}
