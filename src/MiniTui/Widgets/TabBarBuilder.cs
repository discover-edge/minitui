namespace MiniTui;

/// <summary>
/// A row of tabs. When they don't fit, the bar scrolls to keep the selected tab visible.
/// </summary>
public sealed class TabBarBuilder
{
    private readonly List<string> _labels = [];
    private TabBarState? _state;
    private bool _focused;
    private Style? _style;

    /// <summary>Add tabs.</summary>
    public TabBarBuilder Tabs(params string[] labels) { _labels.AddRange(labels ?? []); return this; }

    /// <summary>Add a tab.</summary>
    public TabBarBuilder Tab(string label) { _labels.Add(label ?? ""); return this; }

    /// <summary>The selected tab. The bar writes the tab count back.</summary>
    public TabBarBuilder State(TabBarState state) { _state = state; return this; }

    /// <summary>Use the focused style for the selected tab.</summary>
    public TabBarBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>A style patched over the theme's tab styles.</summary>
    public TabBarBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the tabs on the first row, and a separator line on the second if there's room.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        _state?.Fit(_labels.Count);
        if (area.IsEmpty) return;

        var theme = Theme.Current;
        var selected = _state?.Selected ?? 0;
        var labels = _labels.ConvertAll(l => $"[ {l} ]");
        var widths = labels.ConvertAll(l => UnicodeWidth.Of(Sanitizer.SingleLine(l)));

        // Start late enough that the selected tab fits
        var first = 0;
        while (first < selected && TotalWidth(widths, first, selected) > area.Width) first++;

        var x = area.X;
        for (var i = first; i < labels.Count && x < area.Right; i++)
        {
            var style = (i != selected ? theme.TabInactive : _focused ? theme.TabActive : theme.TabActiveUnfocused).Patch(_style ?? default);
            x += WidgetText.DrawString(buf, x, area.Y, labels[i], area.Right - x, style) + 1;
        }

        if (area.Height > 1)
            WidgetText.HorizontalLine(buf, area.X, area.Y + 1, area.Width, theme.Border);
    }

    private static int TotalWidth(List<int> widths, int from, int to)
    {
        var total = 0;
        for (var i = from; i <= to && i < widths.Count; i++) total += widths[i] + (i > from ? 1 : 0);
        return total;
    }
}
