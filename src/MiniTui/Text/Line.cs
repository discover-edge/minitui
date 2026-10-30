namespace MiniTui;

/// <summary>
/// Horizontal alignment of a line within its area.
/// </summary>
public enum Alignment
{
    /// <summary>Aligned to the left edge.</summary>
    Left,
    /// <summary>Centered.</summary>
    Center,
    /// <summary>Aligned to the right edge.</summary>
    Right
}

/// <summary>
/// A single line of styled spans.
/// </summary>
/// <param name="Spans">The spans, drawn left to right.</param>
/// <param name="Alignment">Alignment used by widgets that lay out lines, such as Paragraph.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters",
    Justification = "Spans or a string: the first parameter's type picks the constructor, so calls are never ambiguous.")]
public sealed record Line(IReadOnlyList<Span> Spans, Alignment Alignment = Alignment.Left)
{
    /// <summary>A line with a single span.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters",
        Justification = "Spans or a string: the first parameter's type picks the constructor, so calls are never ambiguous.")]
    public Line(string content, Style style = default) : this([new Span(content, style)]) { }

    /// <summary>Display width in columns.</summary>
    public int Width
    {
        get
        {
            var width = 0;
            foreach (var span in Spans) width += span.Width;
            return width;
        }
    }

    /// <summary>An unstyled line.</summary>
    public static implicit operator Line(string s) => new(s);
}
