using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace MiniTui;

/// <summary>A position in a <see cref="TextAreaState"/>: a line, and a UTF-16 index into it on a grapheme cluster boundary.</summary>
public readonly record struct TextPosition(int Line, int Index);

/// <summary>
/// The text and cursor of a multi-line text area (1.x spec §6). The text is a list of lines; the cursor is a line
/// and a UTF-16 index that is always on a grapheme cluster boundary, so editing never splits a character.
/// </summary>
public class TextAreaState
{
    private const int TabSize = 4;
    private const int WheelRows = 3;

    private readonly List<string> _lines = [""];
    private readonly ReadOnlyCollection<string> _readOnlyLines;
    private int _line;
    private int _index;
    private int? _goalColumn;
    private char? _pendingHighSurrogate;

    /// <summary>Create a state with optional initial text. The cursor starts at the end.</summary>
    public TextAreaState(string text = "")
    {
        _readOnlyLines = _lines.AsReadOnly();
        Text = text;
    }

    /// <summary>
    /// The text, lines joined by <c>"\n"</c>. Setting it normalizes <c>"\r\n"</c> and <c>"\r"</c> to <c>"\n"</c>,
    /// turns tabs into spaces up to the next multiple of 4 columns, removes other control characters, and moves the
    /// cursor to the end.
    /// </summary>
    public string Text
    {
        get => string.Join('\n', _lines);
        set
        {
            _lines.Clear();
            _lines.Add("");
            _line = 0;
            _index = 0;
            _pendingHighSurrogate = null;
            InsertNormalized(value ?? "");
            Moved();
        }
    }

    /// <summary>The lines, without line breaks. Never empty: empty text is one empty line.</summary>
    public IReadOnlyList<string> Lines => _readOnlyLines;

    /// <summary>The cursor. Set values are clamped to the text and snap to the next cluster boundary.</summary>
    public TextPosition Cursor
    {
        get => new(_line, _index);
        set
        {
            _line = Math.Clamp(value.Line, 0, _lines.Count - 1);
            var line = _lines[_line];
            _index = TextFieldState.SnapToBoundary(line, Math.Clamp(value.Index, 0, line.Length));
            Moved();
        }
    }

    /// <summary>The first visible row (a wrapped row, not a line). Written by the widget while drawing, and by the wheel.</summary>
    public int ScrollOffset { get; internal set; }

    // What the widget last drew, for the keys and the mouse that depend on the layout
    internal int ViewWidth { get; private set; } = int.MaxValue;
    internal int ViewHeight { get; private set; } = 1;
    internal WrapMode ViewWrap { get; private set; } = WrapMode.None;
    internal Rect ViewArea { get; private set; }
    internal int ViewRowCount { get; private set; }

    /// <summary>The first visible display column with <see cref="WrapMode.None"/>.</summary>
    internal int HorizontalOffset { get; set; }

    /// <summary>True while the view should scroll to the cursor; the wheel turns it off until the cursor moves.</summary>
    internal bool FollowCursor { get; private set; } = true;

    private TextPosition End => new(_lines.Count - 1, _lines[^1].Length);

    /// <summary>Clear the text.</summary>
    public void Clear() => Text = "";

    /// <summary>Insert text at the cursor and move the cursor past it. Line breaks in the text start new lines.</summary>
    public void Insert(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        InsertNormalized(text);
        Moved();
    }

