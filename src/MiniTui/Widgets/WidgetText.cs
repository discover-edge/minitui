namespace MiniTui;

/// <summary>Width-aware line drawing shared by the widgets (rule W2).</summary>
internal static class WidgetText
{
    public const string Ellipsis = "…";

    /// <summary>
    /// Draw <paramref name="line"/> in the <paramref name="width"/> columns from <paramref name="x"/>, truncated with
    /// <c>…</c> if it doesn't fit and aligned by <paramref name="alignment"/> (or the line's own alignment).
    /// Each span's style is patched over <paramref name="style"/>.
    /// </summary>
    /// <returns>The columns drawn.</returns>
    public static int DrawLine(Buffer buf, int x, int y, Line line, int width, Style style = default, Alignment? alignment = null)
    {
        if (width <= 0) return 0;
        var lineWidth = line.Width;
        if (lineWidth > width)
        {
            line = Truncate(line, width);
            lineWidth = line.Width;
        }

        var offset = (alignment ?? line.Alignment) switch
        {
            Alignment.Center => (width - lineWidth) / 2,
            Alignment.Right => width - lineWidth,
            _ => 0,
        };

        var used = 0;
        foreach (var span in line.Spans)
        {
            if (used >= lineWidth) break;
            used += buf.SetString(x + offset + used, y, span.Content ?? "", style.Patch(span.Style), lineWidth - used);
        }
        return used;
    }

    /// <summary>Draw a string, truncated with <c>…</c> to <paramref name="width"/> columns.</summary>
    public static int DrawString(Buffer buf, int x, int y, string text, int width, Style style, Alignment alignment = Alignment.Left) =>
        DrawLine(buf, x, y, new Line(text), width, style, alignment);

    /// <summary>The line cut to at most <paramref name="width"/> columns, ending in <c>…</c> when anything was cut.</summary>
    public static Line Truncate(Line line, int width)
    {
        if (line.Width <= width) return line;
        if (width <= 0) return new Line([], line.Alignment);

        var budget = width - 1;
        var spans = new List<Span>();
        var used = 0;
        var lastStyle = Style.Default;
        foreach (var span in line.Spans)
        {
            var content = Sanitizer.SingleLine(span.Content ?? "");
            var w = UnicodeWidth.Of(content);
            lastStyle = span.Style;
            if (used + w <= budget)
            {
                spans.Add(span with { Content = content });
                used += w;
                continue;
            }
            var cut = UnicodeWidth.Truncate(content, budget - used, "");
            if (cut.Length > 0) spans.Add(new Span(cut, span.Style));
            break;
        }
        spans.Add(new Span(Ellipsis, lastStyle));
        return new Line(spans, line.Alignment);
    }

    /// <summary>A ─ line, clipped to the buffer.</summary>
    public static void HorizontalLine(Buffer buf, int x, int y, int width, Style style)
    {
        var clip = new Rect(x, y, width, 1).Intersect(buf.Area);
        for (var cx = clip.X; cx < clip.Right; cx++) buf.SetString(cx, y, "─", style, 1);
    }

    /// <summary>Clear the cells in <paramref name="area"/>, for widgets drawn over other content.</summary>
    public static void Clear(Buffer buf, Rect area) => buf.FillRect(area, Cell.Empty);
}
