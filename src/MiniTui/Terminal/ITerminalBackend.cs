namespace MiniTui;

/// <summary>
/// Where frames go: a real terminal, the legacy console API, or an in-memory grid for tests.
/// <see cref="Terminal"/> owns the double buffer and diffing, and passes the backend only the changed cells.
/// </summary>
public interface ITerminalBackend : IDisposable
{
    /// <summary>The current terminal size in cells.</summary>
    (int Width, int Height) GetSize();

    /// <summary>Put the terminal into TUI mode (alternate screen, hidden cursor, ...). Idempotent.</summary>
    void Enter();

    /// <summary>Restore the terminal to how it was before <see cref="Enter"/>. Idempotent and never throws.</summary>
    void Leave();

    /// <summary>
    /// Draw changed cells. Continuation cells are never included: a wide cell covers the cell to its right.
    /// </summary>
    /// <param name="updates">The changed cells.</param>
    /// <param name="cursor">Where to show the cursor, or null to hide it.</param>
    /// <param name="fullRedraw">Clear the screen first; <paramref name="updates"/> then contains every cell.</param>
    void Draw(IReadOnlyList<CellUpdate> updates, (int X, int Y)? cursor, bool fullRedraw);
}

/// <summary>
/// A cell to draw at a position.
/// </summary>
/// <param name="X">Column.</param>
/// <param name="Y">Row.</param>
/// <param name="Cell">The cell.</param>
public readonly record struct CellUpdate(int X, int Y, Cell Cell);

/// <summary>
/// Thrown when MiniTui can't run, for example because stdin or stdout is redirected.
/// </summary>
public class TerminalNotSupportedException : Exception
{
    /// <summary>Create the exception with a default message.</summary>
    public TerminalNotSupportedException() : base("The terminal is not supported.") { }

    /// <summary>Create the exception with a message.</summary>
    public TerminalNotSupportedException(string message) : base(message) { }

    /// <summary>Create the exception with a message and inner exception.</summary>
    public TerminalNotSupportedException(string message, Exception innerException) : base(message, innerException) { }
}
