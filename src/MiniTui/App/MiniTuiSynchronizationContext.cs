namespace MiniTui;

/// <summary>
/// Posts continuations to the app's queue, so <c>await</c> in UI-thread code resumes on the UI thread (§6.3).
/// </summary>
internal sealed class MiniTuiSynchronizationContext(App app) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        app.Post(() => d(state));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The exception is rethrown on the calling thread.")]
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (app.IsUiThread)
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Exception? error = null;
        app.Post(() =>
        {
            try { d(state); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        done.Wait();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
    }

    public override SynchronizationContext CreateCopy() => this;
}
