namespace MiniTui;

/// <summary>
/// A checkbox's value, toggled by Space and by a click on its box or label (1.x spec §23.4). The checkbox drawn with
/// it records where it was drawn, for the mouse.
/// </summary>
public sealed class CheckboxState
{
    private Rect _area;

    /// <summary>Whether it's checked. Setting it doesn't raise <see cref="Changed"/>.</summary>
    public bool Checked { get; set; }

    /// <summary>Flip <see cref="Checked"/>, without raising <see cref="Changed"/>.</summary>
    public void Toggle() => Checked = !Checked;

    /// <summary>Raised when the user toggles it (Space, or a click on the box or its label); not when code sets <see cref="Checked"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Handle Space, without modifiers.</summary>
    /// <returns>True if the key was handled.</returns>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key != ConsoleKey.Spacebar || key.Modifiers != 0) return false;
        UserToggle();
        return true;
    }

    /// <summary>Handle a left click on the box or its label, where it was drawn last.</summary>
    /// <returns>True if it was toggled.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!mouse.IsClick || !_area.Contains(mouse.X, mouse.Y)) return false;
        UserToggle();
        return true;
    }

    /// <summary>Called by the checkbox while drawing: the cells of its box and label.</summary>
    internal void Track(Rect area) => _area = area;

    private void UserToggle()
    {
        Toggle();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
