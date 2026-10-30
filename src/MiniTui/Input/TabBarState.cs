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

    /// <summary>Handle a left press on a tab drawn last (1.x spec §4.8): select it.</summary>
    /// <returns>True if the press hit a tab.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!mouse.IsClick || mouse.Y != _tabsY) return false;
        foreach (var (index, x, width) in _tabs)
        {
            if (mouse.X < x || mouse.X >= x + width) continue;
            Selected = Clamp(index);
            return true;
        }
        return false;
    }

    /// <summary>Called by widgets while drawing: the row the tabs are on. Clears the labels from the last draw.</summary>
    internal void TrackRow(int y)
    {
        _tabsY = y;
        _tabs.Clear();
    }

    /// <summary>Called by widgets while drawing: where a tab's label was drawn.</summary>
    internal void TrackTab(int index, int x, int width) => _tabs.Add((index, x, width));

    /// <summary>Called by widgets while drawing.</summary>
    internal void Fit(int count)
    {
        Count = Math.Max(0, count);
        Selected = Clamp(Selected);
    }

    private readonly List<(int Index, int X, int Width)> _tabs = [];
    private int _tabsY = -1;

    // Before the first draw, Count may be unknown (0): only negative indices are clamped
    private int Clamp(int index) => Count == 0 ? Math.Max(0, index) : Math.Clamp(index, 0, Count - 1);
}
