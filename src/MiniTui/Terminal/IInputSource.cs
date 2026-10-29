using System.Collections.Concurrent;

namespace MiniTui;

/// <summary>
/// A source of key presses.
/// </summary>
public interface IInputSource : IDisposable
{
    /// <summary>Read a key if one is available. Never blocks.</summary>
    bool TryRead(out ConsoleKeyInfo key);
}

/// <summary>
/// Reads keys from <see cref="Console"/>.
/// </summary>
public sealed class ConsoleInputSource : IInputSource
{
    /// <inheritdoc />
    public bool TryRead(out ConsoleKeyInfo key)
    {
        if (Console.KeyAvailable)
        {
            key = Console.ReadKey(intercept: true);
            return true;
        }
        key = default;
        return false;
    }

    /// <inheritdoc />
    public void Dispose() { }
}

/// <summary>
/// An input source fed by code, for tests. Thread-safe.
/// </summary>
public sealed class ScriptedInputSource : IInputSource
{
    private readonly ConcurrentQueue<ConsoleKeyInfo> _keys = new();

    /// <summary>The number of keys not yet read.</summary>
    public int Pending => _keys.Count;

    /// <summary>Queue keys to be read in order.</summary>
    public ScriptedInputSource Enqueue(params ConsoleKeyInfo[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys) _keys.Enqueue(key);
        return this;
    }

    /// <summary>
    /// Queue one key per character. Letters, digits, space, tab and line breaks get their
    /// <see cref="ConsoleKey"/>; uppercase letters are shifted.
    /// </summary>
    public ScriptedInputSource EnqueueText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var c in text)
            _keys.Enqueue(new ConsoleKeyInfo(c, KeyFor(c), shift: char.IsUpper(c), alt: false, control: false));
        return this;
    }

    /// <inheritdoc />
    public bool TryRead(out ConsoleKeyInfo key) => _keys.TryDequeue(out key);

    /// <inheritdoc />
    public void Dispose() { }

    private static ConsoleKey KeyFor(char c) => c switch
    {
        >= 'a' and <= 'z' => ConsoleKey.A + (c - 'a'),
        >= 'A' and <= 'Z' => ConsoleKey.A + (c - 'A'),
        >= '0' and <= '9' => ConsoleKey.D0 + (c - '0'),
        ' ' => ConsoleKey.Spacebar,
        '\t' => ConsoleKey.Tab,
        '\r' or '\n' => ConsoleKey.Enter,
        '\b' => ConsoleKey.Backspace,
        '\x1b' => ConsoleKey.Escape,
        _ => 0
    };
}
