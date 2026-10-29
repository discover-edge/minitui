namespace MiniTui;

/// <summary>
/// Multi-line styled text.
/// </summary>
/// <param name="Lines">The lines, top to bottom.</param>
public sealed record Text(IReadOnlyList<Line> Lines)
{
    /// <summary>Split a string into unstyled lines on <c>\n</c> and <c>\r\n</c>.</summary>
    public static Text From(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var parts = s.Split('\n');
        var lines = new Line[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            lines[i] = new Line(parts[i].EndsWith('\r') ? parts[i][..^1] : parts[i]);
        return new Text(lines);
    }

    /// <summary>The widest line's width in columns.</summary>
    public int Width
    {
        get
        {
            var width = 0;
            foreach (var line in Lines) width = Math.Max(width, line.Width);
            return width;
        }
    }

    /// <summary>The number of lines.</summary>
    public int Height => Lines.Count;

    /// <summary>Split a string into unstyled lines.</summary>
    public static implicit operator Text(string s) => From(s);

    /// <summary>Text with a single line.</summary>
    public static implicit operator Text(Line line) => new([line]);
}
