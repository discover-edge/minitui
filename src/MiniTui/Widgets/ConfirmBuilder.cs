namespace MiniTui;

/// <summary>
/// A yes/no dialog centered on the screen.
/// </summary>
internal sealed class ConfirmBuilder
{
    private string _message = "Are you sure?";
    private string _yesText = "Yes";
    private string _noText = "No";
    private bool _yesSelected = true;

    /// <summary>Set the confirmation message.</summary>
    public ConfirmBuilder Message(string message) { _message = message ?? ""; return this; }
    /// <summary>Set the Yes button text.</summary>
    public ConfirmBuilder YesText(string text) { _yesText = text ?? ""; return this; }
    /// <summary>Set the No button text.</summary>
    public ConfirmBuilder NoText(string text) { _noText = text ?? ""; return this; }
    /// <summary>Set which button is selected.</summary>
    public ConfirmBuilder Selected(bool yesSelected) { _yesSelected = yesSelected; return this; }

    /// <summary>Draw the confirmation dialog centered on the buffer.</summary>
    public void Draw(Buffer buf)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var theme = Theme.Current;
        var maxWidth = buf.Width - 4;
        if (maxWidth < 3 || buf.Height < 3) return;

        var yes = $" {_yesText} ";
        var no = $" {_noText} ";
        var buttonsWidth = UnicodeWidth.Of(yes) + 4 + UnicodeWidth.Of(no);
        var width = Math.Min(maxWidth, Math.Max(UnicodeWidth.Of(Sanitizer.SingleLine(_message)), buttonsWidth) + 4);
        var message = new Line(_message, default) with { Alignment = Alignment.Center };
        var messageRows = Paragraph.MeasureLines(message, Math.Max(0, width - 4), WrapMode.Word);
        var height = Math.Min(buf.Height - 2, messageRows + 4);

        var area = new Rect((buf.Width - width) / 2, (buf.Height - height) / 2, width, height);
        WidgetText.Clear(buf, area);
        var inner = new BlockBuilder().BorderStyle(theme.ModalBorder).Padding(new Margin(1, 0)).Draw(buf, area);
        if (inner.IsEmpty) return;

        new ParagraphBuilder().Text(message).Wrap(WrapMode.Word)
            .Draw(buf, inner with { Height = Math.Max(0, inner.Height - 2) });

        var y = inner.Bottom - 1;
        var x = inner.X + Math.Max(0, (inner.Width - buttonsWidth) / 2);
        var yesStyle = _yesSelected ? theme.ButtonFocused : theme.Button;
        var noStyle = _yesSelected ? theme.Button : theme.ButtonFocused;
        x += WidgetText.DrawString(buf, x, y, yes, inner.Right - x, yesStyle) + 4;
        if (x < inner.Right) WidgetText.DrawString(buf, x, y, no, inner.Right - x, noStyle);
    }
}
