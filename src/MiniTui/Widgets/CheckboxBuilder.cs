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
    private Vec2? _hover;
    private CheckboxState? _state;

    /// <summary>The label.</summary>
    public CheckboxBuilder Label(string label) { _label = label ?? ""; return this; }

    /// <summary>Whether the box is checked.</summary>
    public CheckboxBuilder Checked(bool isChecked = true) { _checked = isChecked; return this; }

    /// <summary>Use the focused style for the brackets.</summary>
    public CheckboxBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>
    /// The pointer, usually <see cref="RenderContext.Hover"/>. <see cref="Theme.Hover"/> is patched over the checkbox, when it's on it (1.x spec §17.6).
    /// </summary>
    public CheckboxBuilder Hover(Vec2? position) { _hover = position; return this; }

    /// <summary>
    /// Take the value from a state (1.x spec §23.4), instead of <see cref="Checked"/>: the checkbox records where its
    /// box and label are drawn, for the mouse.
    /// </summary>
    public CheckboxBuilder State(CheckboxState state) { _state = state; return this; }

    /// <summary>A style patched over the theme's text style for the label.</summary>
    public CheckboxBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the checkbox on the first row of <paramref name="area"/>.</summary>
    public void Draw(CellBuffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        if (area.IsEmpty)
        {
            _state?.Track(default);
            return;
        }
        var theme = Theme.Current;
        var isChecked = _state?.Checked ?? _checked;
        var brackets = _focused ? theme.BorderFocused : theme.Text;
        var right = area.Right;
        var x = area.X;
        x += buf.SetString(x, area.Y, "[", brackets, right - x);
        x += buf.SetString(x, area.Y, isChecked ? "✓" : " ", theme.Success, right - x);
        x += buf.SetString(x, area.Y, "]", brackets, right - x);
        if (right - x > 1) x += 1 + WidgetText.DrawString(buf, x + 1, area.Y, _label, right - x - 1, theme.Text.Patch(_style ?? default));
        _state?.Track(new Rect(area.X, area.Y, x - area.X, 1));
        if (_hover is { } p && p.Y == area.Y && p.X >= area.X && p.X < x) buf.SetStyle(new Rect(area.X, area.Y, x - area.X, 1), theme.Hover);
    }
}
