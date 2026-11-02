namespace MiniTui;

/// <summary>
/// Two panes with a divider between them (1.x spec §14.3), from a <see cref="SplitState"/>. <see cref="Draw"/>
/// draws only the divider and returns the panes for the app to draw into.
/// </summary>
/// <example>
/// <code>
/// var (left, right) = Widget.Split(_split).Draw(buf, area);
/// ctx.Render(_files, buf, left);
/// ctx.Render(_preview, buf, right);
/// </code>
/// </example>
public sealed class SplitBuilder
{
    private readonly SplitState _state;
    private bool _focused;
    private Style? _style;

    internal SplitBuilder(SplitState state) => _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>Draw the divider in the focused border style, as while it's dragged.</summary>
    public SplitBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>A style patched over the theme's border style for the divider.</summary>
    public SplitBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the divider and return the two panes.</summary>
    public (Rect First, Rect Second) Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var (first, second, divider) = _state.Layout(area);
        if (divider.IsEmpty) return (first, second);

        var theme = Theme.Current;
        var style = (_focused || _state.IsDragging ? theme.BorderFocused : theme.Border).Patch(_style ?? default);
        var glyph = _state.Direction == Direction.Horizontal ? "│" : "─";
        var clip = divider.Intersect(buf.Area);
        for (var y = clip.Y; y < clip.Bottom; y++)
        for (var x = clip.X; x < clip.Right; x++)
            buf.SetString(x, y, glyph, theme.Text.Patch(style), 1);
        return (first, second);
    }
}
