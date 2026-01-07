namespace MiniTui;

/// <summary>
/// Collection of widget-drawing functions. These are stateless - 
/// they just draw to a buffer based on parameters you provide.
/// </summary>
public static class Widgets
{
    /// <summary>
    /// Draw a button. Returns the inner rect if you need it.
    /// </summary>
    public static Rect Button(Buffer buf, Rect area, string label, bool focused = false, bool pressed = false)
    {
        var fg = focused ? ConsoleColor.Black : ConsoleColor.White;
        var bg = focused ? ConsoleColor.Cyan : ConsoleColor.DarkGray;

        if (pressed)
        {
            fg = ConsoleColor.Black;
            bg = ConsoleColor.White;
        }

        // Fill background
        buf.FillRect(area, new Cell(' ', fg, bg));

        // Center the label
        var x = area.X + (area.Width - label.Length) / 2;
        var y = area.Y + area.Height / 2;
        buf.DrawText(x, y, label, fg, bg);

        return area;
    }

    /// <summary>
    /// Draw a text input field.
    /// </summary>
    public static void TextInput(Buffer buf, Rect area, string value, bool focused = false, int cursorPos = -1)
    {
        var fg = focused ? ConsoleColor.White : ConsoleColor.Gray;
        var borderFg = focused ? ConsoleColor.Cyan : ConsoleColor.DarkGray;

        buf.DrawBox(area, borderFg);
        
        var inner = area.Shrink(1, 0);
        inner = new Rect(inner.X, area.Y + 1, inner.Width, 1);

        // Draw value (scrolled if needed)
        var displayWidth = inner.Width;
        var displayValue = value;
        var offset = 0;

        if (value.Length > displayWidth)
        {
            offset = Math.Max(0, (cursorPos >= 0 ? cursorPos : value.Length) - displayWidth + 1);
            displayValue = value.Substring(offset, Math.Min(displayWidth, value.Length - offset));
        }

        buf.DrawText(inner.X, inner.Y, displayValue.PadRight(displayWidth), fg);

        // Draw cursor
        if (focused && cursorPos >= 0)
        {
            var cursorX = inner.X + cursorPos - offset;
            if (cursorX >= inner.X && cursorX < inner.Right)
            {
                var c = cursorPos < value.Length ? value[cursorPos] : ' ';
                buf.Set(cursorX, inner.Y, new Cell(c, ConsoleColor.Black, ConsoleColor.White));
            }
        }
    }

    /// <summary>
    /// Draw a labeled checkbox.
    /// </summary>
    public static void Checkbox(Buffer buf, int x, int y, string label, bool isChecked, bool focused = false)
    {
        var checkChar = isChecked ? '✓' : ' ';
        var bracketFg = focused ? ConsoleColor.Cyan : ConsoleColor.Gray;
        var checkFg = isChecked ? ConsoleColor.Green : ConsoleColor.Gray;
        var labelFg = focused ? ConsoleColor.White : ConsoleColor.Gray;

        buf.Set(x, y, new Cell('[', bracketFg, ConsoleColor.Black));
        buf.Set(x + 1, y, new Cell(checkChar, checkFg, ConsoleColor.Black));
        buf.Set(x + 2, y, new Cell(']', bracketFg, ConsoleColor.Black));
        buf.DrawText(x + 4, y, label, labelFg);
    }

