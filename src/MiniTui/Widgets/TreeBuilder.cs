using System.Globalization;

namespace MiniTui;

/// <summary>
/// A bordered, scrollable tree with a selection (1.x spec §11.5).
/// </summary>
/// <example>
/// <code>
/// Widget.Tree(tree).Title("Files").Guides().Focused(ctx.Focused).Draw(buf, area);
/// </code>
/// </example>
public class TreeBuilder<T> where T : class
{
    private Vec2? _hover;
    private readonly TreeState<T> _state;
    private Func<T, Line>? _format;
    private bool _guides;
    private int _indent = 2;
    private bool _focused;
    private string? _title;
    private bool _showScrollInfo = true;
    private string? _highlightSymbol;
    private BlockBuilder? _block;
    private Style? _style;
    private Style? _highlightStyle;

    /// <summary>Create a builder for <paramref name="state"/>.</summary>
    internal TreeBuilder(TreeState<T> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <summary>How a node is drawn. By default, its text from the state.</summary>
    public TreeBuilder<T> Format(Func<T, Line> format)
    {
        ArgumentNullException.ThrowIfNull(format);
        _format = format;
        return this;
    }

    /// <summary>Draw guide lines (│ ├─ └─) from each node to its parent, instead of indentation only.</summary>
    public TreeBuilder<T> Guides(bool guides = true) { _guides = guides; return this; }

    /// <summary>Columns per level without guides (default 2, at least 1). Guides always take 2.</summary>
    public TreeBuilder<T> Indent(int columns) { _indent = Math.Max(1, columns); return this; }

    /// <summary>Set whether the tree is focused: the border and selection use the focused styles.</summary>
    public TreeBuilder<T> Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>
    /// The pointer, usually <see cref="RenderContext.Hover"/>. <see cref="Theme.Hover"/> is patched over the row under it, unless it's the selected one (1.x spec §17.6).
    /// </summary>
    public TreeBuilder<T> Hover(Vec2? position) { _hover = position; return this; }

    /// <summary>Set the title. Scroll info such as <c>[1-10 of 50]</c> is appended unless turned off.</summary>
    public TreeBuilder<T> Title(string title) { _title = title; return this; }

    /// <summary>Show or hide scroll position info in the title (default: true).</summary>
    public TreeBuilder<T> ShowScrollInfo(bool show = true) { _showScrollInfo = show; return this; }

    /// <summary>A marker drawn before the selected row, such as <c>"> "</c>. Other rows are indented to match.</summary>
    public TreeBuilder<T> HighlightSymbol(string symbol) { _highlightSymbol = symbol; return this; }

    /// <summary>The border around the tree. By default, a single-line border on all sides.</summary>
    public TreeBuilder<T> Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's text style.</summary>
    public TreeBuilder<T> Style(Style style) { _style = style; return this; }

    /// <summary>A style patched over the theme's selection style.</summary>
    public TreeBuilder<T> HighlightStyle(Style style) { _highlightStyle = style; return this; }

    /// <summary>Draw the tree to the buffer.</summary>
    public void Draw(CellBuffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var theme = Theme.Current;
        var block = (_block ?? new BlockBuilder()).Clone();
        if (_focused) block.Focused();

        var inner = block.Inner(area);
        var viewport = Math.Max(0, inner.Height);
        _state.Fit(viewport);
        var rows = _state.Rows;
        var offset = _state.Offset;
        var visible = Math.Clamp(rows.Count - offset, 0, viewport);

        if (_title is not null) block.Title(TitleWithScrollInfo(rows.Count, offset, visible, viewport));
        block.Draw(buf, area);
        _state.Track(area, default, []);
        if (inner.IsEmpty) return;

        var overflow = rows.Count > viewport;
        var scrollbar = block.HasRightBorder
            ? new Rect(area.Right - 1, inner.Y, 1, inner.Height)
            : new Rect(inner.Right - 1, inner.Y, 1, inner.Height);
        var rowArea = overflow && !block.HasRightBorder ? inner with { Width = inner.Width - 1 } : inner;

        var style = theme.Text.Patch(_style ?? default);
        var muted = style.Patch(theme.TextMuted);
        var highlight = (_focused || block.IsFocused ? theme.Selection : theme.SelectionUnfocused).Patch(_highlightStyle ?? default);
        var symbol = _highlightSymbol ?? "";
        var symbolWidth = Math.Min(UnicodeWidth.Of(symbol), rowArea.Width);
        var prefixes = Prefixes(rows, offset, visible);
        var expanders = new int[visible];
        var checks = new int[visible];
        var selected = rows.Count > 0 && _state.Selected is not null ? IndexOfSelected(rows) : -1;

        for (var i = 0; i < visible; i++)
        {
            var row = rows[offset + i];
            var y = rowArea.Y + i;
            var x = rowArea.X;
            var right = rowArea.Right;
            if (offset + i == selected && symbolWidth > 0) buf.SetString(x, y, symbol, style, symbolWidth);
            x += symbolWidth;
            x += buf.SetString(x, y, prefixes[i], muted, Math.Max(0, right - x));

            expanders[i] = x < right && row.HasChildren ? x : -1;
            x += buf.SetString(x, y, Expander(row), muted, Math.Max(0, right - x));
            x += buf.SetString(x, y, " ", style, Math.Max(0, right - x));
            checks[i] = -1;
            if (_state.Checkboxes && right - x >= 3)
            {
                // 1.x spec §18.5: [✓], [ ] or [-] before the label
                checks[i] = x;
                var mark = _state.IsChecked(row.Node) switch { true => Glyphs.Basic ? "x" : "✓", null => "-", false => " " };
                x += buf.SetString(x, y, "[", style, right - x);
                x += buf.SetString(x, y, mark, theme.Success, right - x);
                x += buf.SetString(x, y, "] ", style, Math.Max(0, right - x));
            }
            var label = _format?.Invoke(row.Node) ?? _state.TextOf(row.Node);
            x += WidgetText.DrawLine(buf, x, y, label, Math.Max(0, right - x), style);
            // 1.x spec §18.6: a load running or failed, after the label
            var (loading, error) = _state.LoadStatus(row.Node);
            if (loading && right - x > 1) buf.SetString(x + 1, y, "Loading…", muted, right - x - 1);
            else if (error is not null && right - x > 1) buf.SetString(x + 1, y, Sanitizer.SingleLine(error), style.Patch(theme.Error), right - x - 1);
            if (offset + i == selected) buf.SetStyle(new Rect(rowArea.X, y, rowArea.Width, 1), highlight);
            else if (_hover is { } p && p.Y == y && p.X >= rowArea.X && p.X < rowArea.Right) buf.SetStyle(new Rect(rowArea.X, y, rowArea.Width, 1), theme.Hover);
        }
        _state.Track(area, rowArea with { Height = visible }, expanders, checks);

        if (!overflow) return;
        new ScrollbarBuilder().ContentLength(rows.Count).ViewportLength(viewport).Position(offset).Draw(buf, scrollbar);
        _state.TrackScrollbar(scrollbar);
    }

    private int IndexOfSelected(IReadOnlyList<TreeRow<T>> rows)
    {
        for (var i = 0; i < rows.Count; i++)
            if (ReferenceEquals(rows[i].Node, _state.Selected)) return i;
        return -1;
    }

    private static string Expander(TreeRow<T> row) => !row.HasChildren ? " "
        : Glyphs.Basic ? (row.IsExpanded ? "-" : "+")
        : row.IsExpanded ? "▾" : "▸";

    /// <summary>
    /// The indentation before each visible row's expander. Guides need to know, for each level, whether a later row
    /// continues it, so they're found walking back from the last row.
    /// </summary>
    private string[] Prefixes(IReadOnlyList<TreeRow<T>> rows, int offset, int visible)
    {
        var prefixes = new string[visible];
        if (!_guides)
        {
            for (var i = 0; i < visible; i++) prefixes[i] = new string(' ', rows[offset + i].Depth * _indent);
            return prefixes;
        }

        // seen[d]: a row at depth d comes later, before any row shallower than d
        var seen = new List<bool>();
        var text = new System.Text.StringBuilder();
        for (var i = rows.Count - 1; i >= offset; i--)
        {
            var depth = rows[i].Depth;
            while (seen.Count <= depth) seen.Add(false);
            if (i < offset + visible)
            {
                text.Clear();
                for (var level = 1; level < depth; level++) text.Append(seen[level] ? "│ " : "  ");
                if (depth > 0) text.Append(seen[depth] ? "├─" : "└─");
                prefixes[i - offset] = text.ToString();
            }
            seen[depth] = true;
            for (var deeper = depth + 1; deeper < seen.Count; deeper++) seen[deeper] = false;
        }
        return prefixes;
    }

    private string TitleWithScrollInfo(int count, int offset, int visible, int viewport)
    {
        if (!_showScrollInfo || count == 0) return _title!;

        // A padded title such as " Files " keeps its padding outside the info: " Files [3] "
        var title = _title!.TrimEnd();
        var pad = title.Length < _title.Length ? " " : "";
        var c = CultureInfo.InvariantCulture;
        return count > viewport
            ? string.Create(c, $"{title} [{offset + 1}-{offset + visible} of {count}]{pad}")
            : string.Create(c, $"{title} [{count}]{pad}");
    }
}
