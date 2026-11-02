using System.Diagnostics;

namespace MiniTui;

/// <summary>
/// The default input source: on Linux and macOS it puts the terminal in raw mode, and on Windows the console in VT
/// input mode, and decodes the input bytes itself, so Esc, Alt, function keys and modifier combinations decode the
/// same way everywhere, and a key sequence split across reads is reassembled instead of producing a stray Esc. It
/// also turns on bracketed paste, so a paste arrives as one <see cref="PasteEvent"/> instead of keys. Conhost has
/// no bracketed paste, so there Ctrl+V pastes the clipboard's text as a <see cref="PasteEvent"/>. Wherever raw or
/// VT input isn't available, it reads keys like <see cref="ConsoleInputSource"/>, and pasted text arrives as keys.
/// </summary>
/// <remarks>
/// In raw mode the terminal doesn't handle Ctrl+C, Ctrl+Z, Ctrl+\, Ctrl+S or Ctrl+Q itself: they reach the app as
/// keys. Ctrl+C still quits through <see cref="App.Keys"/>. Set the <c>MINITUI_INPUT</c> environment variable to
/// <c>console</c> to make <see cref="ConsoleInputSource"/> the default instead.
/// </remarks>
public sealed class TerminalInputSource : IInputSource, IInputItemSource, IInputLifecycle, IWaitableInputSource, IMouseCapture
{
    private static readonly TimeSpan MaxEscapeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ModeCheckInterval = TimeSpan.FromMilliseconds(250);

