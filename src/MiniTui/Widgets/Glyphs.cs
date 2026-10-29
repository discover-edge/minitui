namespace MiniTui;

/// <summary>
/// Which glyphs widgets may draw. Conhost (the classic Windows console) renders with a single font and no
/// fallback, and its default fonts (Consolas, Lucida Console) only have the WGL4 block and box characters,
/// so eighth blocks, braille and rounded corners show as <c>?</c>. In basic mode widgets avoid them.
/// </summary>
internal static class Glyphs
{
    [ThreadStatic] private static bool t_basic;

    /// <summary>True when widgets on this thread should draw only basic glyphs.</summary>
    public static bool Basic => t_basic;

    /// <summary>Decide for the running app's backend. Only real consoles are checked; a test backend gets full glyphs.</summary>
    public static bool DetectBasic(ITerminalBackend backend) => backend switch
    {
        LegacyConsoleBackend => true,
        AnsiBackend => DetectBasic(legacyBackend: false, OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable),
        _ => false,
    };

    /// <summary>
    /// Basic on the legacy backend, and on Windows unless a known terminal (Windows Terminal, or one that
    /// sets <c>TERM_PROGRAM</c>, such as VS Code or WezTerm) hosts the app.
    /// </summary>
    internal static bool DetectBasic(bool legacyBackend, bool windows, Func<string, string?> getEnv) =>
        legacyBackend || (windows && getEnv("WT_SESSION") is null && getEnv("TERM_PROGRAM") is null);

    /// <summary>Use basic glyphs (or not) on this thread until the scope is disposed.</summary>
    internal static Scope Use(bool basic) => new(basic);

    internal readonly struct Scope : IDisposable
    {
        private readonly bool _previous;

        public Scope(bool basic)
        {
            _previous = t_basic;
            t_basic = basic;
        }

        public void Dispose() => t_basic = _previous;
    }
}