    /// <summary>
    /// Insert pasted text at the cursor, keeping its line breaks. Tabs and control characters are handled as for
    /// <see cref="Text"/>.
    /// </summary>
    /// <returns>Always true, so a component can write <c>HandlePaste(text) =&gt; State.Paste(text)</c>.</returns>
    public bool Paste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Insert(text);
        return true;
    }

    /// <summary>
    /// Handle an editing or movement key (1.x spec §6.3): text, Enter, ← → ↑ ↓, PgUp PgDn, Home End, Ctrl+Home End,
    /// Ctrl+← →, Backspace, Delete, Ctrl+Backspace / Ctrl+W, Ctrl+U and Ctrl+K. Tab, Esc and other Ctrl or Alt keys
    /// aren't handled.
    /// </summary>
    /// <returns>True if the key was one of these.</returns>
    public virtual bool HandleKey(ConsoleKeyInfo key)
    {
        var ctrl = KeyInput.HasControl(key);
        var alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                if (ctrl || alt) return false;
                Insert("\n");
                return Edited();
            case ConsoleKey.LeftArrow:
                return MoveTo(ctrl ? PreviousWord(Cursor) : PreviousCluster(Cursor));
            case ConsoleKey.RightArrow:
                return MoveTo(ctrl ? NextWord(Cursor) : NextCluster(Cursor));
            case ConsoleKey.UpArrow:
                return MoveRows(-1);
            case ConsoleKey.DownArrow:
                return MoveRows(1);
            case ConsoleKey.PageUp:
                return MoveRows(-Math.Max(1, ViewHeight));
            case ConsoleKey.PageDown:
                return MoveRows(Math.Max(1, ViewHeight));
            case ConsoleKey.Home:
                return MoveTo(ctrl ? default : RowHome());
            case ConsoleKey.End:
                return MoveTo(ctrl ? End : RowEnd());
            case ConsoleKey.Backspace:
                // Unix terminals send Ctrl+Backspace as ^H (Backspace + Control); Windows reports the modifier directly
                DeleteRange(ctrl ? PreviousWord(Cursor) : PreviousCluster(Cursor), Cursor);
                return Edited();
            case ConsoleKey.Delete:
                DeleteRange(Cursor, NextCluster(Cursor));
                return Edited();
        }

        if (KeyInput.IsCtrl(key, 'W'))
        {
            DeleteRange(PreviousWord(Cursor), Cursor);
            return Edited();
        }
        if (KeyInput.IsCtrl(key, 'U'))
        {
            DeleteRange(_index == 0 ? PreviousCluster(Cursor) : new TextPosition(_line, 0), Cursor);
            return Edited();
        }
        if (KeyInput.IsCtrl(key, 'K'))
        {
            DeleteRange(Cursor, _index == _lines[_line].Length ? NextCluster(Cursor) : new TextPosition(_line, _lines[_line].Length));
            return Edited();
        }

        return TypeChar(key);
    }

    /// <summary>
    /// A left press inside the text moves the cursor there; the wheel scrolls by three rows without moving it (1.x
    /// spec §6.6). Uses the area from the last draw.
    /// </summary>
    /// <returns>True if the event did either.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!ViewArea.Contains(mouse.X, mouse.Y)) return false;
        switch (mouse.Action)
        {
            case MouseAction.ScrollUp or MouseAction.ScrollDown:
                var delta = mouse.Action == MouseAction.ScrollUp ? -WheelRows : WheelRows;
                ScrollOffset = Math.Clamp(ScrollOffset + delta, 0, Math.Max(0, ViewRowCount - ViewHeight));
                FollowCursor = false;
                return true;
            case MouseAction.Press when mouse.Button == MouseButton.Left:
                var rows = Rows();
                var row = ScrollOffset + mouse.Y - ViewArea.Y;
                var column = mouse.X - ViewArea.X + (ViewWrap == WrapMode.None ? HorizontalOffset : 0);
                var target = row >= rows.Count ? End : new TextPosition(rows[row].Line, IndexAtColumn(rows[row], column));
                MoveTo(target);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Record what the widget drew, so the keys and the mouse can use the same layout.</summary>
    internal void Track(Rect area, WrapMode wrap, int rowCount)
    {
        ViewArea = area;
        ViewWidth = wrap == WrapMode.None ? int.MaxValue : Math.Max(1, area.Width);
        ViewHeight = Math.Max(1, area.Height);
        ViewWrap = wrap;
        ViewRowCount = rowCount;
    }

    /// <summary>The rows of the last drawn layout, for the current cursor.</summary>
    internal List<TextAreaLayout.Row> Rows() => TextAreaLayout.Build(_lines, ViewWidth, ViewWrap, Cursor);

    /// <summary>The display column of the cursor within its row.</summary>
    internal static int ColumnIn(string line, TextAreaLayout.Row row, int index) =>
        UnicodeWidth.Of(line.AsSpan(row.Start, Math.Max(0, index - row.Start)));

    private bool MoveTo(TextPosition position)
    {
        _line = position.Line;
        _index = position.Index;
        _pendingHighSurrogate = null;
        Moved();
        return true;
    }

    private bool Edited()
    {
        Moved();
        return true;
    }

    // A horizontal move, edit or click: the goal column resets and the view follows the cursor
    private void Moved()
    {
        _goalColumn = null;
        FollowCursor = true;
    }

    // ↑ ↓ PgUp PgDn: by wrapped row, keeping the goal column; past the first or last row, to the start or end
    private bool MoveRows(int delta)
    {
        var rows = Rows();
        var current = TextAreaLayout.RowOf(rows, Cursor);
        var goal = _goalColumn ?? ColumnIn(_lines[_line], rows[current], _index);
        var target = current + delta;
        if (target < 0) MoveTo(default);
        else if (target >= rows.Count) MoveTo(End);
        else MoveTo(new TextPosition(rows[target].Line, IndexAtColumn(rows[target], goal)));
        _goalColumn = goal;
        return true;
    }

    private TextPosition RowHome()
    {
        var rows = Rows();
        var row = rows[TextAreaLayout.RowOf(rows, Cursor)];
        return new TextPosition(_line, _index == row.Start ? 0 : row.Start);
    }

    private TextPosition RowEnd()
    {
        var rows = Rows();
        var row = rows[TextAreaLayout.RowOf(rows, Cursor)];
        var end = LastIndexIn(row);
        return new TextPosition(_line, _index == end ? _lines[_line].Length : end);
    }

    /// <summary>The boundary in <paramref name="row"/> nearest to <paramref name="column"/>, not past the row.</summary>
    private int IndexAtColumn(TextAreaLayout.Row row, int column)
    {
        var line = _lines[row.Line];
        var x = 0;
        for (var i = row.Start; i < row.End;)
        {
            var length = StringInfo.GetNextTextElementLength(line, i);
            var width = UnicodeWidth.OfGrapheme(line.AsSpan(i, length));
            if (x + width > column) return i;
            x += width;
            i += length;
        }
        return LastIndexIn(row);
    }

    // The last place the cursor can be and still show on this row: at a wrap point, End is the next row's start
    private int LastIndexIn(TextAreaLayout.Row row) =>
        row.LastOfLine || row.End == row.Start ? row.End : TextFieldState.PreviousBoundary(_lines[row.Line], row.End);

    private TextPosition PreviousCluster(TextPosition p)
    {
        if (p.Index > 0) return p with { Index = TextFieldState.PreviousBoundary(_lines[p.Line], p.Index) };
        return p.Line > 0 ? new TextPosition(p.Line - 1, _lines[p.Line - 1].Length) : p;
    }

    private TextPosition NextCluster(TextPosition p)
    {
        if (p.Index < _lines[p.Line].Length) return p with { Index = TextFieldState.NextBoundary(_lines[p.Line], p.Index) };
        return p.Line < _lines.Count - 1 ? new TextPosition(p.Line + 1, 0) : p;
    }

    // A line break counts as whitespace, so word moves and deletes cross it
    private bool IsSpaceBefore(TextPosition p) => p.Index == 0 ? p.Line > 0 : char.IsWhiteSpace(_lines[p.Line][p.Index - 1]);

    private bool IsSpaceAt(TextPosition p) =>
        p.Index == _lines[p.Line].Length ? p.Line < _lines.Count - 1 : char.IsWhiteSpace(_lines[p.Line][p.Index]);

    private TextPosition PreviousWord(TextPosition p)
    {
        while (p != default && IsSpaceBefore(p)) p = PreviousCluster(p);
        while (p != default && !IsSpaceBefore(p)) p = PreviousCluster(p);
        return p;
    }

    private TextPosition NextWord(TextPosition p)
    {
        var end = End;
        while (p != end && IsSpaceAt(p)) p = NextCluster(p);
        while (p != end && !IsSpaceAt(p)) p = NextCluster(p);
        return p;
    }

    private void DeleteRange(TextPosition start, TextPosition end)
    {
        if (end.Line < start.Line || (end.Line == start.Line && end.Index <= start.Index)) return;
        var joined = _lines[start.Line][..start.Index] + _lines[end.Line][end.Index..];
        _lines[start.Line] = joined;
        if (end.Line > start.Line) _lines.RemoveRange(start.Line + 1, end.Line - start.Line);
        _line = start.Line;
        _index = TextFieldState.SnapToBoundary(joined, start.Index);
    }

    private bool TypeChar(ConsoleKeyInfo key)
    {
        var c = key.KeyChar;
        if (c == '\0' || char.IsControl(c) || KeyInput.HasCommandModifier(key)) return false;

        if (char.IsHighSurrogate(c))
        {
            _pendingHighSurrogate = c;
            return true;
        }

        if (char.IsLowSurrogate(c))
        {
            var high = _pendingHighSurrogate;
            _pendingHighSurrogate = null;
            if (high is { } h) Insert(new string([h, c]));
            return true;
        }

        _pendingHighSurrogate = null;
        Insert(c.ToString());
        return true;
    }

    // Insert at the cursor: line breaks split lines, tabs become spaces, other control characters are dropped
    private void InsertNormalized(string text)
    {
        var line = _lines[_line];
        var after = line[_index..];
        var current = new StringBuilder(line, 0, _index, _index + text.Length);
        var added = new List<string>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                added.Add(current.ToString());
                current.Clear();
            }
            else if (c == '\t')
            {
                var column = UnicodeWidth.Of(current.ToString());
                current.Append(' ', TabSize - column % TabSize);
            }
            else if (!char.IsControl(c))
            {
                current.Append(c);
            }
        }

        var cursor = current.Length;
        current.Append(after);
        if (added.Count == 0)
        {
            _lines[_line] = current.ToString();
        }
        else
        {
            _lines[_line] = added[0];
            added.RemoveAt(0);
            added.Add(current.ToString());
            _lines.InsertRange(_line + 1, added);
            _line += added.Count;
        }
        _index = TextFieldState.SnapToBoundary(_lines[_line], cursor);
    }
}

