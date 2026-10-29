using System.Text;

namespace MiniTui;

/// <summary>
/// The default backend: writes ANSI/VT escape sequences to standard output (§5.2–5.3).
/// Uses the alternate screen, so the user's screen and scrollback are untouched.
/// </summary>
public sealed class AnsiBackend : ITerminalBackend, IConsoleDevice
{
    private const string RedirectedMessage =
        "MiniTui requires an interactive terminal (stdin/stdout must not be redirected).";

    private readonly IConsoleHost _host;
    private readonly AnsiEncoder _encoder;
    private readonly Lock _lock = new();
    private Stream? _output;
    private bool _entered;
    private bool _disposed;
    private (int X, int Y)? _lastCursor;

    // Saved by Enter, restored by Leave
    private bool _savedTreatControlCAsInput;
    private Encoding? _savedOutputEncoding;
    private uint? _savedOutputMode;

    /// <summary>Create a backend for the detected color mode (<see cref="ColorCapability.Detect(ColorMode?)"/>).</summary>
    public AnsiBackend() : this(ColorCapability.Detect()) { }

    /// <summary>Create a backend that downsamples colors to <paramref name="colorMode"/>.</summary>
    public AnsiBackend(ColorMode colorMode) : this(colorMode, SystemConsoleHost.Instance) { }

    internal AnsiBackend(ColorMode colorMode, IConsoleHost host)
    {
        _host = host;
        _encoder = new AnsiEncoder(colorMode);
    }

    /// <summary>The color mode colors are downsampled to.</summary>
    public ColorMode ColorMode => _encoder.Mode;

    object IConsoleDevice.DeviceKey => ReferenceEquals(_host, SystemConsoleHost.Instance) ? ConsoleDevice.Key : this;

    /// <inheritdoc />
    public (int Width, int Height) GetSize()
    {
        var size = _host.GetWindowSize();
        _encoder.ScreenWidth = size.Width;
        return size;
    }

    /// <inheritdoc />
    /// <exception cref="TerminalNotSupportedException">stdin or stdout is redirected.</exception>
    public void Enter()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entered) return;
            if (_host.IsOutputRedirected || _host.IsInputRedirected)
                throw new TerminalNotSupportedException(RedirectedMessage);

            if (_host.IsWindows)
            {
                if (_host.TryEnableVirtualTerminal(out var mode)) _savedOutputMode = mode;
                _savedOutputEncoding = _host.OutputEncoding;
                _host.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }

            _savedTreatControlCAsInput = _host.TreatControlCAsInput;
            _host.TreatControlCAsInput = true;

            _output ??= _host.OpenStandardOutput();
            _encoder.Clear();
            _encoder.Append("\e[?1049h\e[?25l\e[?7l\e[2J"u8);
            WriteOut();

            _encoder.Forget();
            _lastCursor = null;
            _entered = true;
        }
    }

    /// <inheritdoc />
    public void Leave()
    {
        lock (_lock)
        {
            if (!_entered) return;
            _entered = false;

            Try(() => Write("\e[0m"u8));
            Try(() => Write("\e[?7h"u8));
            Try(() => Write("\e[?25h"u8));
            Try(() => Write("\e[?1049l"u8));
            Try(() => _output?.Flush());
            Try(() => _host.TreatControlCAsInput = _savedTreatControlCAsInput);
            if (_savedOutputEncoding is { } encoding)
                Try(() => _host.OutputEncoding = encoding);
            if (_savedOutputMode is { } mode)
                Try(() => _host.RestoreOutputMode(mode));
            _savedOutputEncoding = null;
            _savedOutputMode = null;
        }
    }

    /// <inheritdoc />
    public void Draw(IReadOnlyList<CellUpdate> updates, (int X, int Y)? cursor, bool fullRedraw)
    {
        ArgumentNullException.ThrowIfNull(updates);
        lock (_lock)
        {
            // Never draw over the user's screen after Leave (e.g. a signal arrived mid-run)
            if (!_entered) return;
            if (!fullRedraw && updates.Count == 0 && cursor == _lastCursor) return;

            _encoder.Clear();
            _encoder.BeginFrame(fullRedraw);
            for (var i = 0; i < updates.Count; i++)
            {
                var update = updates[i];
                _encoder.WriteCell(update.X, update.Y, update.Cell);
            }
            _encoder.EndFrame(cursor);
            _lastCursor = cursor;
            WriteOut();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Leave();
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _encoder.Dispose();
            _output?.Dispose();
            _output = null;
        }
    }

    private void WriteOut()
    {
        _output!.Write(_encoder.Written);
        _output.Flush();
    }

    private void Write(ReadOnlySpan<byte> bytes) => _output?.Write(bytes);

    // Leave must never throw: one failing step can't stop the rest of the restore.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Terminal restore is best-effort and must never throw.")]
    private static void Try(Action step)
    {
        try { step(); }
        catch (Exception) { }
    }
}

/// <summary>
/// A backend that draws to the process console. Backends on the same device share one terminal session.
/// </summary>
internal interface IConsoleDevice
{
    object DeviceKey { get; }
}

internal static class ConsoleDevice
{
    /// <summary>The session key for the process's real console.</summary>
    public static readonly object Key = new();
}
