namespace MiniTui;

/// <summary>
/// A one-row bar with left- and right-aligned text.
/// </summary>
public class StatusBarBuilder
{
    private Line _left = "";
    private Line? _right;
    private Style? _style;

    /// <summary>Set the left-aligned text. It's truncated when there isn't room for both sides.</summary>
    public StatusBarBuilder Left(Line text) { _left = text ?? ""; return this; }

    /// <summary>Set the right-aligned text.</summary>
    public StatusBarBuilder Right(Line text) { _right = text; return this; }

    /// <summary>Set left text from a timed message with fallback.</summary>
    public StatusBarBuilder Message(TimedMessage message, string defaultText) { _left = message.GetOrDefault(defaultText); return this; }

    /// <summary>A style patched over the theme's status bar style.</summary>
    public StatusBarBuilder Style(Style style) { _style = style; return this; }

    /// <summary>
    /// Draw the status bar: on the area's only row if it is one row high, otherwise on its bottom row.
    /// </summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        if (area.IsEmpty) return;

        var y = area.Height == 1 ? area.Y : area.Bottom - 1;
        var row = new Rect(area.X, y, area.Width, 1);
        WidgetText.Clear(buf, row);
        var style = Theme.Current.StatusBar.Patch(_style ?? default);
        buf.SetStyle(row, style);

        // One column of padding on each side; the right text wins when both don't fit
        var available = area.Width - 2;
        if (available <= 0) return;
        var rightWidth = _right is null ? 0 : Math.Min(_right.Width, available);
        if (_right is not null)
            WidgetText.DrawLine(buf, area.Right - 1 - rightWidth, y, _right, rightWidth, style, Alignment.Left);

        var leftWidth = available - rightWidth - (rightWidth > 0 ? 1 : 0);
        WidgetText.DrawLine(buf, area.X + 1, y, _left, leftWidth, style, Alignment.Left);
    }
}
