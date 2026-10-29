namespace MiniTui;

/// <summary>
/// A dialog centered on the screen, drawn over existing content. The text wraps, and scrolls with a
/// <see cref="ScrollState"/> when it doesn't fit. The dialog leaves a 2-cell margin around it.
/// </summary>
[Obsolete("Use App.Overlays.ShowMessageAsync or a custom Overlay instead.")]
public class ModalBuilder
{
    private const int ScreenMargin = 2;

    private string _title = "";
    private Text _text = new([]);
    private string? _footer = "Press Escape to close";
    private ScrollState? _state;

    /// <summary>Set the modal title.</summary>
    public ModalBuilder Title(string title) { _title = title ?? ""; return this; }

    /// <summary>Set the modal content lines.</summary>
    public ModalBuilder Lines(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _text = new Text([.. lines.Select(l => new Line(l))]);
        return this;
    }

    /// <summary>Set the modal content lines.</summary>
    public ModalBuilder Lines(params string[] lines) => Lines((IReadOnlyList<string>)lines);

    /// <summary>Set the modal content.</summary>
    public ModalBuilder Text(Text text) { _text = text ?? new Text([]); return this; }

    /// <summary>Set the modal footer text (null to hide).</summary>
    public ModalBuilder Footer(string? footer) { _footer = footer; return this; }

    /// <summary>Scroll the content with this state when it overflows.</summary>
    public ModalBuilder State(ScrollState state) { _state = state; return this; }

    /// <summary>Draw the modal centered on the buffer.</summary>
    public void Draw(Buffer buf)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var theme = Theme.Current;
        var maxWidth = buf.Width - 2 * ScreenMargin;
        var maxHeight = buf.Height - 2 * ScreenMargin;
        if (maxWidth < 3 || maxHeight < 3) return;

        // Border plus one column of padding on each side
        var footerWidth = _footer is null ? 0 : UnicodeWidth.Of(Sanitizer.SingleLine(_footer));
        var width = Math.Min(maxWidth, Math.Max(UnicodeWidth.Of(Sanitizer.SingleLine(_title)), Math.Max(_text.Width, footerWidth)) + 4);
        var rows = Paragraph.MeasureLines(_text, Math.Max(0, width - 4), WrapMode.Word);
        var height = Math.Min(maxHeight, rows + 2);

        var area = new Rect((buf.Width - width) / 2, (buf.Height - height) / 2, width, height);
        WidgetText.Clear(buf, area);
        var block = new BlockBuilder()
            .Title(_title)
            .TitleStyle(theme.ModalTitle)
            .BorderStyle(theme.ModalBorder)
            .Padding(new Margin(1, 0));
        var paragraph = new ParagraphBuilder().Text(_text).Wrap(WrapMode.Word).State(_state ?? new ScrollState()).Block(block);
        paragraph.Draw(buf, area);

        if (_footer is not null)
            WidgetText.DrawString(buf, area.X + 1, area.Bottom - 1, _footer, area.Width - 2, theme.TextMuted, Alignment.Center);
    }
}
