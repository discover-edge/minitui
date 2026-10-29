namespace MiniTui;

/// <summary>
/// A button with a centered label.
/// </summary>
public sealed class ButtonBuilder
{
    private string _label = "";
    private bool _focused;
    private bool _pressed;
    private Style? _style;

    /// <summary>The label.</summary>
    public ButtonBuilder Label(string label) { _label = label ?? ""; return this; }

    /// <summary>Use the focused style.</summary>
    public ButtonBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>Show the button pressed (the focused style, reversed).</summary>
    public ButtonBuilder Pressed(bool pressed = true) { _pressed = pressed; return this; }

    /// <summary>A style patched over the theme's button style.</summary>
    public ButtonBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Fill <paramref name="area"/> with the button.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        if (area.IsEmpty) return;
        var theme = Theme.Current;
        var style = (_pressed ? theme.ButtonFocused.Reversed() : _focused ? theme.ButtonFocused : theme.Button).Patch(_style ?? default);
        WidgetText.Clear(buf, area);
        buf.SetStyle(area, style);
        WidgetText.DrawString(buf, area.X, area.Y + area.Height / 2, _label, area.Width, style, Alignment.Center);
    }
}
