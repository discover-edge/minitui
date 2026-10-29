namespace MiniTui;

/// <summary>
/// How many colors the terminal can display. Colors are downsampled to this mode when encoded.
/// </summary>
public enum ColorMode
{
    /// <summary>No color codes are emitted. Modifiers (bold, reversed, ...) still apply.</summary>
    None,
    /// <summary>The 16 standard ANSI colors.</summary>
    Ansi16,
    /// <summary>The xterm 256-color palette.</summary>
    Ansi256,
    /// <summary>24-bit RGB color.</summary>
    TrueColor
}
