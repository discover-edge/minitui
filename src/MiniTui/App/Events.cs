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

/// <summary>A mouse button, in a <see cref="MouseEvent"/>.</summary>
public enum MouseButton
{
    /// <summary>No button: a scroll, or a release when the terminal doesn't say which button was released.</summary>
    None,

    /// <summary>The left (primary) button.</summary>
    Left,

    /// <summary>The middle button or wheel click.</summary>
    Middle,

    /// <summary>The right (secondary) button.</summary>
    Right,
}

/// <summary>What happened in a <see cref="MouseEvent"/>.</summary>
public enum MouseAction
{
    /// <summary>A button went down.</summary>
    Press,

    /// <summary>A button went up.</summary>
    Release,

    /// <summary>The wheel scrolled up (away from the user).</summary>
    ScrollUp,

    /// <summary>The wheel scrolled down (toward the user).</summary>
    ScrollDown,

    /// <summary>The wheel scrolled left.</summary>
    ScrollLeft,

    /// <summary>The wheel scrolled right.</summary>
    ScrollRight,

    /// <summary>
    /// The pointer moved with a button held (1.x spec §10.7); <see cref="MouseEvent.Button"/> is the held button.
    /// Drags go to the component that got the press, even outside it.
    /// </summary>
    Drag,
}

/// <summary>
/// A mouse report, delivered while <see cref="App.WithMouse"/> is on (1.x spec §4). <paramref name="X"/> and
/// <paramref name="Y"/> are 0-based cells, like <see cref="Buffer"/> coordinates.
/// </summary>
public sealed record MouseEvent(MouseAction Action, MouseButton Button, int X, int Y, ConsoleModifiers Modifiers = 0) : Event
{
    /// <summary>The cell, as a <see cref="Vec2"/>.</summary>
    public Vec2 Position => new(X, Y);

    /// <summary>True for a left-button press, the event clicks act on.</summary>
    public bool IsClick => Action == MouseAction.Press && Button == MouseButton.Left;
}

/// <summary>The terminal was resized. The next frame is a full redraw.</summary>
public sealed record ResizeEvent(int Width, int Height) : Event;

/// <summary>
/// The app took the terminal back (1.x spec §5): after <see cref="App.Suspend"/>, after the process was stopped and
/// continued, and after a SIGCONT the app didn't cause. The next frame is a full redraw.
/// </summary>
public sealed record ResumeEvent : Event;

/// <summary>
/// Raised through <see cref="App.OnEvent"/> when <see cref="App.TerminalInfo"/> changes because a reply to the
/// startup queries arrived (1.x spec §12.3).
/// </summary>
public sealed record TerminalInfoEvent(TerminalInfo Info) : Event;

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
