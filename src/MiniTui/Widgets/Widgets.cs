namespace MiniTui;

/// <summary>
/// Collection of widget-drawing functions. These are stateless -
/// they just draw to a buffer based on parameters you provide.
/// </summary>
/// <remarks>Each function draws through the matching <see cref="Widget"/> builder.</remarks>
internal static class Widgets
{
    /// <summary>
    /// Draw a button. Returns the inner rect if you need it.
    /// </summary>
    public static Rect Button(Buffer buf, Rect area, string label, bool focused = false, bool pressed = false)
    {
        new ButtonBuilder().Label(label).Focused(focused).Pressed(pressed).Draw(buf, area);
        return area;
    }

    /// <summary>
    /// Draw a labeled checkbox.
    /// </summary>
    public static void Checkbox(Buffer buf, int x, int y, string label, bool isChecked, bool focused = false)
    {
        ArgumentNullException.ThrowIfNull(buf);
        new CheckboxBuilder().Label(label).Checked(isChecked).Focused(focused).Draw(buf, new Rect(x, y, buf.Width - x, 1));
    }

    /// <summary>
    /// Draw a list of selectable items.
    /// </summary>
    public static void List(Buffer buf, Rect area, IReadOnlyList<string> items, int selectedIndex, int scrollOffset = 0, bool focused = false, string? title = null, bool showScrollInfo = true)
    {
        var list = new ListBuilder().Items(items).Focused(focused).ShowScrollInfo(showScrollInfo).ScrollOffset(scrollOffset);
        if (selectedIndex >= 0) list.Selected(selectedIndex);
        if (title is not null) list.Title(title);
        list.Draw(buf, area);
    }

    /// <summary>
    /// Draw a simple table: equal-width columns, a header with a line under it, and the selected row
    /// scrolled into view. Use <see cref="Widget.Table"/> for column widths and styled cells.
    /// </summary>
    public static void Table(Buffer buf, Rect area, string[] headers, string[][] rows, int selectedRow = -1)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        if (headers.Length == 0) return;

        var table = new TableBuilder()
            .Header([.. headers.Select(h => new Line(h))])
            .Rows(rows, r => new Row([.. r.Select(c => new Line(c))]))
            .HeaderSeparator()
            .Focused();
        if (selectedRow >= 0) table.State(new SelectionState { Selected = selectedRow });
        table.Draw(buf, area);
    }

    /// <summary>
    /// Draw a status bar at the bottom of a rect.
    /// </summary>
    public static void StatusBar(Buffer buf, Rect area, string left, string? right = null)
    {
        var bar = new StatusBarBuilder().Left(left ?? "");
        if (right is not null) bar.Right(right);
        bar.Draw(buf, area);
    }

    /// <summary>
    /// Draw a modal dialog centered on screen. Draws on top of existing content.
    /// </summary>
    public static void Modal(Buffer buf, string title, IReadOnlyList<string> lines, string? footer = "Press Escape to close") =>
        new ModalBuilder().Title(title).Lines(lines).Footer(footer).Draw(buf);

    /// <summary>
    /// Draw a confirmation dialog centered on the screen.
    /// </summary>
    public static void Confirm(Buffer buf, string message, string yesText = "Yes", string noText = "No", bool yesSelected = true) =>
        new ConfirmBuilder().Message(message).YesText(yesText).NoText(noText).Selected(yesSelected).Draw(buf);
}