/// <summary>Splits a text area's lines into rows of UTF-16 index ranges (1.x spec §6.5).</summary>
internal static class TextAreaLayout
{
    /// <summary>One row: <paramref name="Start"/> to <paramref name="End"/> of a line, <paramref name="Width"/> columns wide.</summary>
    public readonly record struct Row(int Line, int Start, int End, int Width, bool LastOfLine);

    /// <summary>
    /// The rows for <paramref name="width"/> columns. With word wrap, spaces at a wrap point stay at the end of their
    /// row (and may hang past the edge). When the cursor is at the end of a line whose last row is full, an empty
    /// row follows for it, so the cursor is always drawn inside the area.
    /// </summary>
    public static List<Row> Build(IReadOnlyList<string> lines, int width, WrapMode wrap, TextPosition cursor)
    {
        var rows = new List<Row>(lines.Count);
        for (var l = 0; l < lines.Count; l++)
        {
            var first = rows.Count;
            if (wrap == WrapMode.None || width == int.MaxValue) rows.Add(new Row(l, 0, lines[l].Length, UnicodeWidth.Of(lines[l]), true));
            else WrapLine(lines[l], l, Math.Max(1, width), wrap, rows);

            var last = rows[^1];
            if (wrap != WrapMode.None && width != int.MaxValue && cursor.Line == l && cursor.Index == last.End
                && last.Width >= width && last.End > last.Start && rows.Count > first)
            {
                rows[^1] = last with { LastOfLine = false };
                rows.Add(new Row(l, last.End, last.End, 0, true));
            }
        }
        return rows;
    }

