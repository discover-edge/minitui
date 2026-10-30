using System.Runtime.InteropServices;

namespace MiniTui;

/// <summary>
/// Process-wide, ref-counted terminal sessions (§5.4). The first lease on a device enters the
/// terminal and the last one leaves it, so nested apps share one session instead of each
/// clearing the screen. While a console session is active, the terminal is also restored on
/// SIGTERM, SIGHUP, SIGQUIT, process exit and unhandled exceptions.
/// </summary>
internal static class TerminalSession
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<object, Session> Sessions = [];

    /// <summary>
    /// Raised on SIGWINCH (Unix), or a window size event read by the input thread (Windows), while a console
    /// session is active. Runs on a signal or input thread.
    /// </summary>
    public static event Action? Resized;

    /// <summary>Raise <see cref="Resized"/>; for input sources that see size changes.</summary>
    internal static void RaiseResized() => Resized?.Invoke();

    /// <summary>
    /// Join the session for <paramref name="backend"/>'s device, entering the terminal if this is the first lease.
    /// </summary>
    public static TerminalLease Acquire(ITerminalBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        var key = backend is IConsoleDevice device ? device.DeviceKey : backend;

        lock (Gate)
        {
            if (!Sessions.TryGetValue(key, out var session))
            {
                backend.Enter();
                session = new Session(backend);
                if (ReferenceEquals(key, ConsoleDevice.Key)) session.RegisterRestoreHandlers();
                Sessions[key] = session;
            }
            session.Count++;
            session.Version++;
            return new TerminalLease(session, key);
        }
    }

    /// <summary>True if a session is active for the given backend's device.</summary>
    public static bool IsActive(ITerminalBackend backend)
    {
        var key = backend is IConsoleDevice device ? device.DeviceKey : backend;
        lock (Gate) return Sessions.ContainsKey(key);
    }

    internal static void Release(Session session, object key)
    {
        lock (Gate)
        {
            session.Version++;
            if (--session.Count > 0) return;
            Sessions.Remove(key);
            session.UnregisterRestoreHandlers();
            session.Backend.Leave();
        }
    }

    internal sealed class Session(ITerminalBackend backend)
    {
        private readonly List<IDisposable> _registrations = [];
        private EventHandler? _onProcessExit;
        private UnhandledExceptionEventHandler? _onUnhandledException;

        public ITerminalBackend Backend { get; } = backend;
        public int Count { get; set; }

        /// <summary>Bumped on every acquire and release, so leases can tell that someone else drew.</summary>
        public int Version { get; set; }

        public void RegisterRestoreHandlers()
        {
            // The handlers restore the terminal and let termination proceed
            foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGQUIT })
                TryRegister(signal, _ => Restore());
            if (!OperatingSystem.IsWindows())
                TryRegister(PosixSignal.SIGWINCH, _ => Resized?.Invoke());

            _onProcessExit = (_, _) => Restore();
            _onUnhandledException = (_, _) => Restore();
            AppDomain.CurrentDomain.ProcessExit += _onProcessExit;
            AppDomain.CurrentDomain.UnhandledException += _onUnhandledException;
        }

        private void Restore()
        {
            TerminalInputSource.RestoreTerminalMode();
            Backend.Leave();
        }

        public void UnregisterRestoreHandlers()
        {
            foreach (var registration in _registrations) registration.Dispose();
            _registrations.Clear();
            if (_onProcessExit is not null) AppDomain.CurrentDomain.ProcessExit -= _onProcessExit;
            if (_onUnhandledException is not null) AppDomain.CurrentDomain.UnhandledException -= _onUnhandledException;
            _onProcessExit = null;
            _onUnhandledException = null;
        }

        private void TryRegister(PosixSignal signal, Action<PosixSignalContext> handler)
        {
            try
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, handler));
            }
            catch (PlatformNotSupportedException) { }
        }
    }
}

/// <summary>
/// A share of the terminal session. Disposing the last lease restores the terminal.
/// </summary>
public sealed class TerminalLease : IDisposable
{
    private readonly TerminalSession.Session _session;
    private readonly object _key;
    private bool _disposed;

    internal TerminalLease(TerminalSession.Session session, object key)
    {
        _session = session;
        _key = key;
    }

    /// <summary>The session's backend: the one that entered the terminal.</summary>
    internal ITerminalBackend Backend => _session.Backend;

    /// <summary>Changes whenever any lease on this session is acquired or released.</summary>
    internal int Version => _session.Version;

    /// <summary>Release this share; the last release leaves the terminal.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TerminalSession.Release(_session, _key);
    }
}
