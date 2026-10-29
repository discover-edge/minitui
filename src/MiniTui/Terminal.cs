namespace MiniTui;

/// <summary>
/// Manages the actual terminal - handles double-buffering and efficient rendering.
/// </summary>
public class Terminal : IDisposable
{
    private Buffer _current;
    private Buffer _previous;
    private bool _disposed;
    private bool _fullRedraw;
    private bool _cursorVisible;
    private Color _lastFg;
    private Color _lastBg;

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
    /// Create a new terminal, entering alternate screen mode.
    /// </summary>
    public Terminal()
    {
        // Save original state
        Console.CursorVisible = false;
        
        // Handle terminal resize gracefully
        try
        {
            Width = Console.WindowWidth;
            Height = Console.WindowHeight;
        }
        catch
        {
            // Fallback for environments where this fails
            Width = 80;
            Height = 24;
        }

        _current = new Buffer(Width, Height);
        _previous = new Buffer(Width, Height);

        // Force full redraw on first frame
        Console.Clear();
    }

    /// <summary>
    /// Main render method. Pass a function that draws to the buffer.
    /// </summary>
    public void Draw(Action<Buffer> render)
    {
        _current.Clear();
        render(_current);
        Flush();
    }

    /// <summary>
    /// Flush changes to actual terminal. Only writes cells that changed.
    /// </summary>
    /// <remarks>
    /// Interim System.Console implementation: colors are downsampled to 16 and modifiers are not shown.
    /// Phase 2 replaces this with the ANSI backend.
    /// </remarks>
    private void Flush()
    {
        Console.ResetColor();
        _lastFg = Color.Reset;
        _lastBg = Color.Reset;

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = _current.Get(x, y);

                // Only write changed cells; the terminal draws a wide head's continuation itself
                if (cell.IsContinuation || (!_fullRedraw && cell == _previous.Get(x, y)))
                    continue;

                ApplyColors(cell.Fg, cell.Bg);
                try
                {
                    Console.SetCursorPosition(x, y);
                    Console.Write(cell.Symbol);
                }
                catch
                {
                    // Ignore write errors (can happen on resize)
                }
            }
        }

        _fullRedraw = false;
        UpdateCursor(_current.CursorRequest);
        (_current, _previous) = (_previous, _current);
    }

    private void ApplyColors(Color fg, Color bg)
    {
        fg = fg.ToMode(ColorMode.Ansi16);
        bg = bg.ToMode(ColorMode.Ansi16);
        if (fg == _lastFg && bg == _lastBg) return;

        if ((fg.Kind == ColorKind.Reset && _lastFg.Kind != ColorKind.Reset)
            || (bg.Kind == ColorKind.Reset && _lastBg.Kind != ColorKind.Reset))
        {
            Console.ResetColor();
            _lastFg = Color.Reset;
            _lastBg = Color.Reset;
        }

        if (fg != _lastFg) Console.ForegroundColor = fg.ToConsoleColor();
        if (bg != _lastBg) Console.BackgroundColor = bg.ToConsoleColor();
        _lastFg = fg;
        _lastBg = bg;
    }

    private void UpdateCursor((int X, int Y)? request)
    {
        try
        {
            if (request is var (x, y))
            {
                Console.SetCursorPosition(x, y);
                if (!_cursorVisible) Console.CursorVisible = _cursorVisible = true;
            }
            else
            {
                if (_cursorVisible) Console.CursorVisible = _cursorVisible = false;
                // Reset cursor to bottom-left to avoid visual artifacts
                Console.SetCursorPosition(0, Height - 1);
            }
        }
        catch { }
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
        try
        {
            var newWidth = Console.WindowWidth;
            var newHeight = Console.WindowHeight;

            if (newWidth != Width || newHeight != Height)
            {
                Width = newWidth;
                Height = newHeight;
                _current = new Buffer(Width, Height);
                _previous = new Buffer(Width, Height);
                Console.Clear();
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Dispose the terminal, restoring original console state.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Console.ResetColor();
        Console.CursorVisible = true;
        Console.Clear();
        GC.SuppressFinalize(this);
    }
}
