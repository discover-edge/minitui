namespace MiniTui;

/// <summary>
/// The scroll position of scrollable text, such as a <see cref="ParagraphBuilder"/> or a modal.
/// The widget writes <see cref="ViewportHeight"/> and <see cref="ContentHeight"/> and clamps <see cref="Offset"/> while drawing.
/// </summary>
public sealed class ScrollState
{
    /// <summary>The first visible row. Clamped when drawn.</summary>
    public int Offset { get; set; }

    /// <summary>How many rows were visible at the last draw.</summary>
    public int ViewportHeight { get; private set; }

    /// <summary>How many rows the content had at the last draw.</summary>
    public int ContentHeight { get; private set; }

    /// <summary>Scroll by <paramref name="rows"/> (negative scrolls up).</summary>
    public void ScrollBy(int rows) => Offset = Clamp(Offset + rows);

    /// <summary>
    /// Handle ↑ ↓ j k PgUp PgDn Home End.
    /// </summary>
    /// <returns>True if the key was a scroll key.</returns>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        var page = Math.Max(1, ViewportHeight);
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: ScrollBy(-1); return true;
            case ConsoleKey.DownArrow: ScrollBy(1); return true;
            case ConsoleKey.PageUp: ScrollBy(-page); return true;
            case ConsoleKey.PageDown: ScrollBy(page); return true;
            case ConsoleKey.Home: Offset = 0; return true;
            case ConsoleKey.End: Offset = MaxOffset; return true;
        }

        if (KeyInput.HasCommandModifier(key)) return false;
        switch (key.KeyChar)
        {
            case 'k': ScrollBy(-1); return true;
            case 'j': ScrollBy(1); return true;
            default: return false;
        }
    }

    /// <summary>Called by widgets while drawing.</summary>
    internal void Fit(int contentHeight, int viewportHeight)
    {
        ContentHeight = Math.Max(0, contentHeight);
        ViewportHeight = Math.Max(0, viewportHeight);
        Offset = Clamp(Offset);
    }

    private int MaxOffset => Math.Max(0, ContentHeight - ViewportHeight);

    // Before the first draw the content size is unknown, so only negative offsets are clamped
    private int Clamp(int offset) => ContentHeight == 0 && ViewportHeight == 0
        ? Math.Max(0, offset)
        : Math.Clamp(offset, 0, MaxOffset);
}
