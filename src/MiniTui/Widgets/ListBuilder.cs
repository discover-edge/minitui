using System.Globalization;

namespace MiniTui;

/// <summary>
/// A bordered, scrollable list with a selection.
/// </summary>
/// <example>
/// <code>
/// Widget.List().Title("Files").Items(files, f => f.Name).State(selection).Focused().Draw(buf, area);
/// </code>
/// </example>
public class ListBuilder
{
    private Vec2? _hover;
    private int _count;
    private Func<int, Line> _item = _ => "";
    private SelectionState? _state;
    private bool _focused;
    private Func<int, object>? _key;
    private string? _title;
    private bool _showScrollInfo = true;
    private string? _highlightSymbol;
    private BlockBuilder? _block;
    private Style? _style;
    private Style? _highlightStyle;

    /// <summary>The items to display.</summary>
    public ListBuilder Items(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _count = items.Count;
        _item = i => items[i];
        _key = null;
        return this;
    }

    /// <summary>The items to display, formatted by <paramref name="format"/>. Only visible items are formatted.</summary>
    public ListBuilder Items<T>(IReadOnlyList<T> items, Func<T, Line> format)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(format);
        _count = items.Count;
        _item = i => format(items[i]);
        _key = null;
        return this;
    }

    /// <summary>
    /// The items, formatted by <paramref name="format"/>, with the selection kept on the item with the selected key
    /// when the items change (1.x spec §24.3). Only visible items are formatted.
    /// </summary>
    public ListBuilder Items<T>(IReadOnlyList<T> items, Func<T, Line> format, Func<T, object> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Items(items, format);
        _key = i => key(items[i]);
        return this;
    }

    /// <summary>The selection. The list clamps it and scrolls to keep it visible.</summary>
    public ListBuilder State(SelectionState state) { _state = state; return this; }

    /// <summary>Set the selected item index.</summary>
    public ListBuilder Selected(int index)
    {
        _state ??= new SelectionState();
        _state.Selected = index;
        return this;
    }

    /// <summary>Set the scroll offset. The list adjusts it so the selection is visible.</summary>
    public ListBuilder ScrollOffset(int offset)
    {
        _state ??= new SelectionState();
        _state.Offset = offset;
        return this;
    }

    /// <summary>Set whether the list is focused: the border and selection use the focused styles.</summary>
    public ListBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>Set the list title. Scroll info such as <c>[1-10 of 50]</c> is appended unless turned off.</summary>
    public ListBuilder Title(string title) { _title = title; return this; }

    /// <summary>Show or hide scroll position info in title (default: true).</summary>
    public ListBuilder ShowScrollInfo(bool show = true) { _showScrollInfo = show; return this; }

    /// <summary>A marker drawn before the selected item, such as <c>"> "</c>. Other items are indented to match.</summary>
    public ListBuilder HighlightSymbol(string symbol) { _highlightSymbol = symbol; return this; }

    /// <summary>The border around the list. By default, a single-line border on all sides.</summary>
    public ListBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's text style.</summary>
    public ListBuilder Style(Style style) { _style = style; return this; }

    /// <summary>A style patched over the theme's selection style.</summary>
    public ListBuilder HighlightStyle(Style style) { _highlightStyle = style; return this; }

    /// <summary>
    /// The pointer, usually <see cref="RenderContext.Hover"/>. <see cref="Theme.Hover"/> is patched over the row under it, unless it's the selected one (1.x spec §17.6).
    /// </summary>
    public ListBuilder Hover(Vec2? position) { _hover = position; return this; }

    /// <summary>Draw the list to the buffer.</summary>
    public void Draw(CellBuffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var theme = Theme.Current;
        var block = (_block ?? new BlockBuilder()).Clone();
        if (_focused) block.Focused();

        var inner = block.Inner(area);
        var viewport = Math.Max(0, inner.Height);
        _state?.Fit(_count, viewport, _key);
        var offset = _state?.Offset ?? 0;
        var visible = Math.Clamp(_count - offset, 0, viewport);

        if (_title is not null) block.Title(TitleWithScrollInfo(offset, visible, viewport));
        block.Draw(buf, area);
        _state?.Track(area, default);
        if (inner.IsEmpty) return;

        var overflow = _count > viewport;
        var scrollbar = block.HasRightBorder
            ? new Rect(area.Right - 1, inner.Y, 1, inner.Height)
            : new Rect(inner.Right - 1, inner.Y, 1, inner.Height);
        var rows = overflow && !block.HasRightBorder ? inner with { Width = inner.Width - 1 } : inner;
        // 1.x spec §24.2: the highlight and the clicks reach across the padding, border to border
        var band = block.AcrossPadding(area, rows);
        _state?.Track(area, band with { Height = visible });

        var style = theme.Text.Patch(_style ?? default);
        var highlight = (_focused || block.IsFocused ? theme.Selection : theme.SelectionUnfocused).Patch(_highlightStyle ?? default);
        var symbol = _highlightSymbol ?? "";
        var symbolWidth = Math.Min(UnicodeWidth.Of(symbol), rows.Width);

        for (var i = 0; i < visible; i++)
        {
            var index = offset + i;
            var y = rows.Y + i;
            var selected = _state is { } s && s.Selected == index;
            if (selected && symbolWidth > 0) buf.SetString(rows.X, y, symbol, style, symbolWidth);
            WidgetText.DrawLine(buf, rows.X + symbolWidth, y, _item(index), rows.Width - symbolWidth, style);
            if (selected) buf.SetStyle(new Rect(band.X, y, band.Width, 1), highlight);
            else if (_hover is { } p && p.Y == y && p.X >= band.X && p.X < band.Right) buf.SetStyle(new Rect(band.X, y, band.Width, 1), theme.Hover);
        }

        if (!overflow) return;
        new ScrollbarBuilder().ContentLength(_count).ViewportLength(viewport).Position(offset).Draw(buf, scrollbar);
        _state?.TrackScrollbar(scrollbar, _count);
    }

    private string TitleWithScrollInfo(int offset, int visible, int viewport)
    {
        if (!_showScrollInfo || _count == 0) return _title!;

        // A padded title such as " Files " keeps its padding outside the info: " Files [3] "
        var title = _title!.TrimEnd();
        var pad = title.Length < _title.Length ? " " : "";
        var c = CultureInfo.InvariantCulture;
        return _count > viewport
            ? string.Create(c, $"{title} [{offset + 1}-{offset + visible} of {_count}]{pad}")
            : string.Create(c, $"{title} [{_count}]{pad}");
    }
}
