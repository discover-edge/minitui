namespace MiniTui;

/// <summary>
/// The selected row and scroll offset of a list or table (§8.2). The widget clamps <see cref="Selected"/>
/// and adjusts <see cref="Offset"/> while drawing, so the selection is always visible.
/// </summary>
public sealed class SelectionState
{
    private int _selected;
    private bool _reveal = true;

    // 1.x spec §24.3: the selected item's key at the last draw, one asked for, and whether the selection moved since
    private object? _key;
    private object? _wantedKey;
    private bool _moved;

    /// <summary>
    /// The selected index. Clamped to the item count when drawn. Setting it (or moving it with a key) scrolls it
    /// into view at the next draw.
    /// </summary>
    public int Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            _reveal = true;
            _moved = true;
        }
    }

    /// <summary>
    /// The selected item's key at the last draw of a list or table given a key (<c>Items(items, format, key)</c>,
    /// <c>Rows(items, format, key)</c>); null before, or without one (1.x spec §24.3). While the selection isn't
    /// moved, it follows the item with this key when the items change.
    /// </summary>
    public object? SelectedKey => _key;

    /// <summary>Select the item with <paramref name="key"/> at the next draw with a key, such as a row just added; ignored if none has it.</summary>
    public void SelectKey(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _wantedKey = key;
        _moved = true;
    }

    /// <summary>
    /// The first visible index. Adjusted when drawn so <see cref="Selected"/> is visible after the selection moves;
    /// the wheel and the scrollbar scroll without moving the selection (1.x spec §14.4).
    /// </summary>
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
        var handled = HandleKeyCore(key, count);
        if (handled) _reveal = true;
        return handled;
    }

    private bool HandleKeyCore(ConsoleKeyInfo key, int count)
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

    /// <summary>The column the data is sorted by, ascending if it wasn't already, else the other way (1.x spec §14.5). Raises <see cref="SortChanged"/>.</summary>
    public void ToggleSort(int column)
    {
        SortDescending = SortColumn == column && !SortDescending;
        SortColumn = column;
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised on the UI thread when a header click or <see cref="ToggleSort"/> sorts by a column, for the app to sort its items.</summary>
    public event EventHandler? SortChanged;

    /// <summary>
    /// Raised on the UI thread when a row is double-clicked (1.x spec §17.4), after the first press selected it, so
    /// the row is <see cref="Selected"/>.
    /// </summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Handle the mouse over the list or table drawn last (1.x spec §4.8, §14.4, §14.5): the wheel scrolls by one
    /// item, a left press on a visible row selects it, the scrollbar's thumb drags and its track pages, and a press
    /// on a sortable header cell calls <see cref="ToggleSort"/>. Scrolling doesn't move the selection. A double-click
    /// on a row raises <see cref="Activated"/>.
    /// </summary>
    /// <returns>True if the event did any of these.</returns>
    public bool HandleMouse(MouseEvent mouse, int count)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        var offset = Offset;
        if (_bar.Handle(mouse, ref offset))
        {
            Offset = offset;
            return true;
        }
        if (!_mouseArea.Contains(mouse.X, mouse.Y)) return false;
        if (mouse.IsClick && _headers.FindIndex(h => h.Rect.Contains(mouse.X, mouse.Y)) is var header and >= 0)
        {
            ToggleSort(_headers[header].Column);
            return true;
        }
        switch (mouse.Action)
        {
            case MouseAction.ScrollUp: ScrollBy(-1, count); return true;
            case MouseAction.ScrollDown: ScrollBy(1, count); return true;
        }

        if (!mouse.IsClick || !_mouseRows.Contains(mouse.X, mouse.Y)) return false;
        var index = Offset + (mouse.Y - _mouseRows.Y);
        if (index >= count) return false;
        Selected = index;
        if (mouse.ClickCount == 2) Activated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Called by widgets while drawing: the widget's area, and the rows its items are drawn in.</summary>
    internal void Track(Rect area, Rect rows)
    {
        _mouseArea = area;
        _mouseRows = rows;
        _bar.Track(default, 0, 0);
        _headers.Clear();
    }

    /// <summary>Called by widgets while drawing, after <see cref="Track(Rect, Rect)"/>: where the scrollbar was drawn, for <paramref name="count"/> items.</summary>
    internal void TrackScrollbar(Rect bar, int count) => _bar.Track(bar, count, ViewportHeight);

    /// <summary>Called by the table while drawing, after <see cref="Track(Rect, Rect)"/>: a sortable header cell.</summary>
    internal void TrackHeader(int column, Rect cell) => _headers.Add((column, cell));

    /// <summary>
    /// Called by widgets while drawing: clamp the selection to <paramref name="count"/> and scroll so it's
    /// within a viewport of <paramref name="viewportHeight"/> rows.
    /// </summary>
    /// <param name="count">How many items there are.</param>
    /// <param name="viewportHeight">How many rows show.</param>
    /// <param name="keyAt">With a key (1.x spec §24.3), the key of the item at an index.</param>
    internal void Fit(int count, int viewportHeight, Func<int, object>? keyAt = null)
    {
        if (keyAt is not null) Follow(count, keyAt);
        ViewportHeight = Math.Max(0, viewportHeight);
        _selected = Clamp(_selected, count);
        Offset = _reveal ? FitOffset(Offset, _selected, count, ViewportHeight) : ClampOffset(Offset, count, ViewportHeight);
        _reveal = false;
        _key = keyAt is not null && count > 0 ? keyAt(_selected) : null;
    }

    // A key asked for is selected; otherwise, unless the selection moved, it follows its item to where it is now,
    // moving the offset as far, so the item stays on its screen row. An item that's gone leaves the index.
    private void Follow(int count, Func<int, object> keyAt)
    {
        if (_wantedKey is { } wanted)
        {
            _wantedKey = null;
            if (IndexOf(wanted, count, keyAt) is var found and >= 0) Selected = found;
        }
        else if (!_moved && _key is { } had && count > 0 && !Equals(keyAt(Clamp(_selected, count)), had)
            && IndexOf(had, count, keyAt) is var at and >= 0)
        {
            Offset += at - _selected;
            _selected = at;
        }
        _moved = false;
    }

    private static int IndexOf(object key, int count, Func<int, object> keyAt)
    {
        for (var i = 0; i < count; i++)
            if (Equals(keyAt(i), key)) return i;
        return -1;
    }

    /// <summary>An offset kept within the content, leaving the selection wherever it is.</summary>
    internal static int ClampOffset(int offset, int count, int viewportHeight) =>
        Math.Clamp(offset, 0, Math.Max(0, viewportHeight > 0 ? count - viewportHeight : count - 1));

    private void ScrollBy(int rows, int count) => Offset = ClampOffset(Offset + rows, count, ViewportHeight);

    /// <summary>The offset that keeps <paramref name="selected"/> visible, moving as little as possible.</summary>
    internal static int FitOffset(int offset, int selected, int count, int viewportHeight)
    {
        if (viewportHeight <= 0) return Math.Clamp(offset, 0, Math.Max(0, count - 1));
        if (selected < offset) offset = selected;
        else if (selected >= offset + viewportHeight) offset = selected - viewportHeight + 1;
        return Math.Clamp(offset, 0, Math.Max(0, count - viewportHeight));
    }

    private int Page => Math.Max(1, ViewportHeight);

    private Rect _mouseArea;
    private Rect _mouseRows;
    private readonly ScrollbarDrag _bar = new();
    private readonly List<(int Column, Rect Rect)> _headers = [];

    private static int Clamp(int index, int count) => count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);
}
