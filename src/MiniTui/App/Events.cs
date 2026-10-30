namespace MiniTui;

/// <summary>
/// Something the app loop delivers to <see cref="App.OnEvent"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "The name is part of the 1.0 API (§6.1); VB callers can bracket it.")]
public abstract record Event;

/// <summary>A key press.</summary>
public sealed record KeyEvent(ConsoleKeyInfo Key) : Event;

/// <summary>A paste from the terminal (bracketed paste, 1.x spec §3). Line breaks are normalized to "\n".</summary>
public sealed record PasteEvent(string Text) : Event;

/// <summary>The terminal was resized. The next frame is a full redraw.</summary>
public sealed record ResizeEvent(int Width, int Height) : Event;

/// <summary>A tick from <see cref="App.WithTickRate"/>. <paramref name="Elapsed"/> is the time since the previous tick.</summary>
public sealed record TickEvent(TimeSpan Elapsed) : Event;

/// <summary>
/// What the app does after <see cref="App.OnError"/> sees an exception.
/// </summary>
public enum ErrorAction
{
    /// <summary>Keep running.</summary>
    Continue,

    /// <summary>Stop the loop and return from <see cref="App.Run"/> normally.</summary>
    Quit,

    /// <summary>Restore the terminal, then rethrow the exception from <see cref="App.Run"/>. The default.</summary>
    Rethrow,
}
