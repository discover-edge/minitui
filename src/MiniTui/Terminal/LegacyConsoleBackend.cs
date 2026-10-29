namespace MiniTui;

/// <summary>
/// Fallback backend for consoles without VT support (§5.6), using the <see cref="Console"/> API.
/// Limited to 16 colors; no alternate screen or synchronized output. Reversed is emulated by
/// swapping colors; other modifiers aren't shown.
/// </summary>
public sealed class LegacyConsoleBackend : ITerminalBackend, IConsoleDevice
{
    private readonly Lock _lock = new();
    private bool _entered;
    private bool _savedTreatControlCAsInput;
    private bool _cursorVisible = true;
    private (int X, int Y)? _lastCursor;
    private int _height = 24;

    /// <summary>Create a legacy backend. The color mode is capped at <see cref="ColorMode.Ansi16"/>.</summary>
    public LegacyConsoleBackend(ColorMode colorMode = ColorMode.Ansi16) =>
        ColorMode = colorMode > ColorMode.Ansi16 ? ColorMode.Ansi16 : colorMode;

    /// <summary>The color mode colors are downsampled to.</summary>
    public ColorMode ColorMode { get; }

    object IConsoleDevice.DeviceKey => ConsoleDevice.Key;

    /// <inheritdoc />
    public (int Width, int Height) GetSize()
    {
        var size = SystemConsoleHost.Instance.GetWindowSize();
        _height = size.Height;
        return size;
    }

    /// <inheritdoc />
    /// <exception cref="TerminalNotSupportedException">stdin or stdout is redirected.</exception>
    public void Enter()
    {
        lock (_lock)
        {
            if (_entered) return;
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
                throw new TerminalNotSupportedException(
                    "MiniTui requires an interactive terminal (stdin/stdout must not be redirected).");

            _savedTreatControlCAsInput = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = true;
            SetCursorVisible(false);
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
            Try(Console.ResetColor);
            Try(() => Console.SetCursorPosition(0, Math.Max(0, _height - 1)));
            Try(() => SetCursorVisible(true));
            Try(() => Console.TreatControlCAsInput = _savedTreatControlCAsInput);
        }
    }

    /// <inheritdoc />
    public void Draw(IReadOnlyList<CellUpdate> updates, (int X, int Y)? cursor, bool fullRedraw)
    {
        ArgumentNullException.ThrowIfNull(updates);
        lock (_lock)
        {
            if (!_entered) return;
            if (!fullRedraw && updates.Count == 0 && cursor == _lastCursor) return;

            // Without an alternate screen, a full redraw has to clear the visible screen
            Console.ResetColor();
            if (fullRedraw) Try(Console.Clear);

            var lastFg = Color.Reset;
            var lastBg = Color.Reset;
            for (var i = 0; i < updates.Count; i++)
            {
                var (x, y, cell) = updates[i];
                var fg = cell.Fg.ToMode(ColorMode);
                var bg = cell.Bg.ToMode(ColorMode);
                if ((cell.Modifiers & Modifiers.Reversed) != 0)
                    (fg, bg) = (bg.Kind == ColorKind.Reset ? Color.Black : bg, fg.Kind == ColorKind.Reset ? Color.Gray : fg);

                if (fg != lastFg || bg != lastBg)
                {
                    Console.ResetColor();
                    if (fg.Kind == ColorKind.Named) Console.ForegroundColor = fg.ToConsoleColor();
                    if (bg.Kind == ColorKind.Named) Console.BackgroundColor = bg.ToConsoleColor();
                    lastFg = fg;
                    lastBg = bg;
                }

                Try(() =>
                {
                    Console.SetCursorPosition(x, y);
                    Console.Write(cell.Symbol);
                });
            }
            Console.ResetColor();

            if (cursor is var (cx, cy))
            {
                Try(() => Console.SetCursorPosition(cx, cy));
                SetCursorVisible(true);
            }
            else
            {
                SetCursorVisible(false);
            }
            _lastCursor = cursor;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Leave();

    private void SetCursorVisible(bool visible)
    {
        if (_cursorVisible == visible) return;
        Try(() => Console.CursorVisible = visible);
        _cursorVisible = visible;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Console writes and terminal restore are best-effort.")]
    private static void Try(Action step)
    {
        try { step(); }
        catch (Exception) { }
    }
}