    /// <summary>The row the cursor shows on: at a wrap point, the next row.</summary>
    public static int RowOf(List<Row> rows, TextPosition cursor)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Line != cursor.Line) continue;
            if (cursor.Index < row.End || row.LastOfLine) return i;
        }
        return rows.Count - 1;
    }

    private static void WrapLine(string line, int lineNumber, int width, WrapMode wrap, List<Row> rows)
    {
        var rowStart = 0;
        var rowWidth = 0;
        var breakAt = -1;       // where the next row can start: just after whitespace in this row
        var widthAtBreak = 0;
        for (var i = 0; i < line.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(line, i);
            var w = UnicodeWidth.OfGrapheme(line.AsSpan(i, length));
            var isSpace = char.IsWhiteSpace(line[i]);

            if (rowWidth + w > width && !(isSpace && wrap == WrapMode.Word))
            {
                if (wrap == WrapMode.Word && breakAt > rowStart)
                {
                    rows.Add(new Row(lineNumber, rowStart, breakAt, widthAtBreak, false));
                    rowStart = breakAt;
                    rowWidth -= widthAtBreak;
                    breakAt = -1;
                }
                if (rowWidth + w > width && rowWidth > 0)
                {
                    // No whitespace to break at (or Char wrap): break before this cluster
                    rows.Add(new Row(lineNumber, rowStart, i, rowWidth, false));
                    rowStart = i;
                    rowWidth = 0;
                    breakAt = -1;
                }
            }

            rowWidth += w;
            i += length;
            if (isSpace)
            {
                breakAt = i;
                widthAtBreak = rowWidth;
            }
        }
        rows.Add(new Row(lineNumber, rowStart, line.Length, rowWidth, true));
    }
}
