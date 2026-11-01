using System.Globalization;

namespace MiniTui;

/// <summary>Drawing shared by TextField, CommandLine and the obsolete input widgets.</summary>
internal static class TextFieldRenderer
{
    public static void Draw(Buffer buf, Rect area, TextFieldState state, bool focused, Style style, string? placeholder)
    {
        if (area.IsEmpty) return;
        var theme = Theme.Current;
        var y = area.Y;
        var width = area.Width;
        buf.SetStyle(area with { Height = 1 }, style);

        var text = Sanitizer.SingleLine(state.Text);
        var cursorColumn = UnicodeWidth.Of(Sanitizer.SingleLine(state.Text.AsSpan(0, state.Cursor)));
        var textWidth = UnicodeWidth.Of(text);

        // Scroll by display width so the cursor stays in view, with a column for it at the end
        var scroll = state.ScrollOffset;
        if (textWidth < width) scroll = 0;
        if (cursorColumn < scroll) scroll = cursorColumn;
        if (cursorColumn >= scroll + width) scroll = cursorColumn - width + 1;
        scroll = Math.Clamp(scroll, 0, Math.Max(0, textWidth - width + 1));
        state.ScrollOffset = scroll;

        if (text.Length == 0 && !string.IsNullOrEmpty(placeholder))
            WidgetText.DrawString(buf, area.X, y, placeholder, width, style.Patch(theme.TextMuted));
        else
            DrawScrolled(buf, area.X, y, text, scroll, width, style);

        // 1.x spec §10.8: the selection, by display column, under the cursor
        if (state.HasSelection)
        {
            var from = UnicodeWidth.Of(Sanitizer.SingleLine(state.Text.AsSpan(0, state.SelectionStart))) - scroll;
            var to = UnicodeWidth.Of(Sanitizer.SingleLine(state.Text.AsSpan(0, state.SelectionEnd))) - scroll;
            from = Math.Max(from, 0);
            to = Math.Min(to, width);
            if (to > from) buf.SetStyle(new Rect(area.X + from, y, to - from, 1), focused ? theme.Selection : theme.SelectionUnfocused);
        }

        if (!focused) return;
        var cursorX = area.X + cursorColumn - scroll;
        if (cursorX < area.X || cursorX >= area.Right) return;
        buf.SetStyle(new Rect(cursorX, y, 1, 1), theme.Cursor);
        buf.SetCursor(cursorX, y);
    }

    private static void DrawScrolled(Buffer buf, int x, int y, string text, int scroll, int width, Style style)
    {
        var column = 0;
        for (var i = 0; i < text.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(text, i);
            var grapheme = text.AsSpan(i, length);
            i += length;
            var w = UnicodeWidth.OfGrapheme(grapheme);
            var visibleStart = column - scroll;
            column += w;
            if (column <= scroll) continue;
            if (visibleStart < 0) continue; // a wide character cut by the left edge stays blank
            if (visibleStart + w > width) break;
            buf.SetString(x + visibleStart, y, grapheme, style, w);
        }
    }
}