    /// <summary>
    /// Draw a list of selectable items.
    /// </summary>
    public static void List(Buffer buf, Rect area, IReadOnlyList<string> items, int selectedIndex, int scrollOffset = 0, bool focused = false, string? title = null)
    {
        var borderFg = focused ? ConsoleColor.Cyan : ConsoleColor.DarkGray;
        var titleFg = focused ? ConsoleColor.Yellow : ConsoleColor.Cyan;

        if (title != null)
            buf.DrawBoxWithTitle(area, title, borderFg, titleFg);
        else
            buf.DrawBox(area, borderFg);

        var inner = area.Shrink(1);
        var visibleCount = Math.Min(items.Count - scrollOffset, inner.Height);

        for (var i = 0; i < visibleCount; i++)
        {
            var itemIndex = scrollOffset + i;
            var isSelected = focused && itemIndex == selectedIndex;

            var fg = isSelected ? ConsoleColor.Black : ConsoleColor.Gray;
            var bg = isSelected ? ConsoleColor.Cyan : ConsoleColor.Black;

            var text = items[itemIndex];
            if (text.Length > inner.Width)
                text = text[..(inner.Width - 1)] + "…";

            // Fill entire line with background
            for (var x = 0; x < inner.Width; x++)
            {
                var c = x < text.Length ? text[x] : ' ';
                buf.Set(inner.X + x, inner.Y + i, new Cell(c, fg, bg));
            }
        }

        // Fill empty lines
        for (var i = visibleCount; i < inner.Height; i++)
        {
            for (var x = 0; x < inner.Width; x++)
                buf.Set(inner.X + x, inner.Y + i, new Cell(' ', ConsoleColor.Gray, ConsoleColor.Black));
        }

        // Draw scrollbar if needed
        if (items.Count > inner.Height)
        {
            var scrollbarHeight = Math.Max(1, inner.Height * inner.Height / items.Count);
            var scrollbarPos = items.Count > inner.Height
                ? scrollOffset * (inner.Height - scrollbarHeight) / (items.Count - inner.Height)
                : 0;

            for (var y = 0; y < inner.Height; y++)
            {
                var c = (y >= scrollbarPos && y < scrollbarPos + scrollbarHeight) ? '█' : '░';
                buf.Set(area.Right - 1, inner.Y + y, new Cell(c, ConsoleColor.DarkGray, ConsoleColor.Black));
            }
        }
    }

    /// <summary>
    /// Draw a simple table.
    /// </summary>
    public static void Table(Buffer buf, Rect area, string[] headers, string[][] rows, int selectedRow = -1)
    {
        if (headers.Length == 0) return;

        var colWidth = area.Width / headers.Length;
        
        // Draw headers
        for (var col = 0; col < headers.Length; col++)
        {
            var x = area.X + col * colWidth;
            var header = headers[col].Length > colWidth - 1 
                ? headers[col][..(colWidth - 2)] + "…" 
                : headers[col];
            buf.DrawText(x, area.Y, header, ConsoleColor.Yellow);
        }

        // Draw separator
        buf.DrawHLine(area.X, area.Y + 1, area.Width, '─', ConsoleColor.DarkGray);

        // Draw rows
        var maxRows = Math.Min(rows.Length, area.Height - 2);
        for (var row = 0; row < maxRows; row++)
        {
            var y = area.Y + 2 + row;
            var isSelected = row == selectedRow;
            var fg = isSelected ? ConsoleColor.Black : ConsoleColor.Gray;
            var bg = isSelected ? ConsoleColor.Cyan : ConsoleColor.Black;

            // Fill row background if selected
            if (isSelected)
                buf.FillRect(new Rect(area.X, y, area.Width, 1), new Cell(' ', fg, bg));

            for (var col = 0; col < Math.Min(headers.Length, rows[row].Length); col++)
            {
                var x = area.X + col * colWidth;
                var cell = rows[row][col];
                if (cell.Length > colWidth - 1)
                    cell = cell[..(colWidth - 2)] + "…";
                buf.DrawText(x, y, cell, fg, bg);
            }
        }
    }

    /// <summary>
    /// Draw a status bar at the bottom of a rect.
    /// </summary>
    public static void StatusBar(Buffer buf, Rect area, string left, string? right = null)
    {
        var y = area.Bottom - 1;

        // Fill background
        for (var x = area.X; x < area.Right; x++)
            buf.Set(x, y, new Cell(' ', ConsoleColor.Black, ConsoleColor.DarkGray));

        buf.DrawText(area.X + 1, y, left, ConsoleColor.White, ConsoleColor.DarkGray);

        if (right != null)
        {
            var x = area.Right - right.Length - 1;
            buf.DrawText(x, y, right, ConsoleColor.White, ConsoleColor.DarkGray);
        }
    }

