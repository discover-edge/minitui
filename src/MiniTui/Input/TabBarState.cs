namespace MiniTui;

/// <summary>
/// The selected tab of a <see cref="TabBarBuilder"/>. The widget writes <see cref="Count"/> while drawing.
/// </summary>
public sealed class TabBarState
{
    /// <summary>Create a state, optionally knowing the tab count before the first draw.</summary>
    public TabBarState(int count = 0) => Count = Math.Max(0, count);

    /// <summary>The selected tab index. Clamped when drawn.</summary>
    public int Selected { get; set; }

    /// <summary>How many tabs there were at the last draw.</summary>
    public int Count { get; internal set; }

    /// <summary>Select the next tab, stopping at the last.</summary>
    public void Next() => Selected = Clamp(Selected + 1);

    /// <summary>Select the previous tab, stopping at the first.</summary>
    public void Previous() => Selected = Clamp(Selected - 1);

    /// <summary>
    /// Handle ← → h l.
    /// </summary>
    /// <returns>True if the key changed or tried to change the tab.</returns>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow: Previous(); return true;
            case ConsoleKey.RightArrow: Next(); return true;
        }

        if (KeyInput.HasCommandModifier(key)) return false;
        switch (key.KeyChar)
        {
            case 'h': Previous(); return true;
            case 'l': Next(); return true;
            default: return false;
        }
    }

    /// <summary>Called by widgets while drawing.</summary>
    internal void Fit(int count)
    {
        Count = Math.Max(0, count);
        Selected = Clamp(Selected);
    }

    // Before the first draw, Count may be unknown (0): only negative indices are clamped
    private int Clamp(int index) => Count == 0 ? Math.Max(0, index) : Math.Clamp(index, 0, Count - 1);
}
