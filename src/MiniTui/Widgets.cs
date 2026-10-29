namespace MiniTui;

/// <summary>
/// Collection of widget-drawing functions. These are stateless -
/// they just draw to a buffer based on parameters you provide.
/// </summary>
/// <remarks>Each function draws through the matching <see cref="Widget"/> builder.</remarks>
public static class Widgets
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
    /// Draw a text input field.
    /// </summary>
    [Obsolete("Use Widget.TextField() with a TextFieldState instead.")]
    public static void TextInput(Buffer buf, Rect area, string value, bool focused = false, int cursorPos = -1)
    {
        var state = new TextFieldState(value ?? "");
        if (cursorPos >= 0) state.Cursor = cursorPos;
        new TextFieldBuilder().State(state).Focused(focused && cursorPos >= 0).Block(new BlockBuilder().Focused(focused)).Draw(buf, area);
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
    /// Draw a command input field with prompt.
    /// </summary>
    [Obsolete("Use Widget.CommandLine() with a CommandLineState instead.")]
    public static void CommandInput(Buffer buf, Rect area, string value, string prompt = "> ", bool focused = true, int cursorPos = -1)
    {
        var state = new CommandLineState { Text = value ?? "" };
        if (cursorPos >= 0) state.Cursor = cursorPos;
        new CommandLineBuilder().State(state).Prompt(prompt).Focused(focused).Draw(buf, area);
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

    /// <summary>
    /// Draw a tab bar. Returns the height consumed (typically 2 - tabs + separator).
    /// </summary>
    [Obsolete("Use Widget.TabBar() with a TabBarState instead.")]
    public static int Tabs(Buffer buf, Rect area, IReadOnlyList<string> labels, int selectedIndex, bool focused = false)
    {
        ArgumentNullException.ThrowIfNull(labels);
        new TabBarBuilder().Tabs([.. labels]).State(new TabBarState(labels.Count) { Selected = selectedIndex }).Focused(focused).Draw(buf, area);
        return 2;
    }
}

/// <summary>
/// Fluent builder for CommandInput widget.
/// </summary>
[Obsolete("Use Widget.CommandLine() with a CommandLineState instead.")]
public class CommandInputBuilder
{
    private string _value = "";
    private string _prompt = "> ";
    private bool _focused = true;
    private int _cursorPos = -1;

    /// <summary>Set the input value.</summary>
    public CommandInputBuilder Value(string value) { _value = value; return this; }
    /// <summary>Set state from a CommandInputState.</summary>
    public CommandInputBuilder State(CommandInputState state) { _value = state.Value; _cursorPos = state.CursorPos; return this; }
    /// <summary>Set the prompt string.</summary>
    public CommandInputBuilder Prompt(string prompt) { _prompt = prompt; return this; }
    /// <summary>Set whether the input is focused.</summary>
    public CommandInputBuilder Focused(bool focused = true) { _focused = focused; return this; }
    /// <summary>Set the cursor position.</summary>
    public CommandInputBuilder CursorPos(int pos) { _cursorPos = pos; return this; }

    /// <summary>Draw the command input to the buffer.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        Widgets.CommandInput(buf, area, _value, _prompt, _focused, _cursorPos);
    }
}

/// <summary>
/// Fluent builder for Tabs widget.
/// </summary>
[Obsolete("Use Widget.TabBar() with a TabBarState instead.")]
public class TabsBuilder
{
    private readonly List<string> _labels = [];
    private Func<TabsState>? _stateGetter;
    private bool _focused;

    /// <summary>Add a tab with the given label.</summary>
    public TabsBuilder Tab(string label) { _labels.Add(label); return this; }
    /// <summary>Set state from a getter function.</summary>
    public TabsBuilder State(Func<TabsState> getter) { _stateGetter = getter; return this; }
    /// <summary>Set whether the tabs are focused.</summary>
    public TabsBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>Draw the tabs to the buffer. Returns height consumed.</summary>
    public int Draw(Buffer buf, Rect area)
    {
        var state = _stateGetter?.Invoke() ?? TabsState.Create(_labels.Count);
        return Widgets.Tabs(buf, area, _labels, state.SelectedIndex, _focused);
    }
}

/// <summary>
/// Factory methods for widget builders.
/// </summary>
public static class Widget
{
    /// <summary>A border with titles and padding.</summary>
    public static BlockBuilder Block() => new();
    /// <summary>Wrapped, aligned, scrollable text.</summary>
    public static ParagraphBuilder Paragraph() => new();
    /// <summary>Create a new list widget builder.</summary>
    public static ListBuilder List() => new();
    /// <summary>A table with constraint-sized columns.</summary>
    public static TableBuilder Table() => new();
    /// <summary>A scrollbar.</summary>
    public static ScrollbarBuilder Scrollbar() => new();
    /// <summary>A progress bar.</summary>
    public static GaugeBuilder Gauge() => new();
    /// <summary>A bar chart of recent values.</summary>
    public static SparklineBuilder Sparkline() => new();
    /// <summary>An activity indicator.</summary>
    public static SpinnerBuilder Spinner() => new();
    /// <summary>Pop-up messages from a <see cref="ToastQueue"/>.</summary>
    public static ToastsBuilder Toasts() => new();
    /// <summary>Create a new status bar widget builder.</summary>
    public static StatusBarBuilder StatusBar() => new();
    /// <summary>A single-line text input.</summary>
    public static TextFieldBuilder TextField() => new();
    /// <summary>A command input with a prompt and history.</summary>
    public static CommandLineBuilder CommandLine() => new();
    /// <summary>A row of tabs.</summary>
    public static TabBarBuilder TabBar() => new();
    /// <summary>A button.</summary>
    public static ButtonBuilder Button() => new();
    /// <summary>A checkbox.</summary>
    public static CheckboxBuilder Checkbox() => new();
    /// <summary>Create a new command input widget builder.</summary>
    [Obsolete("Use Widget.CommandLine() instead.")]
    public static CommandInputBuilder CommandInput() => new();
    /// <summary>Create a new modal widget builder.</summary>
    public static ModalBuilder Modal() => new();
    /// <summary>Create a new confirmation dialog builder.</summary>
    public static ConfirmBuilder Confirm() => new();
    /// <summary>Create a new tabs widget builder.</summary>
    [Obsolete("Use Widget.TabBar() instead.")]
    public static TabsBuilder Tabs() => new();
}
