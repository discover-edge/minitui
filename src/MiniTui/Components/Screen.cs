namespace MiniTui;

/// <summary>
/// A full-screen component on the app's <see cref="ScreenStack"/>. Only the top screen renders and gets keys.
/// </summary>
public abstract class Screen : Component
{
    /// <summary>The title shown in the breadcrumb.</summary>
    public virtual string Title => GetType().Name;

    /// <summary>Whether an unhandled Esc pops this screen (when it isn't the only one). True by default.</summary>
    public virtual bool PopOnEscape => true;

    /// <inheritdoc/>
    public override string KeysGroup => Title;
}
