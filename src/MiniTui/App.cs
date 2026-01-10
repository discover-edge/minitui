namespace MiniTui;

/// <summary>
/// Fluent builder for creating TUI applications.
/// </summary>
public class App
{
    private InputRouter? _router;
    private Action? _onSetup;
    private Action<Buffer, Rect>? _onRender;
    private Action? _onUpdate;
    private int _frameDelayMs = 16;
    private bool _running;
    private Terminal? _terminal;

    private App() { }

    /// <summary>
    /// Create a new App builder.
    /// </summary>
    public static App Create() => new();

    /// <summary>
    /// Configure the input router for handling keyboard input.
    /// </summary>
    public App UseRouter(InputRouter router)
    {
        _router = router;
        return this;
    }

    /// <summary>
    /// Set up code to run once before the main loop starts.
    /// </summary>
    public App OnSetup(Action setup)
    {
        _onSetup = setup;
        return this;
    }

    /// <summary>
    /// Configure the render callback. Called each frame with the buffer and screen rect.
    /// </summary>
    public App Render(Action<Buffer, Rect> render)
    {
        _onRender = render;
        return this;
    }

    /// <summary>
    /// Configure per-frame update logic that runs before rendering.
    /// </summary>
    public App OnUpdate(Action update)
    {
        _onUpdate = update;
        return this;
    }

    /// <summary>
    /// Set the frame delay in milliseconds (default: 16ms ~60fps).
    /// </summary>
    public App WithFrameDelay(int milliseconds)
    {
        _frameDelayMs = milliseconds;
        return this;
    }

    /// <summary>
    /// Request the app to quit after the current frame.
    /// </summary>
    public void Quit() => _running = false;

    /// <summary>
    /// Force a full screen redraw on the next frame.
    /// Useful after dismissing modals or overlays.
    /// </summary>
    public void Invalidate() => _terminal?.Invalidate();

    /// <summary>
    /// Run the application. Blocks until Quit() is called.
    /// </summary>
    public void Run()
    {
        if (_onRender == null)
            throw new InvalidOperationException("Render callback is required. Call Render() before Run().");

        _running = true;
        _onSetup?.Invoke();

        using var terminal = new Terminal();
        _terminal = terminal;

        var wasModalActive = false;

        while (_running)
        {
            // Check for terminal resize
            terminal.CheckResize();

            _onUpdate?.Invoke();

            terminal.Draw(buf =>
            {
                var screen = new Rect(0, 0, buf.Width, buf.Height);
                _onRender(buf, screen);
            });

            if (Input.Poll() is { } key)
                _router?.Handle(key);

            // Auto-invalidate when modal is dismissed
            var isModalActive = _router?.IsModalActive ?? false;
            if (wasModalActive && !isModalActive)
                terminal.Invalidate();
            wasModalActive = isModalActive;

            Thread.Sleep(_frameDelayMs);
        }
    }

    /// <summary>
    /// Run the application with a custom run condition.
    /// </summary>
    public void RunWhile(Func<bool> condition)
    {
        if (_onRender == null)
            throw new InvalidOperationException("Render callback is required. Call Render() before Run().");

        _running = true;
        _onSetup?.Invoke();

        using var terminal = new Terminal();
        _terminal = terminal;

        var wasModalActive = false;

        while (_running && condition())
        {
            // Check for terminal resize
            terminal.CheckResize();

            _onUpdate?.Invoke();

            terminal.Draw(buf =>
            {
                var screen = new Rect(0, 0, buf.Width, buf.Height);
                _onRender(buf, screen);
            });

            if (Input.Poll() is { } key)
                _router?.Handle(key);

            // Auto-invalidate when modal is dismissed
            var isModalActive = _router?.IsModalActive ?? false;
            if (wasModalActive && !isModalActive)
                terminal.Invalidate();
            wasModalActive = isModalActive;

            Thread.Sleep(_frameDelayMs);
        }
    }
}
