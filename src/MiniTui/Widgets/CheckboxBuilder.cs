namespace MiniTui;

/// <summary>
/// A checkbox with a label: <c>[✓] Label</c>.
/// </summary>
public sealed class CheckboxBuilder
{
    private string _label = "";
    private bool _checked;
    private bool _focused;
    private Style? _style;

    /// <summary>The label.</summary>
    public CheckboxBuilder Label(string label) { _label = label ?? ""; return this; }

    /// <summary>Whether the box is checked.</summary>
    public CheckboxBuilder Checked(bool isChecked = true) { _checked = isChecked; return this; }

    /// <summary>Use the focused style for the brackets.</summary>
    public CheckboxBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>A style patched over the theme's text style for the label.</summary>
    public CheckboxBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the checkbox on the first row of <paramref name="area"/>.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        if (area.IsEmpty) return;
        var theme = Theme.Current;
        var brackets = _focused ? theme.BorderFocused : theme.Text;
        var right = area.Right;
        var x = area.X;
        x += buf.SetString(x, area.Y, "[", brackets, right - x);
        x += buf.SetString(x, area.Y, _checked ? "✓" : " ", theme.Success, right - x);
        x += buf.SetString(x, area.Y, "]", brackets, right - x);
        if (right - x > 1) WidgetText.DrawString(buf, x + 1, area.Y, _label, right - x - 1, theme.Text.Patch(_style ?? default));
    }
}
