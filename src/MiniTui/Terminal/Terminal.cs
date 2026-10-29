namespace MiniTui;

/// <summary>
/// Manages the actual terminal - handles double-buffering and efficient rendering.
/// Only changed cells are sent to the <see cref="ITerminalBackend"/>.
/// </summary>
/// <remarks>
/// Terminals share a process-wide session: creating a <see cref="Terminal"/> while another is active
/// (for example, an app started from inside another app) reuses it, and the outer terminal redraws
/// fully on its next frame. The terminal is restored when the last one is disposed.
/// </remarks>
public class Terminal : IDisposable
{
    private readonly TerminalLease _lease;
    private readonly ITerminalBackend _backend;
    private readonly List<CellUpdate> _updates = [];
    private Buffer _current;
    private Buffer _previous;
    private bool _disposed;
    private bool _fullRedraw = true;
    private int _seenVersion;

    /// <summary>
    /// Terminal width in cells.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Terminal height in cells.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// The full terminal area as a Rect.
    /// </summary>
    public Rect Area => new(0, 0, Width, Height);

    /// <summary>
    /// Create a terminal on the default backend: <see cref="AnsiBackend"/>, or
    /// <see cref="LegacyConsoleBackend"/> on Windows consoles without VT support.
    /// Enters the alternate screen unless a terminal session is already active.
    /// </summary>
    /// <exception cref="TerminalNotSupportedException">stdin or stdout is redirected.</exception>
    public Terminal() : this(CreateDefaultBackend()) { }

    /// <summary>
    /// Create a terminal on a specific backend, such as <see cref="TestBackend"/>.
    /// </summary>
    public Terminal(ITerminalBackend backend)
    {
        _lease = TerminalSession.Acquire(backend);
        _backend = _lease.Backend;
        _seenVersion = _lease.Version;

        (Width, Height) = Sanitize(_backend.GetSize());
        _current = new Buffer(Width, Height);
        _previous = new Buffer(Width, Height);
    }

    /// <summary>
    /// Main render method. Pass a function that draws to the buffer.
    /// </summary>
    public void Draw(Action<Buffer> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        _current.Clear();
        render(_current);
        Flush();
    }

    /// <summary>
    /// Send changed cells to the backend. Allocates nothing when the frame is unchanged.
    /// </summary>
    private void Flush()
    {
        // Another terminal drew on this session since our last frame (e.g. a nested app)
        if (_lease.Version != _seenVersion)
        {
            _seenVersion = _lease.Version;
            _fullRedraw = true;
        }

        _updates.Clear();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = _current.Get(x, y);

                // A wide head covers its continuation, so continuations are never sent
                if (cell.IsContinuation) continue;
                if (_fullRedraw || cell != _previous.Get(x, y))
                    _updates.Add(new CellUpdate(x, y, cell));
            }
        }

        _backend.Draw(_updates, _current.CursorRequest, _fullRedraw);
        _fullRedraw = false;
        (_current, _previous) = (_previous, _current);
    }

    /// <summary>
    /// Force a complete redraw next frame (useful after terminal resize).
    /// </summary>
    public void Invalidate() => _fullRedraw = true;

    /// <summary>
    /// Check if terminal was resized. If so, recreates buffers and returns true.
    /// </summary>
    public bool CheckResize()
    {
        var (width, height) = Sanitize(_backend.GetSize());
        if (width == Width && height == Height) return false;

        Width = width;
        Height = height;
        _current = new Buffer(Width, Height);
        _previous = new Buffer(Width, Height);
        _fullRedraw = true;
        return true;
    }

    /// <summary>
    /// Dispose the terminal. The last terminal in the session restores the original console state.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lease.Dispose();
        GC.SuppressFinalize(this);
    }

    private static (int Width, int Height) Sanitize((int Width, int Height) size) =>
        (Math.Max(0, size.Width), Math.Max(0, size.Height));

    private static ITerminalBackend CreateDefaultBackend()
    {
        var mode = ColorCapability.Detect();
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected && !WindowsConsole.SupportsVirtualTerminal())
            return new LegacyConsoleBackend(mode);
        return new AnsiBackend(mode);
    }
}
