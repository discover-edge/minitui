namespace MiniTui;

/// <summary>
/// Text attributes that can be added to or removed from a cell.
/// </summary>
[Flags]
public enum Modifiers : ushort
{
    /// <summary>No modifiers.</summary>
    None = 0,
    /// <summary>Bold or increased intensity.</summary>
    Bold = 1,
    /// <summary>Dim or decreased intensity.</summary>
    Dim = 2,
    /// <summary>Italic.</summary>
    Italic = 4,
    /// <summary>Underlined.</summary>
    Underlined = 8,
    /// <summary>Foreground and background swapped.</summary>
    Reversed = 16,
    /// <summary>Strikethrough.</summary>
    CrossedOut = 32
}

/// <summary>
/// A patch of cell attributes. Drawing with a style changes only what the style specifies:
/// a null <see cref="Fg"/> or <see cref="Bg"/> keeps the cell's existing color.
/// </summary>
/// <param name="Fg">Foreground color, or null to keep the existing one.</param>
/// <param name="Bg">Background color, or null to keep the existing one.</param>
/// <param name="Add">Modifiers to turn on.</param>
/// <param name="Remove">Modifiers to turn off.</param>
public readonly record struct Style(
    Color? Fg = null,
    Color? Bg = null,
    Modifiers Add = Modifiers.None,
    Modifiers Remove = Modifiers.None)
{
    /// <summary>A style that changes nothing.</summary>
    public static Style Default => default;

    /// <summary>
    /// Combine two styles. <paramref name="other"/>'s non-null colors win, and its modifiers
    /// are applied on top of this style's.
    /// </summary>
    public Style Patch(Style other) => new(
        other.Fg ?? Fg,
        other.Bg ?? Bg,
        (Add & ~other.Remove) | other.Add,
        (Remove & ~other.Add) | other.Remove);

    /// <summary>Returns this style with the given foreground color.</summary>
    public Style WithFg(Color c) => this with { Fg = c };

    /// <summary>Returns this style with the given background color.</summary>
    public Style WithBg(Color c) => this with { Bg = c };

    /// <summary>Returns this style with <see cref="Modifiers.Bold"/> added.</summary>
    public Style Bold() => With(Modifiers.Bold);

    /// <summary>Returns this style with <see cref="Modifiers.Dim"/> added.</summary>
    public Style Dim() => With(Modifiers.Dim);

    /// <summary>Returns this style with <see cref="Modifiers.Italic"/> added.</summary>
    public Style Italic() => With(Modifiers.Italic);

    /// <summary>Returns this style with <see cref="Modifiers.Underlined"/> added.</summary>
    public Style Underlined() => With(Modifiers.Underlined);

    /// <summary>Returns this style with <see cref="Modifiers.Reversed"/> added.</summary>
    public Style Reversed() => With(Modifiers.Reversed);

    /// <summary>Returns this style with <see cref="Modifiers.CrossedOut"/> added.</summary>
    public Style CrossedOut() => With(Modifiers.CrossedOut);

    /// <summary>Apply this style to a cell's attributes, keeping its symbol.</summary>
    internal Cell ApplyTo(Cell cell) => cell with
    {
        Fg = Fg ?? cell.Fg,
        Bg = Bg ?? cell.Bg,
        Modifiers = (cell.Modifiers | Add) & ~Remove
    };

    private Style With(Modifiers m) => this with { Add = Add | m, Remove = Remove & ~m };
}
