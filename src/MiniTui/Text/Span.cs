namespace MiniTui;

/// <summary>
/// A run of text with a single style.
/// </summary>
/// <param name="Content">The text. It is sanitized when drawn.</param>
/// <param name="Style">The style patch applied to each cell.</param>
public readonly record struct Span(string Content, Style Style)
{
    /// <summary>Display width in columns, as drawn on a single line.</summary>
    public int Width => UnicodeWidth.Of(Sanitizer.SingleLine(Content ?? ""));

    /// <summary>An unstyled span.</summary>
    public static implicit operator Span(string content) => new(content, Style.Default);
}
