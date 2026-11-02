namespace MiniTui;

// Terminal queries, their replies and light and dark themes (1.x spec §12.3, §12.4)
public partial class App
{
    private bool _queries = true;
    private bool _keyboardProtocol = true;
    private bool _kittySeen;
    private (Theme Dark, Theme Light)? _themePair;
    private TerminalInfo _terminalInfo = new();
    private long? _queriedAt;
    private bool _themeChosen;

    /// <summary>What the terminal has said about itself so far (1.x spec §12.3). Updated on the UI thread as replies arrive.</summary>
    public TerminalInfo TerminalInfo => _terminalInfo;

    /// <summary>
    /// Ask the terminal about itself at startup and after a resume: its colors, its keyboard protocols and its
    /// name (1.x spec §12.3). On by default; the <c>MINITUI_QUERIES=0</c> environment variable also turns it off.
    /// Replies arrive in <see cref="TerminalInfo"/> and as a <see cref="TerminalInfoEvent"/>.
    /// </summary>
    public App WithTerminalQueries(bool enabled = true)
    {
        _queries = enabled;
        return this;
    }

    /// <summary>
    /// Turn on the best keyboard protocol the terminal supports (1.x spec §12.5): the kitty keyboard protocol, else
    /// xterm's modifyOtherKeys, each only after the terminal said it supports it, or Windows Terminal's
    /// win32-input-mode on Windows. Keys the classic encoding can't express, such as Ctrl+Enter, Shift+Enter,
    /// Ctrl+Tab and Ctrl+I, then arrive as themselves; every other key arrives as before. On by default; the
    /// <c>MINITUI_KEYBOARD=legacy</c> environment variable also turns it off. The protocol in use is
    /// <see cref="TerminalInfo.Keyboard"/>.
    /// </summary>
    public App WithKeyboardProtocol(bool enabled = true)
    {
        _keyboardProtocol = enabled;
        return this;
    }

    /// <summary>
    /// Use <paramref name="dark"/> on a dark background and <paramref name="light"/> on a light one, as
    /// <see cref="TerminalInfo.IsDark"/> says (1.x spec §12.4). <paramref name="dark"/> is used until the background
    /// is known, and when it never is; the first frame waits up to 150 ms for the terminal's reply. Calling
    /// <see cref="WithTheme(Theme)"/> afterwards replaces the pair.
    /// </summary>
    public App WithTheme(Theme dark, Theme light)
    {
        ArgumentNullException.ThrowIfNull(dark);
        ArgumentNullException.ThrowIfNull(light);
        _themePair = (dark, light);
        _theme = null;
        if (_running) InvokeOnUiThread(() => ApplyTheme(PickTheme()));
        return this;
    }

    /// <summary>Writes the queries instead of the backend; set by tests.</summary>
    internal Action? QueryWriter { get; set; }

    /// <summary>Switches the keyboard protocol instead of the input source; set by tests.</summary>
    internal Action<KeyboardProtocol>? KeyboardWriter { get; set; }

    /// <summary>Whether this run is on Windows, for win32-input-mode; set by tests.</summary>
    internal bool? WindowsOverride { get; set; }

    /// <summary>Queue a reply as if the input source had decoded it.</summary>
    internal void InjectReply(TerminalReply reply) => _queue.Writer.TryWrite(new KeyBatch([InputItem.FromReply(reply)]));

    /// <summary>Called by <see cref="StartRun"/>: what's known before any reply.</summary>
    private void StartTerminalInfo()
    {
        _terminalInfo = new TerminalInfo { IsDark = TerminalQueries.IsDarkFromColorFgBg(GetEnvironment("COLORFGBG")) };
        _queriedAt = null;
        _themeChosen = false;
    }

    /// <summary>The theme to start with: the configured one, or the pair's pick from what's known.</summary>
    private Theme? StartTheme() => _themePair is not null ? PickTheme() : _theme;

    private Theme PickTheme() => _themePair is { } pair ? (_terminalInfo.IsDark == false ? pair.Light : pair.Dark) : _theme ?? Theme.Default;

    private void ApplyTheme(Theme theme)
    {
        _activeTheme = ResolveTheme(theme);
        Theme.ReplaceCurrent(_activeTheme);
    }

