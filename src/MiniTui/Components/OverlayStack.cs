namespace MiniTui;

/// <summary>
/// Modal overlays drawn over the screens (§9.3). While any is open, the top one gets every key.
/// </summary>
/// <remarks>
/// Changes made from another thread while the app runs are posted to the UI thread.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name from the spec (§9.3); it is a stack.")]
public sealed class OverlayStack
{
    private readonly App _app;
    private readonly List<Overlay> _overlays = [];

    internal OverlayStack(App app) => _app = app;

    /// <summary>The overlay on top, or null.</summary>
    public Overlay? Top => _overlays.Count == 0 ? null : _overlays[^1];

    /// <summary>True while any overlay is open.</summary>
    public bool Any => _overlays.Count > 0;

    /// <summary>The open overlays, bottom first.</summary>
    internal IReadOnlyList<Overlay> All => [.. _overlays];

    /// <summary>Open an overlay on top of the others.</summary>
    /// <exception cref="InvalidOperationException">The overlay is already mounted.</exception>
    public void Show(Overlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (overlay.IsMounted || overlay.Parent is not null || _overlays.Contains(overlay))
            throw new InvalidOperationException($"{overlay.GetType().Name} is already mounted.");
        _app.OnUiThread(() =>
        {
            _overlays.Add(overlay);
            try
            {
                overlay.Mount(_app);
            }
            catch
            {
                _overlays.Remove(overlay);
                overlay.Unmount();
                throw;
            }
            overlay.Activate();
            _app.RequestRedraw();
        });
    }

    /// <summary>Close an overlay. Does nothing if it isn't open.</summary>
    public void Close(Overlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _app.OnUiThread(() =>
        {
            if (!_overlays.Remove(overlay)) return;
            try
            {
                overlay.Deactivate();
            }
            finally
            {
                overlay.Unmount();
                _app.RequestRedraw();
            }
        });
    }

    /// <summary>
    /// Ask a yes/no question. Enter on Yes and <c>y</c> answer true; Enter on No, <c>n</c>, Esc and closing the
    /// dialog answer false. Awaited on the UI thread, the continuation runs there.
    /// </summary>
    public Task<bool> ConfirmAsync(string message, string yes = "Yes", string no = "No")
    {
        var dialog = new ConfirmDialog(message, yes, no);
        Show(dialog);
        return dialog.Result;
    }

    /// <summary>Show a scrollable message. The task completes when it's closed (Enter or Esc).</summary>
    public Task ShowMessageAsync(string title, Text body)
    {
        var dialog = new MessageDialog(title, body);
        Show(dialog);
        return dialog.Closed;
    }

    /// <summary>Show the key bindings in scope on the current focus chain, grouped by component (§9.5).</summary>
    public void ShowHelp() => _app.OnUiThread(() => Show(new HelpOverlay(KeyRouter.HelpLines(_app))));
}
