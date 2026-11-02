namespace MiniTui;

/// <summary>
/// A scrollable message, used by <see cref="OverlayStack.ShowMessageAsync"/>. Enter or Esc closes it.
/// </summary>
public sealed class MessageDialog : Overlay
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _title;
    private readonly Text _body;
    private readonly ScrollState _scroll = new();

    /// <summary>Create a dialog. Show it with <see cref="OverlayStack.Show"/>.</summary>
    public MessageDialog(string title, Text body)
    {
        _title = title ?? "";
        _body = body ?? new Text([]);
    }

    /// <summary>Completes when the dialog closes.</summary>
    public Task Closed => _closed.Task;

    /// <inheritdoc/>
    public override string? Title => _title.Length == 0 ? null : $" {_title.Trim()} ";

    /// <inheritdoc/>
    public override Rect Placement(Rect screen) => ContentPlacement(screen, _body, UnicodeWidth.Of(Sanitizer.SingleLine(_title)));

    /// <inheritdoc/>
    public override bool HandleKey(ConsoleKeyInfo key)
    {
        if (KeyInput.IsEnter(key))
        {
            Close();
            return true;
        }
        return _scroll.HandleKey(key);
    }

    /// <inheritdoc/>
    public override bool HandleMouse(MouseEvent mouse) => _scroll.HandleMouse(mouse);

    /// <inheritdoc/>
    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        Widget.Paragraph().Text(_body).Wrap(WrapMode.Word).State(_scroll).Draw(buf, area);

    /// <inheritdoc/>
    protected override void OnMount() => App.BeginUserWait();

    /// <inheritdoc/>
    protected override void OnUnmount()
    {
        App.EndUserWait();
        _closed.TrySetResult();
    }

    /// <summary>Sized to the text, within the default bounds: border and padding around it, at least 30 × 7.</summary>
    internal static Rect ContentPlacement(Rect screen, Text body, int titleWidth)
    {
        var width = Math.Max(30, Math.Max(body.Width, titleWidth) + 4);
        var fitted = Math.Min(width, Math.Max(0, screen.Width - 4));
        var rows = Paragraph.MeasureLines(body, Math.Max(1, fitted - 4), WrapMode.Word);
        return Centered(screen, width, Math.Max(7, rows + 2));
    }
}