    // After a resume, .NET reapplies its own terminal mode at some point (1.x spec §5.5), so check often for a while
    private static readonly TimeSpan FastModeCheckInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan FastModeCheckPeriod = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    private readonly int _fd;
    private readonly int _outputFd;
    private readonly AnsiInputDecoder _decoder;
    private readonly ItemQueue _items = new();
    private readonly IInputSink _sink;
    private readonly WindowsInputTranslator _translator;
    private readonly InputRecord[] _records = new InputRecord[256];
    private readonly ConsoleInputSource _fallback = new();
    private readonly byte[] _buffer = new byte[4096];
    private readonly long _started = Stopwatch.GetTimestamp();
    private volatile bool _raw;
    private bool _closed;
    private TimeSpan _lastModeCheck;
    private TimeSpan _fastModeCheckUntil;
    private int _mouseRefs;
    private KeyboardProtocol _keyboard;

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
        if (OperatingSystem.IsWindows())
        {
            _sink = new ClipboardPasteSink(_items, WindowsConsoleInput.ReadClipboardText);
            _translator = new WindowsInputTranslator(WindowsConsoleInput.WindowOrigin);
        }
        else
        {
            _sink = _items;
            _translator = new WindowsInputTranslator(() => default);
        }
    }

    /// <summary>The default escape timeout, 50 ms.</summary>
    public static TimeSpan DefaultEscapeTimeout { get; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a lone Esc byte waits for the rest of a sequence.</summary>
    public TimeSpan EscapeTimeout { get; }

    /// <summary>True while this source has the terminal in raw mode.</summary>
    internal bool IsRaw => _raw;

    private TimeSpan Now => Stopwatch.GetElapsedTime(_started);

    /// <summary>Read the next key if one is available, skipping any pastes and mouse events ahead of it. Never blocks.</summary>
    public bool TryRead(out ConsoleKeyInfo key)
    {
        while (((IInputItemSource)this).TryReadItem(out var item))
        {
            if (!item.IsKey) continue;
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
            else if (!_closed && OperatingSystem.IsWindows()) ReadAvailableWindows();
            _decoder.Expire(Now, _sink);
            return _items.TryDequeue(out item);
        }
    }

    /// <summary>Restore the terminal mode if this source changed it.</summary>
    public void Dispose() => ((IInputLifecycle)this).Deactivate();

    void IInputLifecycle.Activate()
    {
        lock (_lock)
        {
            if (_raw) return;
            if (OperatingSystem.IsWindows())
            {
                // Only the real console: the internal constructor's fds are for Unix pty tests
                if (_fd == 0) _raw = WindowsConsoleInput.TryEnterVtInput();
            }
            else if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && UnixTerminal.IsSupported)
            {
                _raw = UnixTerminal.TryEnterRaw(_fd, _outputFd);
            }
            _closed = false;
            _lastModeCheck = Now;
            if (_raw && _mouseRefs > 0) ApplyMouse(true);

            // The kitty protocol and modifyOtherKeys come back from the replies to the next queries; win32-input-mode has none
            if (_raw && _keyboard == KeyboardProtocol.Win32InputMode) ApplyKeyboard(_keyboard);
        }
    }

    void IMouseCapture.SetMouseCapture(bool enabled)
    {
        lock (_lock)
        {
            var before = _mouseRefs > 0;
            _mouseRefs = Math.Max(0, _mouseRefs + (enabled ? 1 : -1));
            var after = _mouseRefs > 0;
            if (before != after && _raw) ApplyMouse(after);
        }
    }

    /// <summary>Switch the keyboard protocol (1.x spec §12.5). Restoring the terminal turns it off.</summary>
    internal void SetKeyboardProtocol(KeyboardProtocol protocol)
    {
        lock (_lock)
        {
            _keyboard = protocol;
            if (_raw) ApplyKeyboard(protocol);
        }
    }

    private static void ApplyKeyboard(KeyboardProtocol protocol)
    {
        if (OperatingSystem.IsWindows()) WindowsConsoleInput.SetKeyboard(protocol);
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) UnixTerminal.SetKeyboard(protocol);
    }

    private static void ApplyMouse(bool enabled)
    {
        if (OperatingSystem.IsWindows()) WindowsConsoleInput.SetMouse(enabled);
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) UnixTerminal.SetMouse(enabled);
    }

    void IInputLifecycle.Deactivate()
    {
        lock (_lock)
        {
            if (!_raw) return;
            _raw = false;
            RestoreTerminalMode();
        }
    }

    void IInputLifecycle.Suspend()
    {
        lock (_lock)
        {
            if (!_raw) return;

            // A held Esc is the user's key, typed before the suspend
            _decoder.Expire(TimeSpan.MaxValue, _sink);
            _raw = false;
            if (_keyboard != KeyboardProtocol.Win32InputMode) _keyboard = KeyboardProtocol.Legacy;
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) UnixTerminal.Suspend();
            else if (OperatingSystem.IsWindows()) WindowsConsoleInput.Suspend();
        }
    }

    void IInputLifecycle.Resume()
    {
        ((IInputLifecycle)this).Activate();
        CheckModeSoon();
    }

    /// <summary>Check the terminal mode often for the next second, e.g. after SIGCONT, when .NET may reset it.</summary>
    internal void CheckModeSoon()
    {
        lock (_lock) _fastModeCheckUntil = Now + FastModeCheckPeriod;
    }

    bool IWaitableInputSource.WaitForInput(TimeSpan timeout)
    {
        if (!_raw)
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
            if (Now < _fastModeCheckUntil) wait = Min(wait, FastModeCheckInterval);
        }
        if (wait <= TimeSpan.Zero) return true;

        // Wake for input, or when held bytes are due to resolve
        var ms = (int)Math.Ceiling(wait.TotalMilliseconds);
        if (OperatingSystem.IsWindows()) return WindowsConsoleInput.Wait(ms) || holding;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return UnixTerminal.Poll(_fd, ms) || holding;
        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private void ReadAvailable()
    {
        if (UnixTerminal.Poll(_fd, 0))
        {
            var n = UnixTerminal.Read(_fd, _buffer);
            if (n > 0) _decoder.Feed(_buffer.AsSpan(0, n), Now, _sink);
            else _closed = true; // the terminal went away; SIGHUP handling restores it
        }

        var interval = Now < _fastModeCheckUntil ? FastModeCheckInterval : ModeCheckInterval;
        if (Now - _lastModeCheck >= interval)
        {
            _lastModeCheck = Now;
            UnixTerminal.Reapply();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void ReadAvailableWindows()
    {
        var n = WindowsConsoleInput.Read(_records);
        if (n < 0)
        {
            _closed = true; // the console went away
            return;
        }
        if (n == 0) return;

        _translator.Translate(_records.AsSpan(0, n), _decoder, Now, _sink, out var resized);
        if (resized) TerminalSession.RaiseResized();
    }

    /// <summary>Restore the terminal mode on exit and signal paths, whichever source changed it.</summary>
    internal static void RestoreTerminalMode()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) UnixTerminal.Restore();
        else if (OperatingSystem.IsWindows()) WindowsConsoleInput.Restore();
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private sealed class ItemQueue : IInputSink
    {
        private readonly Queue<InputItem> _queue = new();
        private bool _win32Seen;

        public int Count => _queue.Count;

        public void Key(ConsoleKeyInfo key) => _queue.Enqueue(InputItem.FromKey(key));

        public void Paste(string text) => _queue.Enqueue(InputItem.FromPaste(text));

        public void Mouse(MouseEvent mouse) => _queue.Enqueue(InputItem.FromMouse(mouse));

        // Every win32-input-mode key reports the mode; the app needs to hear it once
        public void Reply(TerminalReply reply)
        {
            if (reply is TerminalReply.Win32InputMode)
            {
                if (_win32Seen) return;
                _win32Seen = true;
            }
            _queue.Enqueue(InputItem.FromReply(reply));
        }

        public bool TryDequeue(out InputItem item) => _queue.TryDequeue(out item);
    }
}
