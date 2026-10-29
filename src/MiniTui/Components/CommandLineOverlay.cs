namespace MiniTui;

/// <summary>
/// A <c>:</c> command line for a <see cref="CommandRegistry"/>, shown at the bottom of the screen. Enter runs the
/// command and shows its message as a toast; Esc closes. Not bound by default:
/// <c>app.Keys.Add(":", "Command", () => app.Overlays.Show(new CommandLineOverlay(registry)))</c>.
/// </summary>
public sealed class CommandLineOverlay : Overlay
{
    private readonly CommandRegistry _registry;
    private readonly CommandLineState _state = new();

    /// <summary>Create a command line that runs commands from <paramref name="registry"/>.</summary>
    public CommandLineOverlay(CommandRegistry registry) => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <inheritdoc/>
    public override bool CapturesText => true;

    /// <inheritdoc/>
    public override string? Title => " Command ";

    /// <inheritdoc/>
    public override Rect Placement(Rect screen) =>
        new(screen.X, Math.Max(screen.Y, screen.Bottom - 3), screen.Width, Math.Min(3, Math.Max(0, screen.Height)));

    /// <inheritdoc/>
    public override bool HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) return false; // closes the overlay
        if (key.Key != ConsoleKey.Enter) return _state.HandleKey(key);

        // Closing unmounts this overlay, so keep the app for the command's results
        var app = App;
        var text = _state.Text.Trim();
        Close();
        if (text.Length == 0) return true;

        var command = ParsedCommand.Parse(text.StartsWith(':') ? text : ":" + text);
        var result = _registry.Execute(command);
        if (result is null) app.Toasts.Push($"Unknown command: {command?.Name ?? text}", ToastLevel.Error);
        else if (result.Message is { } message) app.Toasts.Push(message, result.Success ? ToastLevel.Success : ToastLevel.Error);
        if (result?.ShouldQuit == true) app.Quit();
        return true;
    }

    /// <inheritdoc/>
    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        Widget.CommandLine().State(_state).Prompt(":").Draw(buf, area);
}
