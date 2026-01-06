namespace MiniTui;

/// <summary>
/// Simple input handling utilities.
/// </summary>
public static class Input
{
    /// <summary>
    /// Non-blocking key poll. Returns null if no key is available.
    /// </summary>
    public static ConsoleKeyInfo? Poll()
    {
        if (Console.KeyAvailable)
            return Console.ReadKey(intercept: true);
        return null;
    }

    /// <summary>
    /// Blocking key read. Waits for a key press.
    /// </summary>
    public static ConsoleKeyInfo Read() => Console.ReadKey(intercept: true);

    /// <summary>
    /// Check if a specific key was pressed. Convenience for common patterns.
    /// </summary>
    public static bool IsKey(ConsoleKeyInfo? key, ConsoleKey target) 
        => key?.Key == target;

    public static bool IsChar(ConsoleKeyInfo? key, char target) 
        => key?.KeyChar == target;

    /// <summary>
    /// Check for common exit keys (q, Escape, Ctrl+C).
    /// </summary>
    public static bool IsQuit(ConsoleKeyInfo? key)
    {
        if (key == null) return false;
        return key.Value.KeyChar == 'q' 
            || key.Value.Key == ConsoleKey.Escape
            || (key.Value.Key == ConsoleKey.C && key.Value.Modifiers.HasFlag(ConsoleModifiers.Control));
    }
}

/// <summary>
/// Simple focus manager for navigating between UI elements.
/// </summary>
public class FocusManager
{
    private int _current;
    private readonly int _count;

    public int Current => _current;

    public FocusManager(int elementCount)
    {
        _count = elementCount;
        _current = 0;
    }

    public void Next() => _current = (_current + 1) % _count;
    public void Previous() => _current = (_current - 1 + _count) % _count;
    public void Set(int index) => _current = Math.Clamp(index, 0, _count - 1);

    public bool IsFocused(int index) => _current == index;

    /// <summary>
    /// Handle common navigation keys. Returns true if handled.
    /// </summary>
    public bool HandleInput(ConsoleKeyInfo? key)
    {
        if (key == null) return false;

        switch (key.Value.Key)
        {
            case ConsoleKey.Tab:
                if (key.Value.Modifiers.HasFlag(ConsoleModifiers.Shift))
                    Previous();
                else
                    Next();
                return true;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                Next();
                return true;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                Previous();
                return true;
        }

        return false;
    }
}
