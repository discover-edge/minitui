namespace MiniTui;

/// <summary>
/// A modal component drawn over the screens, inside a themed block. While any overlay is open, the top one
/// gets every key.
/// </summary>
public abstract class Overlay : Component
{
    /// <summary>Whether an unhandled Esc closes the overlay. True by default.</summary>
    public virtual bool CloseOnEscape => true;

    /// <summary>
    /// Whether the app draws the themed block around the overlay. When false, the app only clears the
    /// <see cref="Placement"/> rect, and <see cref="Component.Render"/> gets all of it to draw, border included.
    /// True by default.
    /// </summary>
    public virtual bool Framed => true;

    /// <summary>
    /// Whether a press of any mouse button outside the overlay closes it (1.x spec §7.6). The press goes nowhere
    /// else. False by default.
    /// </summary>
    public virtual bool CloseOnClickOutside => false;

    /// <summary>The title in the overlay's border.</summary>
    public virtual string? Title => null;

    /// <inheritdoc/>
    public override string KeysGroup => Title?.Trim() is { Length: > 0 } title ? title : GetType().Name;

    /// <summary>
    /// Where the overlay goes, including its border. The default is centered, 60% × 50% of the screen, at least
    /// 30 × 7, and clamped to the screen minus a 2-cell margin.
    /// </summary>
    public virtual Rect Placement(Rect screen) => Centered(screen, Math.Max(30, screen.Width * 60 / 100), Math.Max(7, screen.Height * 50 / 100));

    /// <summary>Close this overlay.</summary>
    protected void Close() => App.Overlays.Close(this);

    /// <summary>A rect of up to <paramref name="width"/> × <paramref name="height"/>, centered in the screen minus a 2-cell margin.</summary>
    protected static Rect Centered(Rect screen, int width, int height)
    {
        var bounds = screen.Inner(new Margin(2));
        width = Math.Clamp(width, 0, bounds.Width);
        height = Math.Clamp(height, 0, bounds.Height);
        return new Rect(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
    }
}
