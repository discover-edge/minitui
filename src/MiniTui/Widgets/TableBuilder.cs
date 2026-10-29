namespace MiniTui;

/// <summary>
/// A table row: one line per cell, with an optional style for the whole row.
/// </summary>
public sealed class Row
{
    /// <summary>Create a row from its cells.</summary>
    public Row(params Line[] cells) => Cells = cells ?? [];

    /// <summary>The cells, left to right.</summary>
    public IReadOnlyList<Line> Cells { get; }

    /// <summary>A style patched over the row's text before the cell styles.</summary>
    public Style Style { get; init; }
}

/// <summary>
/// A table with a header, constraint-sized columns and a scrolling selection (§8.3).
/// Only the visible rows are formatted, so the cost per frame doesn't depend on the row count.
/// </summary>
/// <example>
/// <code>
/// Widget.Table()
///     .Header("Name", "Status")
///     .Columns(Constraint.Fill(), Constraint.Length(10))
///     .Rows(services, s => new Row(s.Name, s.Status))
///     .State(selection)
///     .Focused()
///     .Draw(buf, area);
/// </code>
/// </example>
public sealed class TableBuilder
{
    private Line[]? _header;
    private Constraint[]? _columns;
    private int _columnSpacing = 1;
    private int _count;
    private Func<int, Row> _row = _ => new Row();
    private SelectionState? _state;
    private bool _focused;
    private string? _highlightSymbol;
    private BlockBuilder? _block;
    private Style? _style;
    private Style? _headerStyle;
    private Style? _highlightStyle;
    private bool _headerSeparator;

    /// <summary>The header cells.</summary>
    public TableBuilder Header(params Line[] cells) { _header = cells; return this; }

    /// <summary>Column widths, solved like a horizontal <see cref="Layout"/>. By default each column is <c>Fill(1)</c>.</summary>
    public TableBuilder Columns(params Constraint[] widths) { _columns = widths; return this; }

    /// <summary>Empty columns between columns. The default is 1.</summary>
    public TableBuilder ColumnSpacing(int spacing) { _columnSpacing = Math.Max(0, spacing); return this; }

    /// <summary>The rows.</summary>
    public TableBuilder Rows(IReadOnlyList<Row> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _count = rows.Count;
        _row = i => rows[i];
        return this;
    }

    /// <summary>The rows, formatted by <paramref name="format"/>. Only visible rows are formatted.</summary>
    public TableBuilder Rows<T>(IReadOnlyList<T> items, Func<T, Row> format)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(format);
        _count = items.Count;
        _row = i => format(items[i]);
        return this;
    }

    /// <summary>The selection. The table clamps it and scrolls to keep it visible. Without one, no row is highlighted.</summary>
    public TableBuilder State(SelectionState state) { _state = state; return this; }

    /// <summary>Use the focused selection style.</summary>
    public TableBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>A marker drawn before the selected row, such as <c>"> "</c>.</summary>
    public TableBuilder HighlightSymbol(string symbol) { _highlightSymbol = symbol; return this; }

    /// <summary>Draw a block around the table.</summary>
    public TableBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's text style.</summary>
    public TableBuilder Style(Style style) { _style = style; return this; }

    /// <summary>A style patched over the theme's header style.</summary>
    public TableBuilder HeaderStyle(Style style) { _headerStyle = style; return this; }

    /// <summary>A style patched over the theme's selection style.</summary>
    public TableBuilder HighlightStyle(Style style) { _highlightStyle = style; return this; }

    // A ─ line under the header, as the 0.7 static Widgets.Table drew
    internal TableBuilder HeaderSeparator(bool separator = true) { _headerSeparator = separator; return this; }

    /// <summary>Draw the table.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var theme = Theme.Current;
        BlockBuilder? block = null;
        if (_block is not null)
        {
            block = _block.Clone();
            if (_focused) block.Focused();
        }
        var inner = block?.Draw(buf, area) ?? area;

        var headerRows = _header is null ? 0 : _headerSeparator ? 2 : 1;
        var viewport = Math.Max(0, inner.Height - headerRows);
        _state?.Fit(_count, viewport);
        if (inner.IsEmpty) return;

        var offset = _state?.Offset ?? 0;
        var visible = Math.Clamp(_count - offset, 0, viewport);
        var overflow = _count > viewport && viewport > 0;
        var scrollbarOnBorder = block?.HasRightBorder == true;
        var body = overflow && !scrollbarOnBorder ? inner with { Width = inner.Width - 1 } : inner;

        var symbol = _highlightSymbol ?? "";
        var symbolWidth = Math.Min(UnicodeWidth.Of(symbol), body.Width);
        var columnCount = _columns?.Length ?? _header?.Length ?? (visible > 0 ? _row(offset).Cells.Count : 0);
        var constraints = _columns ?? Enumerable.Repeat(Constraint.Fill(), columnCount).ToArray();
        var columns = LayoutSolver.Solve(body.Width - symbolWidth, constraints, _columnSpacing, Flex.Start);
        var x0 = body.X + symbolWidth;

        var style = theme.Text.Patch(_style ?? default);
        var y = body.Y;
        if (_header is not null)
        {
            var headerStyle = style.Patch(theme.TableHeader).Patch(_headerStyle ?? default);
            for (var c = 0; c < columns.Length && c < _header.Length; c++)
                DrawHeaderCell(buf, x0 + columns[c].Offset, y, _header[c], columns[c].Size, headerStyle, c);
            y++;
            if (_headerSeparator && y < body.Bottom)
            {
                WidgetText.HorizontalLine(buf, body.X, y, body.Width, theme.Border);
                y++;
            }
        }

        var highlight = (_focused ? theme.Selection : theme.SelectionUnfocused).Patch(_highlightStyle ?? default);
        for (var i = 0; i < visible; i++)
        {
            var index = offset + i;
            var row = _row(index);
            var rowStyle = style.Patch(row.Style);
            var selected = _state is { } s && s.Selected == index;
            if (selected && symbolWidth > 0) buf.SetString(body.X, y, symbol, rowStyle, symbolWidth);
            for (var c = 0; c < columns.Length && c < row.Cells.Count; c++)
                WidgetText.DrawLine(buf, x0 + columns[c].Offset, y, row.Cells[c], columns[c].Size, rowStyle);
            if (selected) buf.SetStyle(new Rect(body.X, y, body.Width, 1), highlight);
            y++;
        }

        if (overflow)
        {
            var top = body.Y + headerRows;
            var bar = scrollbarOnBorder ? new Rect(area.Right - 1, top, 1, viewport) : new Rect(body.Right, top, 1, viewport);
            new ScrollbarBuilder().ContentLength(_count).ViewportLength(viewport).Position(offset).Draw(buf, bar);
        }
    }

    private void DrawHeaderCell(Buffer buf, int x, int y, Line cell, int width, Style style, int column)
    {
        if (_state?.SortColumn != column || width < 3)
        {
            WidgetText.DrawLine(buf, x, y, cell, width, style);
            return;
        }

        // Keep the sort marker visible: the text gets what's left after " ▲"
        var used = WidgetText.DrawLine(buf, x, y, cell, width - 2, style, Alignment.Left);
        buf.SetString(x + used + 1, y, _state.SortDescending ? "▼" : "▲", style, 1);
    }
}
