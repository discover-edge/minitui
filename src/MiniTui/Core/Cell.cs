using System.Diagnostics.CodeAnalysis;

namespace MiniTui;

/// <summary>
/// A single terminal cell: one grapheme cluster plus its colors and modifiers.
/// A wide (two-column) cell is followed by a continuation cell whose <see cref="Symbol"/> is empty.
/// </summary>
/// <param name="Symbol">Exactly one grapheme cluster, or <c>""</c> for a continuation cell.</param>
/// <param name="Fg">Foreground color.</param>
/// <param name="Bg">Background color.</param>
/// <param name="Modifiers">Text modifiers.</param>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Cell.Char is a 0.7 compatibility shim.")]
public readonly record struct Cell(string Symbol, Color Fg, Color Bg, Modifiers Modifiers)
{
    /// <summary>Exactly one grapheme cluster, or <c>""</c> for a continuation cell.</summary>
    public string Symbol { get => field ?? " "; init; } = Symbol;

    /// <summary>A space with the terminal's default colors and no modifiers.</summary>
    public static Cell Empty { get; } = new(" ", Color.Reset, Color.Reset, Modifiers.None);

    /// <summary>Display width in columns: 0 for a continuation cell, otherwise 1 or 2.</summary>
    public int Width => Symbol switch
    {
        "" => 0,
        { Length: 1 } s when s[0] < 0x1100 => 1,
        var s => Math.Clamp(UnicodeWidth.OfGrapheme(s), 1, 2)
    };

    /// <summary>True for the cell that follows a wide character.</summary>
    public bool IsContinuation => Symbol.Length == 0;
}
