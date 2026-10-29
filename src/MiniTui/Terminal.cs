namespace MiniTui;

/// <summary>
/// Manages the actual terminal - handles double-buffering and efficient rendering.
/// </summary>
public class Terminal : IDisposable
{
    private Buffer _current;
    private Buffer _previous;
    private bool _disposed;

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
    private void Flush()
    {
        var lastFg = ConsoleColor.Gray;
        var lastBg = ConsoleColor.Black;
        Console.ForegroundColor = lastFg;
        Console.BackgroundColor = lastBg;

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = _current.Get(x, y);
                var prev = _previous.Get(x, y);

                // Only write changed cells - massive performance win
                if (cell != prev)
                {
                    // Only change colors when needed
                    if (cell.Fg != lastFg)
                    {
                        Console.ForegroundColor = cell.Fg;
                        lastFg = cell.Fg;
                    }
                    if (cell.Bg != lastBg)
                    {
                        Console.BackgroundColor = cell.Bg;
                        lastBg = cell.Bg;
                    }

                    try
                    {
                        Console.SetCursorPosition(x, y);
                        Console.Write(cell.Char);
                    }
                    catch
                    {
                        // Ignore write errors (can happen on resize)
                    }

                    _previous.Set(x, y, cell);
                }
            }
        }

        // Reset cursor to bottom-left to avoid visual artifacts
        try
        {
            Console.SetCursorPosition(0, Height - 1);
        }
        catch { }
    }

    /// <summary>
    /// Force a complete redraw next frame (useful after terminal resize).
    /// </summary>
    public void Invalidate()
    {
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            _previous.Set(x, y, new Cell('\0', ConsoleColor.Black, ConsoleColor.Black));
    }

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
