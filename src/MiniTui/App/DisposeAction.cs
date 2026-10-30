namespace MiniTui;

/// <summary>Runs an action on the first dispose; later disposes do nothing.</summary>
internal sealed class DisposeAction(Action? action) : IDisposable
{
    /// <summary>A scope with nothing to undo.</summary>
    public static readonly DisposeAction None = new(null);

    private Action? _action = action;

    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}
