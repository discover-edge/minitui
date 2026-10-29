namespace MiniTui;

/// <summary>
/// A single-line text input. It scrolls horizontally by display width to keep the cursor visible, and when
/// focused shows the terminal cursor at the insertion point.
/// </summary>
public sealed class TextFieldBuilder
{
    private TextFieldState? _state;
    private string? _placeholder;
    private bool _focused;
    private BlockBuilder? _block;
    private Style? _style;

    /// <summary>The text and cursor.</summary>
    public TextFieldBuilder State(TextFieldState state) { _state = state; return this; }

    /// <summary>Muted text shown while the field is empty.</summary>
    public TextFieldBuilder Placeholder(string? placeholder) { _placeholder = placeholder; return this; }

    /// <summary>Show the cursor and use the focused styles.</summary>
    public TextFieldBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>Draw a block around the field. Its border shows focus.</summary>
    public TextFieldBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's input style.</summary>
    public TextFieldBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the field on the first row inside its block (or area).</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Clone().Focused(_focused || _block.IsFocused).Draw(buf, area) ?? area;
        var theme = Theme.Current;
        var style = (_focused ? theme.InputFocused : theme.Input).Patch(_style ?? default);
        TextFieldRenderer.Draw(buf, inner, _state ?? new TextFieldState(), _focused, style, _placeholder);
    }
}
