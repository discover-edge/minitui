namespace MiniTui;

/// <summary>
/// The app's screens (§9.3). Only the top screen renders and receives keys; the ones below keep their state.
/// </summary>
/// <remarks>
/// Changes made from another thread while the app runs are posted to the UI thread.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name from the spec (§9.3); it is a stack.")]
public sealed class ScreenStack
{
    private readonly App _app;
    private readonly List<Screen> _screens = [];

    internal ScreenStack(App app) => _app = app;

    /// <summary>The screen on top, or null when the stack is empty.</summary>
    public Screen? Top => _screens.Count == 0 ? null : _screens[^1];

    /// <summary>How many screens are on the stack.</summary>
    public int Count => _screens.Count;

    /// <summary>The screens, bottom first.</summary>
    public IReadOnlyList<Screen> All => [.. _screens];

    /// <summary>Mount <paramref name="screen"/>, deactivate the previous top and activate the new one.</summary>
    /// <exception cref="InvalidOperationException">The app has a <see cref="App.Render"/> callback, or the screen is already mounted.</exception>
    public void Push(Screen screen)
    {
        Validate(screen);
        _app.OnUiThread(() =>
        {
            var previous = Top;
            Add(screen);
            previous?.Deactivate();
            screen.Activate();
            _app.RequestRedraw();
        });
    }

    /// <summary>
    /// Unmount the top screen and activate the new top. If that empties the stack and
    /// <see cref="App.QuitWhenEmpty"/> is on (the default), the app quits.
    /// </summary>
    public void Pop() => _app.OnUiThread(() =>
    {
        if (Top is not { } top) return;
        Remove(top);
        Top?.Activate();
        _app.RequestRedraw();
        if (_screens.Count == 0) _app.OnScreensEmpty();
    });

    /// <summary>Pop the top screen and push <paramref name="screen"/>, without activating the screen below in between.</summary>
    public void Replace(Screen screen)
    {
        Validate(screen);
        _app.OnUiThread(() =>
        {
            if (Top is { } top) Remove(top);
            Add(screen);
            screen.Activate();
            _app.RequestRedraw();
        });
    }

    /// <summary>Pop screens until <paramref name="screen"/> is on top.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="screen"/> isn't on the stack.</exception>
    public void PopTo(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (!_screens.Contains(screen)) throw new InvalidOperationException($"{screen.GetType().Name} isn't on the screen stack.");
        _app.OnUiThread(() =>
        {
            if (!_screens.Contains(screen)) return;
            while (Top is { } top && !ReferenceEquals(top, screen)) Remove(top);
            screen.Activate();
            _app.RequestRedraw();
        });
    }

    private void Validate(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (_app.HasRenderCallback)
            throw new InvalidOperationException("Screens can't be used with a Render callback; use one or the other.");
        if (screen.IsMounted || screen.Parent is not null || _screens.Contains(screen))
            throw new InvalidOperationException($"{screen.GetType().Name} is already mounted.");
    }

    private void Add(Screen screen)
    {
        _screens.Add(screen);
        try
        {
            screen.Mount(_app);
        }
        catch
        {
            _screens.Remove(screen);
            screen.Unmount();
            throw;
        }
    }

    private void Remove(Screen screen)
    {
        try
        {
            screen.Deactivate();
        }
        finally
        {
            _screens.Remove(screen);
            screen.Unmount();
        }
    }
}
