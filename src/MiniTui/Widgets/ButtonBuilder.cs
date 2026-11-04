namespace MiniTui;

/// <summary>
/// A button with a centered label.
/// </summary>
public sealed class ButtonBuilder
{
    private Vec2? _hover;
    private string _label = "";
    private bool _focused;
    private bool _pressed;
    private Style? _style;
    private ButtonState? _state;

    /// <summary>The label.</summary>
    public ButtonBuilder Label(string label) { _label = label ?? ""; return this; }

    /// <summary>Use the focused style.</summary>
    public ButtonBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>Show the button pressed (the focused style, reversed).</summary>
    public ButtonBuilder Pressed(bool pressed = true) { _pressed = pressed; return this; }

    /// <summary>
    /// The pointer, usually <see cref="RenderContext.Hover"/>. <see cref="Theme.Hover"/> is patched over the button, when it's inside it (1.x spec §17.6).
    /// </summary>
    public ButtonBuilder Hover(Vec2? position) { _hover = position; return this; }

    /// <summary>
    /// Take presses from a state (1.x spec §23.4): the button records where it's drawn for the mouse, and draws
    /// pressed while <see cref="ButtonState.IsDown"/>.
    /// </summary>
    public ButtonBuilder State(ButtonState state) { _state = state; return this; }

    /// <summary>A style patched over the theme's button style.</summary>
    public ButtonBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Fill <paramref name="area"/> with the button.</summary>
    public void Draw(CellBuffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        _state?.Track(area);
        if (area.IsEmpty) return;
        var theme = Theme.Current;
        var style = (_pressed || _state?.IsDown == true ? theme.ButtonFocused.Reversed() : _focused ? theme.ButtonFocused : theme.Button).Patch(_style ?? default);
        WidgetText.Clear(buf, area);
        if (_hover is { } p && area.Contains(p.X, p.Y)) style = style.Patch(theme.Hover);
        buf.SetStyle(area, style);
        WidgetText.DrawString(buf, area.X, area.Y + area.Height / 2, _label, area.Width, style, Alignment.Center);
    }
}
