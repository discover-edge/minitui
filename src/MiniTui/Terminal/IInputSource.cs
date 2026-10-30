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

/// <summary>What <see cref="InputPump"/> carries: a key, a paste or a mouse event, in arrival order (1.x spec §3.3, §4.3).</summary>
internal readonly struct InputItem
{
    private InputItem(ConsoleKeyInfo key, string? paste, MouseEvent? mouse)
    {
        Key = key;
        Paste = paste;
        Mouse = mouse;
    }

    public ConsoleKeyInfo Key { get; }

    /// <summary>The pasted text, or null unless a paste.</summary>
    public string? Paste { get; }

    /// <summary>The mouse event, or null unless a mouse event.</summary>
    public MouseEvent? Mouse { get; }

    /// <summary>True for a key.</summary>
    public bool IsKey => Paste is null && Mouse is null;

    public static InputItem FromKey(ConsoleKeyInfo key) => new(key, null, null);

    public static InputItem FromPaste(string text) => new(default, text, null);

    public static InputItem FromMouse(MouseEvent mouse) => new(default, null, mouse);
}

/// <summary>A source that produces pastes and mouse events as well as keys.</summary>
internal interface IInputItemSource
{
    /// <summary>Read a key, paste or mouse event if one is available. Never blocks.</summary>
    bool TryReadItem(out InputItem item);
}

/// <summary>Start and stop hooks <see cref="InputPump"/> calls on sources that change the terminal's mode.</summary>
internal interface IInputLifecycle
{
    /// <summary>The first app started reading: enter raw mode.</summary>
    void Activate();

    /// <summary>The last app stopped reading: restore the terminal mode.</summary>
    void Deactivate();

    /// <summary>An app is giving the terminal back for a while (1.x spec §5.4): restore the terminal mode.</summary>
    void Suspend() => Deactivate();

    /// <summary>The app is taking the terminal again after <see cref="Suspend"/>.</summary>
    void Resume() => Activate();
}

/// <summary>A source that can have the terminal report the mouse (1.x spec §4.6).</summary>
internal interface IMouseCapture
{
    /// <summary>Take (true) or release (false) a reference: the mouse is reported while any is held.</summary>
    void SetMouseCapture(bool enabled);
}

/// <summary>A source <see cref="InputPump"/> can wait on instead of polling.</summary>
internal interface IWaitableInputSource
{
    /// <summary>Block until input may be available or <paramref name="timeout"/> passes.</summary>
    /// <returns>True if <see cref="IInputSource.TryRead"/> may have a key.</returns>
    bool WaitForInput(TimeSpan timeout);
}

/// <summary>
/// Reads keys from <see cref="Console"/>. This was the default before 1.1; <see cref="TerminalInputSource"/> is now.
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
public sealed class ScriptedInputSource : IInputSource, IInputItemSource
{
    private readonly ConcurrentQueue<InputItem> _items = new();

    /// <summary>The number of keys, pastes and mouse events not yet read.</summary>
    public int Pending => _items.Count;

    /// <summary>Queue keys to be read in order.</summary>
    public ScriptedInputSource Enqueue(params ConsoleKeyInfo[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys) _items.Enqueue(InputItem.FromKey(key));
        return this;
    }

    /// <summary>Queue a paste, in order with the queued keys. An empty paste is ignored, as terminals' are.</summary>
    public ScriptedInputSource EnqueuePaste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0) _items.Enqueue(InputItem.FromPaste(text));
        return this;
    }

    /// <summary>Queue a mouse event, in order with the queued keys and pastes.</summary>
    public ScriptedInputSource EnqueueMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        _items.Enqueue(InputItem.FromMouse(mouse));
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
            _items.Enqueue(InputItem.FromKey(new ConsoleKeyInfo(c, KeyFor(c), shift: char.IsUpper(c), alt: false, control: false)));
        return this;
    }

    /// <summary>Read the next key, skipping any pastes and mouse events ahead of it.</summary>
    public bool TryRead(out ConsoleKeyInfo key)
    {
        while (_items.TryDequeue(out var item))
        {
            if (!item.IsKey) continue;
            key = item.Key;
            return true;
        }
        key = default;
        return false;
    }

    bool IInputItemSource.TryReadItem(out InputItem item) => _items.TryDequeue(out item);

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