    /// <summary>
    /// Draw a command input field with prompt.
    /// </summary>
    public static void CommandInput(Buffer buf, Rect area, string value, string prompt = "> ", bool focused = true, int cursorPos = -1)
    {
        var fg = focused ? ConsoleColor.White : ConsoleColor.Gray;
        var promptFg = ConsoleColor.Cyan;
        var bg = ConsoleColor.Black;

        // Fill background
        buf.FillRect(area, new Cell(' ', fg, bg));

        // Draw prompt
        buf.DrawText(area.X, area.Y, prompt, promptFg, bg);

        // Calculate available width for text
        var textX = area.X + prompt.Length;
        var textWidth = area.Width - prompt.Length;

        // Draw value (scrolled if needed)
        var displayValue = value;
        var offset = 0;

        if (value.Length > textWidth)
        {
            offset = Math.Max(0, (cursorPos >= 0 ? cursorPos : value.Length) - textWidth + 1);
            displayValue = value.Substring(offset, Math.Min(textWidth, value.Length - offset));
        }

        buf.DrawText(textX, area.Y, displayValue, fg, bg);

        // Draw cursor
        if (focused)
        {
            var actualCursorPos = cursorPos >= 0 ? cursorPos : value.Length;
            var cursorX = textX + actualCursorPos - offset;
            if (cursorX >= textX && cursorX < area.Right)
            {
                var c = actualCursorPos < value.Length ? value[actualCursorPos] : ' ';
                buf.Set(cursorX, area.Y, new Cell(c, ConsoleColor.Black, ConsoleColor.White));
            }
        }
    }

    /// <summary>
    /// Draw a modal dialog centered on screen. Draws on top of existing content.
    /// </summary>
    public static void Modal(Buffer buf, string title, IReadOnlyList<string> lines, string? footer = "Press Escape to close")
    {
        // Calculate modal size
        var contentWidth = lines.Count > 0 ? lines.Max(l => l.Length) : 0;
        var width = Math.Max(title.Length + 4, Math.Max(contentWidth + 4, (footer?.Length ?? 0) + 4));
        width = Math.Min(width, buf.Width - 4); // Leave margin
        var height = lines.Count + 4; // +2 for border, +1 for title line, +1 for footer
        height = Math.Min(height, buf.Height - 2);

        // Center on screen
        var x = (buf.Width - width) / 2;
        var y = (buf.Height - height) / 2;
        var area = new Rect(x, y, width, height);

        // Draw shadow
        for (var sy = area.Y + 1; sy < area.Bottom + 1 && sy < buf.Height; sy++)
        {
            for (var sx = area.X + 1; sx < area.Right + 1 && sx < buf.Width; sx++)
            {
                var existing = buf.Get(sx, sy);
                buf.Set(sx, sy, new Cell(existing.Char, ConsoleColor.DarkGray, ConsoleColor.Black));
            }
        }

        // Fill background
        buf.FillRect(area, new Cell(' ', ConsoleColor.Gray, ConsoleColor.Black));

        // Draw border with title
        buf.DrawBoxWithTitle(area, title, ConsoleColor.DarkGray, ConsoleColor.Cyan, BoxStyle.Single);

        // Draw content
        var inner = area.Shrink(1);
        var visibleLines = Math.Min(lines.Count, inner.Height - 1); // -1 for footer
        for (var i = 0; i < visibleLines; i++)
        {
            var line = lines[i];
            if (line.Length > inner.Width)
                line = line[..(inner.Width - 1)] + "…";
            buf.DrawText(inner.X, inner.Y + i, line, ConsoleColor.Gray, ConsoleColor.Black);
        }

        // Draw footer
        if (footer != null)
        {
            var footerY = area.Bottom - 1;
            var footerX = area.X + (area.Width - footer.Length) / 2;
            buf.DrawText(footerX, footerY, footer, ConsoleColor.DarkGray, ConsoleColor.Black);
        }
    }
}

