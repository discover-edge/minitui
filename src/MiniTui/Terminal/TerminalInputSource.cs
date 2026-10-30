using System.Diagnostics;

namespace MiniTui;

/// <summary>
/// The default input source: on Linux and macOS it puts the terminal in raw mode and decodes its input bytes
/// itself, so Esc, Alt, function keys and modifier combinations are reliable, and a key sequence split across
/// reads is reassembled instead of producing a stray Esc. It also turns on bracketed paste, so a paste arrives as
/// one <see cref="PasteEvent"/> instead of keys. On Windows, and wherever raw mode isn't available, it reads keys
/// like <see cref="ConsoleInputSource"/>, and pasted text arrives as keys.
/// </summary>
/// <remarks>
/// In raw mode the terminal doesn't handle Ctrl+C, Ctrl+Z, Ctrl+\, Ctrl+S or Ctrl+Q itself: they reach the app as
/// keys. Ctrl+C still quits through <see cref="App.Keys"/>. Set the <c>MINITUI_INPUT</c> environment variable to
/// <c>console</c> to make <see cref="ConsoleInputSource"/> the default instead.
/// </remarks>
public sealed class TerminalInputSource : IInputSource, IInputItemSource, IInputLifecycle, IWaitableInputSource
{
    private static readonly TimeSpan MaxEscapeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ModeCheckInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private readonly int _fd;
    private readonly int _outputFd;
    private readonly AnsiInputDecoder _decoder;
    private readonly ItemQueue _items = new();
    private readonly ConsoleInputSource _fallback = new();
    private readonly byte[] _buffer = new byte[4096];
    private readonly long _started = Stopwatch.GetTimestamp();
    private volatile bool _raw;
    private bool _closed;
    private TimeSpan _lastModeCheck;

    /// <summary>Create a source with the <see cref="DefaultEscapeTimeout"/>.</summary>
    public TerminalInputSource() : this(DefaultEscapeTimeout) { }

    /// <summary>Create a source with a custom escape timeout.</summary>
    /// <param name="escapeTimeout">
    /// How long to wait after an Esc byte for the rest of a key sequence before treating it as the Esc key.
    /// Zero resolves at the end of each read.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="escapeTimeout"/> is negative or over 1 second.</exception>
    public TerminalInputSource(TimeSpan escapeTimeout) : this(0, escapeTimeout, outputFd: 1) { }

    /// <summary>Read from <paramref name="fd"/>; bracketed paste is requested on <paramref name="outputFd"/> (−1 for none).</summary>
    internal TerminalInputSource(int fd, TimeSpan escapeTimeout, int outputFd = -1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(escapeTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(escapeTimeout, MaxEscapeTimeout);
        _fd = fd;
        _outputFd = outputFd;
        EscapeTimeout = escapeTimeout;
        _decoder = new AnsiInputDecoder(escapeTimeout);
    }

    /// <summary>The default escape timeout, 50 ms.</summary>
    public static TimeSpan DefaultEscapeTimeout { get; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a lone Esc byte waits for the rest of a sequence.</summary>
    public TimeSpan EscapeTimeout { get; }

    /// <summary>True while this source has the terminal in raw mode.</summary>
    internal bool IsRaw => _raw;

    private TimeSpan Now => Stopwatch.GetElapsedTime(_started);

    /// <summary>Read the next key if one is available, skipping any pastes ahead of it. Never blocks.</summary>
    public bool TryRead(out ConsoleKeyInfo key)
    {
        while (((IInputItemSource)this).TryReadItem(out var item))
        {
            if (item.Paste is not null) continue;
            key = item.Key;
            return true;
        }
        key = default;
        return false;
    }

    bool IInputItemSource.TryReadItem(out InputItem item)
    {
        lock (_lock)
        {
            if (_items.TryDequeue(out item)) return true;
            if (!_raw)
            {
                var read = _fallback.TryRead(out var key);
                item = InputItem.FromKey(key);
                return read;
            }

            if (!_closed && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) ReadAvailable();
            _decoder.Expire(Now, _items);
            return _items.TryDequeue(out item);
        }
    }

    /// <summary>Restore the terminal mode if this source changed it.</summary>
    public void Dispose() => ((IInputLifecycle)this).Deactivate();

    void IInputLifecycle.Activate()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        lock (_lock)
        {
            if (_raw || !UnixTerminal.IsSupported) return;
            _raw = UnixTerminal.TryEnterRaw(_fd, _outputFd);
            _closed = false;
            _lastModeCheck = Now;
        }
    }

    void IInputLifecycle.Deactivate()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        lock (_lock)
        {
            if (!_raw) return;
            _raw = false;
            UnixTerminal.Restore();
        }
    }

    bool IWaitableInputSource.WaitForInput(TimeSpan timeout)
    {
        if (!_raw || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            // Console.KeyAvailable can't be waited on, so poll it as InputPump always did
            Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(5, timeout.TotalMilliseconds)));
            return true;
        }

        TimeSpan wait;
        bool holding;
        lock (_lock)
        {
            if (_items.Count > 0) return true;
            if (_closed)
            {
                Thread.Sleep(timeout);
                return false;
            }
            holding = _decoder.Deadline is not null;
            wait = _decoder.Deadline is { } deadline ? Min(deadline - Now, timeout) : timeout;
        }
        if (wait <= TimeSpan.Zero) return true;

        // Wake for input, or when held bytes are due to resolve
        return UnixTerminal.Poll(_fd, (int)Math.Ceiling(wait.TotalMilliseconds)) || holding;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private void ReadAvailable()
    {
        if (UnixTerminal.Poll(_fd, 0))
        {
            var n = UnixTerminal.Read(_fd, _buffer);
            if (n > 0) _decoder.Feed(_buffer.AsSpan(0, n), Now, _items);
            else _closed = true; // the terminal went away; SIGHUP handling restores it
        }

        if (Now - _lastModeCheck >= ModeCheckInterval)
        {
            _lastModeCheck = Now;
            UnixTerminal.Reapply();
        }
    }

    /// <summary>Restore the terminal mode on exit and signal paths, whichever source changed it.</summary>
    internal static void RestoreTerminalMode()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) UnixTerminal.Restore();
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private sealed class ItemQueue : IInputSink
    {
        private readonly Queue<InputItem> _queue = new();

        public int Count => _queue.Count;

        public void Key(ConsoleKeyInfo key) => _queue.Enqueue(InputItem.FromKey(key));

        public void Paste(string text) => _queue.Enqueue(InputItem.FromPaste(text));

        public bool TryDequeue(out InputItem item) => _queue.TryDequeue(out item);
    }
}
