namespace MiniTui;

/// <summary>
/// What a component needs to know while rendering: whether it's focused, the theme and the app.
/// </summary>
public sealed class RenderContext
{
    private readonly Component? _owner;

    internal RenderContext(App app, Component? owner, bool focused)
    {
        App = app;
        _owner = owner;
        Focused = focused;
        Theme = Theme.Current;
    }

    /// <summary>True when the component is on the focus chain.</summary>
    public bool Focused { get; }

    /// <summary>The theme to draw with (<see cref="Theme.Current"/>).</summary>
    public Theme Theme { get; }

    /// <summary>The app.</summary>
    public App App { get; }

    /// <summary>The context for rendering a child: focused when this one is and its focus scope focuses the child.</summary>
    public RenderContext For(Component child)
    {
        ArgumentNullException.ThrowIfNull(child);
        var focused = Focused && _owner is not null && _owner.FocusScope.IsFocused(child);
        return new RenderContext(App, child, focused);
    }

    /// <summary>Hints for the bindings in scope on the current focus chain (§9.5), truncated to fit.</summary>
    public string Hints(int maxWidth) => KeyRouter.Hints(App, maxWidth);
}