/// <summary>
/// Fluent builder for List widget.
/// </summary>
public class ListBuilder
{
    private IReadOnlyList<string> _items = [];
    private int _selectedIndex;
    private int _scrollOffset;
    private bool _focused;
    private string? _title;

    /// <summary>Set the list items to display.</summary>
    public ListBuilder Items(IReadOnlyList<string> items) { _items = items; return this; }
    /// <summary>Set state from a ListState.</summary>
    public ListBuilder State(ListState state) { _selectedIndex = state.SelectedIndex; _scrollOffset = state.ScrollOffset; return this; }
    /// <summary>Set the selected item index.</summary>
    public ListBuilder Selected(int index) { _selectedIndex = index; return this; }
    /// <summary>Set the scroll offset.</summary>
    public ListBuilder ScrollOffset(int offset) { _scrollOffset = offset; return this; }
    /// <summary>Set whether the list is focused.</summary>
    public ListBuilder Focused(bool focused = true) { _focused = focused; return this; }
    /// <summary>Set the list title.</summary>
    public ListBuilder Title(string title) { _title = title; return this; }

    /// <summary>Draw the list to the buffer.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        Widgets.List(buf, area, _items, _selectedIndex, _scrollOffset, _focused, _title);
    }
}

/// <summary>
/// Fluent builder for CommandInput widget.
/// </summary>
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
/// Fluent builder for StatusBar widget.
/// </summary>
public class StatusBarBuilder
{
    private string _left = "";
    private string? _right;

    /// <summary>Set the left-aligned text.</summary>
    public StatusBarBuilder Left(string text) { _left = text; return this; }
    /// <summary>Set the right-aligned text.</summary>
    public StatusBarBuilder Right(string text) { _right = text; return this; }
    /// <summary>Set left text from a timed message with fallback.</summary>
    public StatusBarBuilder Message(TimedMessage message, string defaultText) { _left = message.GetOrDefault(defaultText); return this; }

    /// <summary>Draw the status bar to the buffer.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        Widgets.StatusBar(buf, area, _left, _right);
    }
}

/// <summary>
/// Fluent builder for Modal widget.
/// </summary>
public class ModalBuilder
{
    private string _title = "";
    private IReadOnlyList<string> _lines = [];
    private string? _footer = "Press Escape to close";

    /// <summary>Set the modal title.</summary>
    public ModalBuilder Title(string title) { _title = title; return this; }
    /// <summary>Set the modal content lines.</summary>
    public ModalBuilder Lines(IReadOnlyList<string> lines) { _lines = lines; return this; }
    /// <summary>Set the modal content lines.</summary>
    public ModalBuilder Lines(params string[] lines) { _lines = lines; return this; }
    /// <summary>Set the modal footer text (null to hide).</summary>
    public ModalBuilder Footer(string? footer) { _footer = footer; return this; }

    /// <summary>Draw the modal centered on the buffer.</summary>
    public void Draw(Buffer buf)
    {
        Widgets.Modal(buf, _title, _lines, _footer);
    }
}

/// <summary>
/// Factory methods for widget builders.
/// </summary>
public static class Widget
{
    /// <summary>Create a new list widget builder.</summary>
    public static ListBuilder List() => new();
    /// <summary>Create a new command input widget builder.</summary>
    public static CommandInputBuilder CommandInput() => new();
    /// <summary>Create a new status bar widget builder.</summary>
    public static StatusBarBuilder StatusBar() => new();
    /// <summary>Create a new modal widget builder.</summary>
    public static ModalBuilder Modal() => new();
}
