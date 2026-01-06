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
    public static void List(Buffer buf, Rect area, IReadOnlyList<string> items, int selectedIndex, int scrollOffset = 0)
    {
        buf.DrawBox(area, ConsoleColor.Gray);
        var inner = area.Shrink(1);

        var visibleCount = Math.Min(items.Count - scrollOffset, inner.Height);

        for (var i = 0; i < visibleCount; i++)
        {
            var itemIndex = scrollOffset + i;
            var isSelected = itemIndex == selectedIndex;

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
}
