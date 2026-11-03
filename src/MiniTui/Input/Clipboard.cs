namespace MiniTui;

/// <summary>
/// The clipboard that copy, cut and Ctrl+V in text fields and text areas use (1.x spec §10.5). Text set here is
/// always kept as the in-app clipboard, and also goes to the system clipboard where the platform and terminal allow
/// it: the Win32 clipboard on Windows, and OSC 52 on Unix terminals, while an app is running.
/// </summary>
public static class Clipboard
{
    private static volatile string? s_text;
    [ThreadStatic] private static Target? t_target;

    /// <summary>Put text on the system clipboard where the platform and terminal allow it, and on the in-app clipboard.</summary>
    public static void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        s_text = text;
        t_target?.Write?.Invoke(text);
    }

    /// <summary>
    /// The text Ctrl+V pastes: the system clipboard on Windows, otherwise the last text set in this process. Null if
    /// there's none.
    /// </summary>
    public static string? GetText() => t_target?.Read?.Invoke() ?? s_text;

    /// <summary>
    /// The system clipboard's text where it can be read (1.x spec §20.3): the Win32 clipboard on Windows, and on Linux
    /// and macOS with <see cref="App.WithClipboardRead"/>, the running app's terminal through an OSC 52 query.
    /// Otherwise, or when the terminal refuses or doesn't answer in time, <see cref="GetText"/>.
    /// </summary>
    public static async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        if (t_target?.ReadAsync is not { } read) return GetText();
        return await read(cancellationToken).ConfigureAwait(true) ?? s_text;
    }

    /// <summary>
    /// Ctrl+V and Shift+Insert in the text states: paste the clipboard now, or, when it's read from the terminal,
    /// when the reply arrives (through <see cref="App.RunTask"/>).
    /// </summary>
    internal static bool PasteInto(Func<string, bool> paste)
    {
        if (t_target?.ReadAsync is not null && App.Current is { } app)
        {
            _ = app.RunTask(async ct => paste(await GetTextAsync(ct).ConfigureAwait(true) ?? ""));
            return true;
        }
        return paste(GetText() ?? "");
    }

    /// <summary>The running app's system clipboard, for the given backend; null for in-app only.</summary>
    internal static Target? For(ITerminalBackend? backend)
    {
        if (backend is not (AnsiBackend or LegacyConsoleBackend)) return null;
        if (OperatingSystem.IsWindows())
            return new Target(WindowsConsoleInput.WriteClipboardText, WindowsConsoleInput.ReadClipboardText);
        return backend is AnsiBackend ansi ? new Target(ansi.WriteClipboard, null) : null;
    }

    /// <summary>Use <paramref name="target"/> on this thread until the scope is disposed.</summary>
    internal static Scope Use(Target? target) => new(target);

    /// <summary>Forget the in-app text, for tests.</summary>
    internal static void Reset() => s_text = null;

    /// <summary>Where text goes and comes from besides the in-app clipboard.</summary>
    internal sealed record Target(Action<string>? Write, Func<string?>? Read)
    {
        /// <summary>Reads the terminal's clipboard: null when it refused or didn't answer (1.x spec §20.3).</summary>
        public Func<CancellationToken, Task<string?>>? ReadAsync { get; init; }
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly Target? _previous;

        public Scope(Target? target)
        {
            _previous = t_target;
            t_target = target;
        }

        public void Dispose() => t_target = _previous;
    }
}
