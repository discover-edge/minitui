namespace MiniTui;

/// <summary>
/// A yes/no question, used by <see cref="OverlayStack.ConfirmAsync"/>. ← → Tab h l switch buttons; Enter picks the
/// selected one; <c>y</c> answers yes; <c>n</c> and Esc answer no.
/// </summary>
public sealed class ConfirmDialog : Overlay
{
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _message;
    private readonly string _yes;
    private readonly string _no;
    private bool _yesSelected = true;

    /// <summary>Create a dialog. Show it with <see cref="OverlayStack.Show"/> and await <see cref="Result"/>.</summary>
    public ConfirmDialog(string message, string yes = "Yes", string no = "No")
    {
        _message = message ?? "";
        _yes = yes ?? "Yes";
        _no = no ?? "No";
    }

    /// <summary>The answer: true for yes; false for no, or when the dialog is closed any other way.</summary>
    public Task<bool> Result => _result.Task;

    /// <inheritdoc/>
    public override string? Title => " Confirm ";

    /// <inheritdoc/>
    public override Rect Placement(Rect screen)
    {
        var width = Math.Max(30, Math.Max(UnicodeWidth.Of(Sanitizer.SingleLine(_message)), ButtonsWidth) + 4);
        var rows = Paragraph.MeasureLines(_message, Math.Max(1, Math.Min(width, screen.Width - 4) - 4), WrapMode.Word);
        return Centered(screen, width, rows + 4);
    }

    /// <inheritdoc/>
    public override bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Tab:
                _yesSelected = !_yesSelected;
                return true;
            case ConsoleKey.Enter:
                Answer(_yesSelected);
                return true;
            case ConsoleKey.Escape:
                Answer(false);
                return true;
        }

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'h' or 'l':
                _yesSelected = !_yesSelected;
                return true;
            case 'y':
                Answer(true);
                return true;
            case 'n':
                Answer(false);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        Widget.Paragraph().Text(new Line(_message) with { Alignment = Alignment.Center }).Wrap(WrapMode.Word)
            .Draw(buf, area with { Height = Math.Max(0, area.Height - 2) });

        var y = area.Bottom - 1;
        var x = area.X + Math.Max(0, (area.Width - ButtonsWidth) / 2);
        var yes = _yesSelected ? ctx.Theme.ButtonFocused : ctx.Theme.Button;
        var no = _yesSelected ? ctx.Theme.Button : ctx.Theme.ButtonFocused;
        var yesWidth = WidgetText.DrawString(buf, x, y, $" {_yes} ", area.Right - x, yes);
        _yesButton = new Rect(x, y, yesWidth, 1);
        x += yesWidth + 4;
        var noWidth = x < area.Right ? WidgetText.DrawString(buf, x, y, $" {_no} ", area.Right - x, no) : 0;
        _noButton = new Rect(x, y, noWidth, 1);
    }

    /// <inheritdoc/>
    public override bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!mouse.IsClick) return false;
        if (_yesButton.Contains(mouse.X, mouse.Y)) Answer(true);
        else if (_noButton.Contains(mouse.X, mouse.Y)) Answer(false);
        else return false;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnMount() => App.BeginUserWait();

    /// <inheritdoc/>
    protected override void OnUnmount()
    {
        App.EndUserWait();
        _result.TrySetResult(false);
    }

    private Rect _yesButton;
    private Rect _noButton;

    private int ButtonsWidth => UnicodeWidth.Of(_yes) + UnicodeWidth.Of(_no) + 8;

    private void Answer(bool yes)
    {
        _result.TrySetResult(yes);
        Close();
    }
}
