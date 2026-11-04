namespace MiniTui;

/// <summary>
/// A button's presses (1.x spec §23.4): Enter or Space, or a left click released over it, as in GUIs, so a press
/// dragged off it is cancelled. The button drawn with it records where it was drawn and draws pressed while
/// <see cref="IsDown"/>.
/// </summary>
public sealed class ButtonState
{
    private Rect _area;
    private bool _held;

    /// <summary>Raised when the user presses it: Enter or Space, or a left click released over it.</summary>
    public event EventHandler? Pressed;

    /// <summary>True while the left button is held down on it, so it draws pressed.</summary>
    public bool IsDown { get; private set; }

    /// <summary>Handle Enter and Space, without modifiers.</summary>
    /// <returns>True if the key was handled.</returns>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar) || key.Modifiers != 0) return false;
        Pressed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Handle a left press where it was drawn last, the drags after it (down only while over it) and the release,
    /// which raises <see cref="Pressed"/> when it's over it.
    /// </summary>
    /// <returns>True if the event was for the button.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        switch (mouse.Action)
        {
            case MouseAction.Press when mouse.Button == MouseButton.Left:
                _held = IsDown = _area.Contains(mouse.X, mouse.Y);
                return _held;
            case MouseAction.Drag when _held:
                IsDown = _area.Contains(mouse.X, mouse.Y);
                return true;
            case MouseAction.Release when _held:
                var over = _area.Contains(mouse.X, mouse.Y);
                _held = IsDown = false;
                if (over) Pressed?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Called by the button while drawing: its area.</summary>
    internal void Track(Rect area) => _area = area;
}