    /// <summary>
    /// Write the queries, if they're on and the replies will be decoded: a VT terminal on the ANSI backend, read by
    /// <see cref="TerminalInputSource"/> in raw mode. Anywhere else the replies would arrive as typed keys.
    /// </summary>
    private void SendQueries()
    {
        if (!_queries || GetEnvironment("MINITUI_QUERIES") == "0" || _run is not { Terminal: { } terminal } run) return;
        if (QueryWriter is { } writer) writer();
        else if (CanTalkToTerminal(run, terminal)) ((AnsiBackend)terminal.Backend).WriteQueries();
        else return;
        _queriedAt = _time.GetTimestamp();
        _kittySeen = false;
    }

    private static bool CanTalkToTerminal(RunScope run, Terminal terminal) =>
        terminal.Backend is AnsiBackend && run.Input is TerminalInputSource { IsRaw: true };

    private bool IsWindows => WindowsOverride ?? OperatingSystem.IsWindows();

    private bool KeyboardProtocolOn => _keyboardProtocol && GetEnvironment("MINITUI_KEYBOARD") != "legacy";

    /// <summary>
    /// Called by <see cref="StartRun"/> and after a resume: the protocol goes back to legacy until the replies say
    /// otherwise, except win32-input-mode on Windows, which is asked for at once (1.x spec §12.5).
    /// </summary>
    private void StartKeyboard()
    {
        _terminalInfo = _terminalInfo with { Keyboard = KeyboardProtocol.Legacy };
        if (KeyboardProtocolOn && IsWindows)
            SetKeyboard(KeyboardProtocol.Win32InputMode, report: false);
    }

    private void SetKeyboard(KeyboardProtocol protocol, bool report)
    {
        if (_run is not { Terminal: { } terminal } run) return;
        if (KeyboardWriter is { } writer) writer(protocol);
        else if (CanTalkToTerminal(run, terminal) && run.Input is TerminalInputSource source) source.SetKeyboardProtocol(protocol);
        else return;
        if (report) _terminalInfo = _terminalInfo with { Keyboard = protocol };
    }

    // The kitty protocol is preferred: its reply comes before the XTQMODKEYS one
    private void ChooseKeyboard(TerminalReply reply)
    {
        if (!KeyboardProtocolOn) return;
        switch (reply)
        {
            case TerminalReply.KittyFlags:
                _kittySeen = true;
                if (_terminalInfo.Keyboard != KeyboardProtocol.Kitty) SetKeyboard(KeyboardProtocol.Kitty, report: true);
                break;
            case TerminalReply.ModifyOtherKeys when !_kittySeen && _terminalInfo.Keyboard is not KeyboardProtocol.Kitty:
                if (_terminalInfo.Keyboard != KeyboardProtocol.ModifyOtherKeys) SetKeyboard(KeyboardProtocol.ModifyOtherKeys, report: true);
                break;
            case TerminalReply.Win32InputMode when _terminalInfo.Keyboard == KeyboardProtocol.Legacy && IsWindows:
                _terminalInfo = _terminalInfo with { Keyboard = KeyboardProtocol.Win32InputMode };
                break;
        }
    }

    private void HandleReply(TerminalReply reply)
    {
        var before = _terminalInfo;
        _terminalInfo = TerminalQueries.Apply(before, reply);
        ChooseKeyboard(reply);

        // The theme is picked once, when the background is first known
        if (_themePair is not null && !_themeChosen && reply is TerminalReply.DefaultColor { Which: 11 })
        {
            _themeChosen = true;
            ApplyTheme(PickTheme());
        }
        if (_terminalInfo != before) _onEvent?.Invoke(new TerminalInfoEvent(_terminalInfo));
    }

    /// <summary>
    /// With a theme pair, the first frame waits for the background (or the DA1 reply that says it isn't coming), up
    /// to <see cref="TerminalQueries.ThemeWait"/> after the queries were sent.
    /// </summary>
    private bool HoldFirstFrame(out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (_themePair is null || _queriedAt is not { } sent || _terminalInfo.Answered || _terminalInfo.Background is not null)
            return false;
        remaining = TerminalQueries.ThemeWait - _time.GetElapsedTime(sent);
        return remaining > TimeSpan.Zero;
    }
}
