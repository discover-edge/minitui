namespace MiniTui;

// Suspend and resume (1.x spec §5).
public partial class App
{
    // Set while a Suspend action runs on this thread, so a nested Suspend runs its action directly
    [ThreadStatic] private static bool t_suspended;

    /// <summary>Stops the process; replaced in tests, which must never stop the test runner.</summary>
    internal Action StopProcessAction { get; set; } = DefaultStopProcess;

    /// <summary>Overrides whether <see cref="SuspendProcess"/> can stop the process; for tests.</summary>
    internal bool? CanStopProcessOverride { get; set; }

    /// <summary>
    /// Give the terminal back while <paramref name="action"/> runs, for example to run <c>$EDITOR</c> or another
    /// program in it (1.x spec §5.4). The app stops reading input, turns mouse reporting and bracketed paste off,
    /// restores the terminal mode, leaves the alternate screen and stops capturing <see cref="Console.Out"/> and
    /// <see cref="Console.Error"/>. When the action returns, or throws, the app takes the terminal again, sends
    /// <see cref="OnEvent"/> a <see cref="ResumeEvent"/> and redraws fully. Input typed during the action goes to
    /// the action or the program it runs, not to the app. While it runs, Ctrl+C doesn't end the app.
    /// </summary>
    /// <remarks>Called from inside an action, it runs <paramref name="action"/> directly.</remarks>
    /// <exception cref="InvalidOperationException">The app isn't running, or this isn't its UI thread.</exception>
    public void Suspend(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var (run, terminal) = RequireSuspendable();
        if (t_suspended)
        {
            action();
            return;
        }

        t_suspended = true;
        try
        {
            using (GiveTerminalBack(run, terminal)) action();
        }
        finally
        {
            t_suspended = false;
            AfterResume(terminal);
        }
    }

    /// <summary>
    /// Stop the process and return to the shell, as Ctrl+Z does in most terminal programs, with the terminal given
    /// back as <see cref="Suspend"/> does (1.x spec §5.5). Returns once the shell continues the process (<c>fg</c>)
    /// and the app has taken the terminal again. See also <see cref="WithSuspendKey"/>.
    /// </summary>
    /// <returns>
    /// False, doing nothing, where there's no job control: on Windows, or when the app doesn't draw to the console.
    /// </returns>
    /// <exception cref="InvalidOperationException">The app isn't running, or this isn't its UI thread.</exception>
    public bool SuspendProcess()
    {
        var (_, terminal) = RequireSuspendable();
        if (!(CanStopProcessOverride ?? (IsConsole(terminal.Backend) && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))))
            return false;
        Suspend(StopProcessAction);
        return true;
    }

    private (RunScope Run, Terminal Terminal) RequireSuspendable()
    {
        if (!_running || _run is not { Terminal: { } terminal } run)
            throw new InvalidOperationException("The app isn't running.");
        if (!IsUiThread) throw new InvalidOperationException("Suspend must be called on the app's UI thread.");
        return (run, terminal);
    }

    // Input first, so nothing reads the terminal while it's given back; then console capture and the screen
    private static DisposeAction GiveTerminalBack(RunScope run, Terminal terminal)
    {
        var undo = new Stack<IDisposable>();
        try
        {
            undo.Push(InputPump.Pause(run.Input));

            // Innermost app first, so the outer apps' writers come back, then the real ones
            for (var scope = run; scope is not null; scope = scope.PreviousApp?._run)
                if (scope.Capture is { } capture) undo.Push(capture.Pause());

            undo.Push(terminal.Suspend());
        }
        catch
        {
            while (undo.TryPop(out var step)) step.Dispose();
            throw;
        }
        return new DisposeAction(() =>
        {
            while (undo.TryPop(out var step)) step.Dispose();
        });
    }

    private void AfterResume(Terminal terminal)
    {
        if (terminal.CheckResize())
        {
            var resize = new ResizeEvent(terminal.Width, terminal.Height);
            Guard(() => _onEvent?.Invoke(resize));
        }
        Guard(() => _onEvent?.Invoke(new ResumeEvent()));
        StartKeyboard();
        SendQueries(); // the user may have come back in another terminal
        RequestRedraw();
    }

    // SIGTSTP from outside: stop the way Ctrl+Z does, so the terminal is given back first
    internal void OnStopSignal() => Post(() => SuspendProcess());

    // SIGCONT the app didn't cause: the shell may have drawn over the screen, and .NET may reset the mode
    internal void OnContinueSignal() => Post(() =>
    {
        if (_run is not { } run) return;
        (run.Input as TerminalInputSource)?.CheckModeSoon();
        Invalidate();
        _onEvent?.Invoke(new ResumeEvent());
        SendQueries();
    });

    private static bool IsConsole(ITerminalBackend backend) =>
        backend is IConsoleDevice device && ReferenceEquals(device.DeviceKey, ConsoleDevice.Key);

    private static void DefaultStopProcess()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) UnixTerminal.StopProcess();
    }
}
