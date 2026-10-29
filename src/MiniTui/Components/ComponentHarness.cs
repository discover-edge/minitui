using System.Diagnostics;

namespace MiniTui;

/// <summary>
/// Runs a screen in a test (§9.6): an app on a <see cref="TestBackend"/> with scripted input, driven one loop
/// iteration at a time on the calling thread. It never touches the real console.
/// </summary>
/// <example>
/// <code>
/// using var harness = new ComponentHarness(new ServicesScreen(fakeApi));
/// harness.Press("j j enter");
/// Assert.Contains("svc-3", harness.Lines[0]);
/// </code>
/// </example>
public sealed class ComponentHarness : IDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(5);

    private readonly TestBackend _backend;
    private bool _disposed;

    /// <summary>Push <paramref name="root"/> on a new app and render the first frame.</summary>
    /// <param name="root">The first screen.</param>
    /// <param name="width">Terminal width.</param>
    /// <param name="height">Terminal height.</param>
    /// <param name="time">The app's clock, such as a fake one; the system clock by default.</param>
    public ComponentHarness(Screen root, int width = 80, int height = 24, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        _backend = new TestBackend(width, height);
        App = App.Create()
            .WithBackend(_backend)
            .WithInput(new ScriptedInputSource())
            .WithTickRate(null)
            .CaptureConsoleOutput(false);
        if (time is not null) App.WithTimeProvider(time);
        App.Screens.Push(root);

        App.StartRun(condition: null, CancellationToken.None);
        try
        {
            using (App.EnterUi()) App.Setup();
            RunUntilIdle();
        }
        catch
        {
            App.EndRun();
            throw;
        }
    }

    /// <summary>The app under test. Its <see cref="App.Screens"/>, <see cref="App.Overlays"/> and <see cref="App.Toasts"/> can be inspected and changed.</summary>
    public App App { get; }

    /// <summary>The screen, one string per row.</summary>
    public string[] Lines => _backend.Lines;

    /// <summary>The cell at a position, as last drawn.</summary>
    public Cell CellAt(int x, int y) => _backend.CellAt(x, y);

    /// <summary>
    /// Press keys, then <see cref="RunUntilIdle"/>. <paramref name="keys"/> is space-separated
    /// <see cref="KeyBinding"/> names, such as <c>"j j enter"</c> or <c>"ctrl+r"</c>.
    /// </summary>
    /// <exception cref="FormatException">A key name isn't valid.</exception>
    public void Press(string keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var parts = keys.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        App.InjectKeys([.. parts.Select(k => KeyBinding.Parse(k).ToKeyInfo())]);
        RunUntilIdle();
    }

    /// <summary>Type text, one key per character, then <see cref="RunUntilIdle"/>.</summary>
    public void Type(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var keys = new List<ConsoleKeyInfo>();
        foreach (var c in text)
        {
            keys.Add(c switch
            {
                '\r' or '\n' => new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
                ' ' => new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false),
                _ when char.IsAsciiLetter(c) => new ConsoleKeyInfo(c, ConsoleKey.A + (char.ToLowerInvariant(c) - 'a'), char.IsAsciiLetterUpper(c), false, false),
                _ when char.IsAsciiDigit(c) => new ConsoleKeyInfo(c, ConsoleKey.D0 + (c - '0'), false, false, false),
                _ => new ConsoleKeyInfo(c, 0, false, false, false),
            });
        }
        App.InjectKeys([.. keys]);
        RunUntilIdle();
    }

    /// <summary>
    /// Handle queued keys, posted work and UI-thread continuations until nothing is left and no load or
    /// <see cref="App.RunTask"/> is in flight (other than ones waiting on a dialog), then render one frame.
    /// An exception the app would rethrow from <c>Run()</c> is rethrown here.
    /// </summary>
    /// <exception cref="TimeoutException">Work is still in flight after 5 seconds of real time.</exception>
    public void RunUntilIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var ui = App.EnterUi();
        var clock = Stopwatch.StartNew();
        while (true)
        {
            ThrowIfFailed();
            if (!App.IsRunning) return;
            if (App.HasQueuedWork)
            {
                App.Step(wait: false);
                continue;
            }
            if (App.PendingWork <= App.AwaitingUser) break;
            if (clock.Elapsed > IdleTimeout)
                throw new TimeoutException($"The app still had work in flight after {IdleTimeout.TotalSeconds:0} seconds.");
            App.WaitForQueue(TimeSpan.FromMilliseconds(10));
        }

        App.RequestRedraw();
        App.Step(wait: false);
        ThrowIfFailed();
    }

    /// <summary>Stop the app: screens and overlays stay mounted, but loads are cancelled through <see cref="App.Stopping"/>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        App.EndRun();
    }

    private void ThrowIfFailed() => App.TakeFailure()?.Throw();
}
