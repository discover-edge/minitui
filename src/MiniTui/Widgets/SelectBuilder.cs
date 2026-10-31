namespace MiniTui;

/// <summary>
/// A dropdown field (1.x spec §7.5): the chosen option, or a muted placeholder, and a ▼ at the right edge.
/// </summary>
public sealed class SelectBuilder
{
    private SelectState? _state;
    private string? _placeholder;
    private bool _focused;
    private BlockBuilder? _block;
    private Style? _style;

    /// <summary>The options and choice.</summary>
    public SelectBuilder State(SelectState state) { _state = state; return this; }

    /// <summary>Muted text shown while nothing is chosen.</summary>
    public SelectBuilder Placeholder(string? placeholder) { _placeholder = placeholder; return this; }

    /// <summary>Use the focused styles.</summary>
    public SelectBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>Draw a block around the field. Its border shows focus.</summary>
    public SelectBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's input style.</summary>
    public SelectBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the field inside its block (or area).</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Clone().Focused(_focused || _block.IsFocused).Draw(buf, area) ?? area;
        _state?.Track(area);
        if (inner.IsEmpty) return;

        var theme = Theme.Current;
        var style = (_focused ? theme.InputFocused : theme.Input).Patch(_style ?? default);
        var row = inner with { Height = 1 };
        buf.SetStyle(row, style);

        var value = _state?.Value;
        var textWidth = Math.Max(0, inner.Width - 2);
        if (value is not null) WidgetText.DrawString(buf, inner.X, inner.Y, Sanitizer.SingleLine(value), textWidth, style);
        else if (!string.IsNullOrEmpty(_placeholder)) WidgetText.DrawString(buf, inner.X, inner.Y, _placeholder, textWidth, style.Patch(theme.TextMuted));
        buf.SetString(inner.Right - 1, inner.Y, "▼", style, 1);
    }
}
