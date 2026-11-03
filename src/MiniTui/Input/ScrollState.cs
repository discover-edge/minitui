using System.Globalization;
using System.Text;

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

    /// <summary>
    /// Let the mouse select the text (1.x spec §20.5): a press and drag select, a double-click a word, a triple-click
    /// a row. Ctrl+C and Ctrl+Insert copy, Ctrl+A selects all and Esc clears. Off by default.
    /// </summary>
    public bool Selectable { get; set; }

    /// <summary>True while something is selected.</summary>
    public bool HasSelection => _anchor != _head;

    /// <summary>The selected part of each row as it was drawn last, with a line break between rows.</summary>
    public string SelectedText
    {
        get
        {
            if (!HasSelection) return "";
            var (start, end) = Ordered;
            var text = new StringBuilder();
            for (var row = start.Row; row <= end.Row && row < _rows.Count; row++)
            {
                if (row > start.Row) text.Append('\n');
                var from = row == start.Row ? start.Column : 0;
                var to = row == end.Row ? end.Column : int.MaxValue;
                foreach (var (cluster, column, width) in Clusters(_rows[row].Text))
                    if (column < to && column + width > from) text.Append(cluster);
            }
            return text.ToString();
        }
    }

    /// <summary>Select every row drawn last.</summary>
    public void SelectAll()
    {
        if (_rows.Count == 0) return;
        _anchor = new Mark(0, 0);
        _head = new Mark(_rows.Count - 1, _rows[^1].Width);
    }

    /// <summary>Clear the selection.</summary>
    public void ClearSelection() => _anchor = _head = default;

    /// <summary>Copy the selection to the <see cref="Clipboard"/>.</summary>
    /// <returns>False, doing nothing, without a selection.</returns>
    public bool Copy()
    {
        if (!HasSelection) return false;
        Clipboard.SetText(SelectedText);
        return true;
    }

    /// <summary>Scroll by <paramref name="rows"/> (negative scrolls up).</summary>
    public void ScrollBy(int rows) => Offset = Clamp(Offset + rows);

    /// <summary>
    /// Handle ↑ ↓ j k PgUp PgDn Home End; when <see cref="Selectable"/>, also Ctrl+C and Ctrl+Insert (copy, with a
    /// selection), Ctrl+A (select all) and Esc (clear a selection).
    /// </summary>
    /// <returns>True if the key was handled.</returns>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (Selectable && HandleSelectionKey(key, out var handled)) return handled;
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

    /// <summary>
    /// Handle the mouse over the text drawn last (1.x spec §4.8, §14.4): the wheel scrolls by three rows, the
    /// scrollbar's thumb drags and its track pages. When <see cref="Selectable"/>, the left button selects (§20.5).
    /// </summary>
    /// <returns>True if it scrolled, tried to, or selected.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        var offset = Offset;
        if (_bar.Handle(mouse, ref offset))
        {
            Offset = Clamp(offset);
            return true;
        }
        if (Selectable && HandleSelectionMouse(mouse)) return true;
        if (!_mouseArea.Contains(mouse.X, mouse.Y)) return false;
        switch (mouse.Action)
        {
            case MouseAction.ScrollUp: ScrollBy(-WheelRows); return true;
            case MouseAction.ScrollDown: ScrollBy(WheelRows); return true;
            default: return false;
        }
    }

    /// <summary>Called by widgets while drawing: the area the text was drawn in.</summary>
    internal void Track(Rect area)
    {
        _mouseArea = area;
        _bar.Track(default, 0, 0);
    }

    /// <summary>
    /// Called by widgets while drawing, after <see cref="Fit"/>: the rows laid out and where they were drawn, for
    /// selecting. A selection on rows that are gone is cleared.
    /// </summary>
    internal void TrackRows(Rect text, IReadOnlyList<Line> rows, Alignment alignment)
    {
        _textArea = text;
        _rows.Clear();
        if (!Selectable) return;
        foreach (var line in rows)
        {
            var content = new StringBuilder();
            foreach (var span in line.Spans) content.Append(Sanitizer.SingleLine(span.Content ?? ""));
            var row = content.ToString();
            var width = UnicodeWidth.Of(row);
            var shown = Math.Min(width, text.Width);
            var start = (line.Alignment != Alignment.Left ? line.Alignment : alignment) switch
            {
                Alignment.Center => (text.Width - shown) / 2,
                Alignment.Right => text.Width - shown,
                _ => 0,
            };
            _rows.Add(new Row(row, width, start));
        }
        if (HasSelection && Math.Max(_anchor.Row, _head.Row) >= _rows.Count) ClearSelection();
    }

    /// <summary>
    /// Called by widgets while drawing: the selected columns of row <paramref name="row"/>, from the left of the text
    /// area. Empty without a selection on it.
    /// </summary>
    internal (int From, int To) SelectedColumns(int row)
    {
        if (!HasSelection || row >= _rows.Count) return default;
        var (start, end) = Ordered;
        if (row < start.Row || row > end.Row) return default;
        var from = row == start.Row ? start.Column : 0;
        var to = row == end.Row ? end.Column : int.MaxValue;
        int x0 = int.MaxValue, x1 = 0;
        foreach (var (_, column, width) in Clusters(_rows[row].Text))
        {
            if (column >= to) break;
            if (column + width <= from) continue;
            x0 = Math.Min(x0, column);
            x1 = column + width;
        }
        return x1 > x0 ? (x0 + _rows[row].Start, x1 + _rows[row].Start) : default;
    }

    /// <summary>Called by widgets while drawing, after <see cref="Fit"/>: where the scrollbar was drawn.</summary>
    internal void TrackScrollbar(Rect bar) => _bar.Track(bar, ContentHeight, ViewportHeight);

    /// <summary>Called by widgets while drawing.</summary>
    internal void Fit(int contentHeight, int viewportHeight)
    {
        ContentHeight = Math.Max(0, contentHeight);
        ViewportHeight = Math.Max(0, viewportHeight);
        Offset = Clamp(Offset);
    }

    private const int WheelRows = 3;

    private Rect _mouseArea;
    private Rect _textArea;
    private readonly ScrollbarDrag _bar = new();
    private readonly List<Row> _rows = [];
    private Mark _anchor;
    private Mark _head;
    private bool _selecting;

    // A row as drawn: its text, its width, and the column it starts at (by its alignment)
    private readonly record struct Row(string Text, int Width, int Start);

    // A row and a column of that row's text: the boundary before that cell
    private readonly record struct Mark(int Row, int Column) : IComparable<Mark>
    {
        public int CompareTo(Mark other) => Row != other.Row ? Row.CompareTo(other.Row) : Column.CompareTo(other.Column);
    }

    private (Mark Start, Mark End) Ordered => _anchor.CompareTo(_head) <= 0 ? (_anchor, _head) : (_head, _anchor);

    private bool HandleSelectionKey(ConsoleKeyInfo key, out bool handled)
    {
        var ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        if (KeyInput.IsCtrlC(key) || (key.Key == ConsoleKey.Insert && ctrl && !shift)) handled = Copy();
        else if (KeyInput.IsCtrl(key, 'a')) { SelectAll(); handled = true; }
        else if (key.Key == ConsoleKey.Escape && HasSelection) { ClearSelection(); handled = true; }
        else return handled = false;
        return true;
    }

    private bool HandleSelectionMouse(MouseEvent mouse)
    {
        var area = _textArea;
        switch (mouse.Action)
        {
            case MouseAction.Press when mouse.Button == MouseButton.Left:
                _selecting = area.Contains(mouse.X, mouse.Y) && _rows.Count > 0;
                if (!_selecting) return false;
                var at = MarkAt(Offset + mouse.Y - area.Y, mouse.X - area.X);
                var row = _rows[at.Row];
                // 1.x spec §17.4's word rule: a double-click selects a word, a triple-click the row
                if (mouse.ClickCount == 2 && row.Text.Length > 0)
                {
                    var (start, end) = TextFieldState.WordAt(row.Text, IndexAt(row.Text, at.Column));
                    _anchor = at with { Column = ColumnOf(row.Text, start) };
                    _head = at with { Column = ColumnOf(row.Text, end) };
                }
                else if (mouse.ClickCount >= 3)
                {
                    _anchor = at with { Column = 0 };
                    _head = at with { Column = row.Width };
                }
                else _anchor = _head = at;
                return true;
            case MouseAction.Drag when mouse.Button == MouseButton.Left && _selecting:
                // Past the top or bottom, scroll a row and select to the first or last visible row
                int visible;
                if (mouse.Y < area.Y) visible = Offset = Clamp(Offset - 1);
                else if (mouse.Y >= area.Bottom) visible = (Offset = Clamp(Offset + 1)) + area.Height - 1;
                else visible = Offset + mouse.Y - area.Y;
                _head = MarkAt(visible, mouse.X - area.X);
                return true;
            case MouseAction.Release when _selecting:
                _selecting = false;
                return false;
            default:
                return false;
        }
    }

    // The boundary nearest a visible row and a column from the text area's left; below the last row, its end
    private Mark MarkAt(int row, int x)
    {
        if (row >= _rows.Count) return new Mark(_rows.Count - 1, _rows[^1].Width);
        row = Math.Max(0, row);
        var text = _rows[row];
        var column = Math.Clamp(x - text.Start, 0, text.Width);
        // Inside a wide character, the boundary before it
        var at = 0;
        foreach (var (_, start, width) in Clusters(text.Text))
        {
            if (start + width > column) break;
            at = start + width;
        }
        return new Mark(row, at);
    }

    // The string index of the cluster at a column, or the end
    private static int IndexAt(string text, int column)
    {
        var at = 0;
        for (var i = 0; i < text.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(text, i);
            at += UnicodeWidth.OfGrapheme(text.AsSpan(i, length));
            if (at > column) return i;
            i += length;
        }
        return text.Length;
    }

    private static int ColumnOf(string text, int index) => UnicodeWidth.Of(text.AsSpan(0, index));

    // The visible clusters of a row, each with its column and width
    private static IEnumerable<(string Cluster, int Column, int Width)> Clusters(string text)
    {
        var column = 0;
        for (var i = 0; i < text.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(text, i);
            var cluster = text.Substring(i, length);
            i += length;
            var width = UnicodeWidth.OfGrapheme(cluster);
            if (width == 0) continue;
            yield return (cluster, column, width);
            column += width;
        }
    }

    private int MaxOffset => Math.Max(0, ContentHeight - ViewportHeight);

    // Before the first draw the content size is unknown, so only negative offsets are clamped
    private int Clamp(int offset) => ContentHeight == 0 && ViewportHeight == 0
        ? Math.Max(0, offset)
        : Math.Clamp(offset, 0, MaxOffset);
}
