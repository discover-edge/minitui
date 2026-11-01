using System.Globalization;

namespace MiniTui;

/// <summary>
/// A multi-line text input (1.x spec §6). Long lines wrap; the view scrolls vertically to keep the cursor visible,
/// and when focused shows the terminal cursor at the insertion point.
/// </summary>
public sealed class TextAreaBuilder
{
    private TextAreaState? _state;
    private string? _placeholder;
    private bool _focused;
    private BlockBuilder? _block;
    private Style? _style;
    private WrapMode _wrap = WrapMode.Word;

    /// <summary>The text and cursor.</summary>
    public TextAreaBuilder State(TextAreaState state) { _state = state; return this; }

    /// <summary>Muted text shown on the first row while the text is empty.</summary>
    public TextAreaBuilder Placeholder(string? placeholder) { _placeholder = placeholder; return this; }

    /// <summary>Show the cursor and use the focused styles.</summary>
    public TextAreaBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>Draw a block around the text area. Its border shows focus.</summary>
    public TextAreaBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's input style.</summary>
    public TextAreaBuilder Style(Style style) { _style = style; return this; }

    /// <summary>
    /// How long lines break: <see cref="WrapMode.Word"/> (the default) at whitespace, <see cref="WrapMode.Char"/>
    /// anywhere, or <see cref="WrapMode.None"/>, which scrolls the view horizontally instead.
    /// </summary>
    public TextAreaBuilder Wrap(WrapMode wrap) { _wrap = wrap; return this; }

    /// <summary>Draw the text area inside its block (or area).</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Clone().Focused(_focused || _block.IsFocused).Draw(buf, area) ?? area;
        var state = _state ?? new TextAreaState();
        var theme = Theme.Current;
        var style = (_focused ? theme.InputFocused : theme.Input).Patch(_style ?? default);
        if (inner.IsEmpty)
        {
            state.Track(inner, _wrap, 0);
            return;
        }
        buf.SetStyle(inner, style);

        var width = _wrap == WrapMode.None ? int.MaxValue : inner.Width;
        var rows = TextAreaLayout.Build(state.Lines, width, _wrap, state.Cursor);
        state.Track(inner, _wrap, rows.Count);
        var cursor = state.Cursor;
        var cursorRow = TextAreaLayout.RowOf(rows, cursor);
        var cursorColumn = TextAreaState.ColumnIn(state.Lines[cursor.Line], rows[cursorRow], cursor.Index);
        Scroll(state, rows.Count, cursorRow, cursorColumn, inner);

        var empty = state.Lines.Count == 1 && state.Lines[0].Length == 0;
        if (empty && !string.IsNullOrEmpty(_placeholder))
        {
            WidgetText.DrawString(buf, inner.X, inner.Y, _placeholder, inner.Width, style.Patch(theme.TextMuted));
        }
        else
        {
            var left = _wrap == WrapMode.None ? state.HorizontalOffset : 0;
            var selection = _focused ? theme.Selection : theme.SelectionUnfocused;
            for (var y = 0; y < inner.Height && state.ScrollOffset + y < rows.Count; y++)
            {
                var visible = rows[state.ScrollOffset + y];
                DrawRow(buf, inner.X, inner.Y + y, state.Lines, visible, left, inner.Width, style);
                if (state.HasSelection) DrawSelection(buf, inner, inner.Y + y, state, visible, left, selection);
            }
        }

        if (!_focused) return;
        var x = inner.X + cursorColumn - (_wrap == WrapMode.None ? state.HorizontalOffset : 0);
        var row = cursorRow - state.ScrollOffset;
        if (row < 0 || row >= inner.Height) return;

        // Spaces hanging past the edge at a wrap point put the cursor in the last column
        x = Math.Min(x, inner.Right - 1);
        buf.SetStyle(new Rect(x, inner.Y + row, 1, 1), theme.Cursor);
        buf.SetCursor(x, inner.Y + row);
    }

    // Follow the cursor unless the wheel moved the view, then keep the last row from rising above the bottom
    private void Scroll(TextAreaState state, int rowCount, int cursorRow, int cursorColumn, Rect inner)
    {
        var top = state.ScrollOffset;
        if (state.FollowCursor)
        {
            if (cursorRow < top) top = cursorRow;
            if (cursorRow >= top + inner.Height) top = cursorRow - inner.Height + 1;
        }
        state.ScrollOffset = Math.Clamp(top, 0, Math.Max(0, rowCount - inner.Height));

        if (_wrap != WrapMode.None)
        {
            state.HorizontalOffset = 0;
            return;
        }
        var left = state.HorizontalOffset;
        if (state.FollowCursor)
        {
            if (cursorColumn < left) left = cursorColumn;
            if (cursorColumn >= left + inner.Width) left = cursorColumn - inner.Width + 1;
        }
        state.HorizontalOffset = Math.Max(0, left);
    }

    // 1.x spec §10.8: the selected part of a row, and a cell for a selected line break when the row has room
    private static void DrawSelection(Buffer buf, Rect inner, int y, TextAreaState state, TextAreaLayout.Row row, int left, Style style)
    {
        var (start, end) = (state.SelectionStart, state.SelectionEnd);
        if (row.Line < start.Line || row.Line > end.Line) return;
        var line = state.Lines[row.Line];
        var from = Math.Max(row.Line == start.Line ? start.Index : 0, row.Start);
        var to = Math.Min(row.Line == end.Line ? end.Index : line.Length, row.End);
        if (from > to) return;
        var x0 = TextAreaState.ColumnIn(line, row, from) - left;
        var x1 = TextAreaState.ColumnIn(line, row, to) - left;
        if (row.LastOfLine && row.Line < end.Line) x1++;
        x0 = Math.Max(x0, 0);
        x1 = Math.Min(x1, inner.Width);
        if (x1 > x0) buf.SetStyle(new Rect(inner.X + x0, y, x1 - x0, 1), style);
    }

    private static void DrawRow(Buffer buf, int x, int y, IReadOnlyList<string> lines, TextAreaLayout.Row row, int left, int width, Style style)
    {
        var line = lines[row.Line];
        var column = 0;
        for (var i = row.Start; i < row.End;)
        {
            var length = StringInfo.GetNextTextElementLength(line, i);
            var grapheme = line.AsSpan(i, length);
            i += length;
            var w = UnicodeWidth.OfGrapheme(grapheme);
            var visibleStart = column - left;
            column += w;
            if (column <= left) continue;
            if (visibleStart < 0) continue; // a wide character cut by the left edge stays blank
            if (visibleStart + w > width) break;
            buf.SetString(x + visibleStart, y, grapheme, style, w);
        }
    }
}
