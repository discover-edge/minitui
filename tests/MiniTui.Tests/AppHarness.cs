// Also exercises APIs that are obsolete in 0.9 and removed in 1.0 (§9.7).
#pragma warning disable CS0618

namespace MiniTui.Tests;

/// <summary>
/// Runs apps against a <see cref="TestBackend"/> and scripted input on a background UI thread.
/// </summary>
internal static class AppHarness
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>An app on a test terminal and scripted input, with no ticks and no console capture.</summary>
    public static App Create(TestBackend backend, IInputSource input) =>
        App.Create().WithBackend(backend).WithInput(input).WithTickRate(null).CaptureConsoleOutput(false);

    public static ConsoleKeyInfo Key(char c, ConsoleKey key = 0, bool control = false) =>
        new(c, key == 0 ? KeyFor(c) : key, shift: false, alt: false, control);

    public static ConsoleKeyInfo CtrlC => new('\x03', ConsoleKey.C, shift: false, alt: false, control: true);

    /// <summary>Run the app and wait for it to exit.</summary>
    public static Task Run(App app) => app.RunAsync().WaitAsync(Timeout);

    /// <summary>Run with a RunWhile condition on a dedicated thread and wait for it to exit.</summary>
    public static Task RunWhile(App app, Func<bool> condition) =>
        Task.Factory.StartNew(() => app.RunWhile(condition), TaskCreationOptions.LongRunning).WaitAsync(Timeout);

    /// <summary>Evaluate <paramref name="read"/> on the UI thread.</summary>
    public static async Task<T> OnUi<T>(App app, Func<T> read)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Post(() => result.SetResult(read()));
        return await result.Task.WaitAsync(Timeout);
    }

    /// <summary>Wait until <paramref name="condition"/>, evaluated on the UI thread, is true.</summary>
    public static async Task WaitUntil(App app, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await OnUi(app, condition))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met.");
            await Task.Delay(5);
        }
    }

    private static ConsoleKey KeyFor(char c) => c switch
    {
        >= 'a' and <= 'z' => ConsoleKey.A + (c - 'a'),
        '\r' => ConsoleKey.Enter,
        _ => 0,
    };
}
