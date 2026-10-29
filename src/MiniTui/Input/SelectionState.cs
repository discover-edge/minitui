namespace MiniTui;

/// <summary>
/// The selected row and scroll offset of a list or table (§8.2). The widget clamps <see cref="Selected"/>
/// and adjusts <see cref="Offset"/> while drawing, so the selection is always visible.
/// </summary>
public sealed class SelectionState
{
    /// <summary>The selected index. Clamped to the item count when drawn.</summary>
    public int Selected { get; set; }

    /// <summary>The first visible index. Adjusted when drawn so <see cref="Selected"/> is visible.</summary>
    public int Offset { get; set; }

    /// <summary>How many rows were visible at the last draw. Used by <see cref="PageDown"/> and <see cref="PageUp"/>.</summary>
    public int ViewportHeight { get; private set; }

    /// <summary>The column the data is sorted by, for the ▲/▼ header marker. Display only.</summary>
    public int? SortColumn { get; set; }

    /// <summary>Whether <see cref="SortColumn"/> is sorted descending (▼). Display only.</summary>
    public bool SortDescending { get; set; }

    /// <summary>Select the next item, stopping at the last.</summary>
    public void Next(int count) => Selected = Clamp(Selected + 1, count);

    /// <summary>Select the previous item, stopping at the first.</summary>
    public void Previous(int count) => Selected = Clamp(Selected - 1, count);

    /// <summary>Move the selection down by one viewport.</summary>
    public void PageDown(int count) => Selected = Clamp(Selected + Page, count);

    /// <summary>Move the selection up by one viewport.</summary>
    public void PageUp(int count) => Selected = Clamp(Selected - Page, count);

    /// <summary>Select the first item.</summary>
    public void First() => Selected = 0;

    /// <summary>Select the last item.</summary>
    public void Last(int count) => Selected = Math.Max(0, count - 1);

    /// <summary>
    /// Handle ↑ ↓ j k PgUp PgDn Home End.
    /// </summary>
    /// <returns>True if the key was a navigation key.</returns>
    public bool HandleKey(ConsoleKeyInfo key, int count)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Previous(count); return true;
            case ConsoleKey.DownArrow: Next(count); return true;
            case ConsoleKey.PageUp: PageUp(count); return true;
            case ConsoleKey.PageDown: PageDown(count); return true;
            case ConsoleKey.Home: First(); return true;
            case ConsoleKey.End: Last(count); return true;
        }

        if (KeyInput.HasCommandModifier(key)) return false;
        switch (key.KeyChar)
        {
            case 'k': Previous(count); return true;
            case 'j': Next(count); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Called by widgets while drawing: clamp the selection to <paramref name="count"/> and scroll so it's
    /// within a viewport of <paramref name="viewportHeight"/> rows.
    /// </summary>
    internal void Fit(int count, int viewportHeight)
    {
        ViewportHeight = Math.Max(0, viewportHeight);
        Selected = Clamp(Selected, count);
        Offset = FitOffset(Offset, Selected, count, ViewportHeight);
    }

    /// <summary>The offset that keeps <paramref name="selected"/> visible, moving as little as possible.</summary>
    internal static int FitOffset(int offset, int selected, int count, int viewportHeight)
    {
        if (viewportHeight <= 0) return Math.Clamp(offset, 0, Math.Max(0, count - 1));
        if (selected < offset) offset = selected;
        else if (selected >= offset + viewportHeight) offset = selected - viewportHeight + 1;
        return Math.Clamp(offset, 0, Math.Max(0, count - viewportHeight));
    }

    private int Page => Math.Max(1, ViewportHeight);

    private static int Clamp(int index, int count) => count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);
}
