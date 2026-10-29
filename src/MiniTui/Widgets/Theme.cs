namespace MiniTui;

/// <summary>
/// The styles widgets draw with (§8.1). Backgrounds are left to the terminal except for bars, buttons and
/// selections, so light and custom terminal themes show through.
/// </summary>
/// <remarks>
/// Change a few slots with <c>Theme.Default with { Selection = ... }</c>, and apply a theme with <see cref="App.WithTheme"/>.
/// </remarks>
public sealed record Theme
{
    [ThreadStatic] private static Theme? t_current;

    /// <summary>Plain text.</summary>
    public Style Text { get; init; }
    /// <summary>Secondary text, such as placeholders and footers.</summary>
    public Style TextMuted { get; init; }
    /// <summary>Borders of unfocused blocks.</summary>
    public Style Border { get; init; }
    /// <summary>Borders of focused blocks.</summary>
    public Style BorderFocused { get; init; }
    /// <summary>Titles of unfocused blocks.</summary>
    public Style Title { get; init; }
    /// <summary>Titles of focused blocks.</summary>
    public Style TitleFocused { get; init; }
    /// <summary>The selected row of a focused list or table.</summary>
    public Style Selection { get; init; }
    /// <summary>The selected row of an unfocused list or table.</summary>
    public Style SelectionUnfocused { get; init; }
    /// <summary>Table header cells.</summary>
    public Style TableHeader { get; init; }
    /// <summary>Text in an unfocused input.</summary>
    public Style Input { get; init; }
    /// <summary>Text in a focused input.</summary>
    public Style InputFocused { get; init; }
    /// <summary>The prompt of a command line.</summary>
    public Style Prompt { get; init; }
    /// <summary>The cursor cell of a focused input.</summary>
    public Style Cursor { get; init; }
    /// <summary>The status bar row.</summary>
    public Style StatusBar { get; init; }
    /// <summary>An unfocused button.</summary>
    public Style Button { get; init; }
    /// <summary>A focused button.</summary>
    public Style ButtonFocused { get; init; }
    /// <summary>The selected tab of a focused tab bar.</summary>
    public Style TabActive { get; init; }
    /// <summary>The selected tab of an unfocused tab bar.</summary>
    public Style TabActiveUnfocused { get; init; }
    /// <summary>Tabs that aren't selected.</summary>
    public Style TabInactive { get; init; }
    /// <summary>Modal and dialog borders.</summary>
    public Style ModalBorder { get; init; }
    /// <summary>Modal and dialog titles.</summary>
    public Style ModalTitle { get; init; }
    /// <summary>The scrollbar track.</summary>
    public Style ScrollbarTrack { get; init; }
    /// <summary>The scrollbar thumb.</summary>
    public Style ScrollbarThumb { get; init; }
    /// <summary>The filled part of a gauge.</summary>
    public Style GaugeFilled { get; init; }
    /// <summary>The empty part of a gauge.</summary>
    public Style GaugeEmpty { get; init; }
    /// <summary>Success messages and healthy values.</summary>
    public Style Success { get; init; }
    /// <summary>Warnings.</summary>
    public Style Warning { get; init; }
    /// <summary>Errors.</summary>
    public Style Error { get; init; }
    /// <summary>Informational messages.</summary>
    public Style Info { get; init; }

    /// <summary>The default colors.</summary>
    public static Theme Default { get; } = new()
    {
        Text = Style.Default,
        TextMuted = new Style(Color.DarkGray),
        Border = new Style(Color.DarkGray),
        BorderFocused = new Style(Color.Cyan),
        Title = new Style(Color.Cyan),
        TitleFocused = new Style(Color.Yellow),
        Selection = new Style(Color.Black, Color.Cyan),
        SelectionUnfocused = new Style(Color.Black, Color.DarkGray),
        TableHeader = new Style(Color.Yellow).Bold(),
        Input = Style.Default,
        InputFocused = Style.Default,
        Prompt = new Style(Color.Cyan),
        Cursor = Style.Default.Reversed(),
        StatusBar = new Style(Color.White, Color.DarkGray),
        Button = new Style(Color.White, Color.DarkGray),
        ButtonFocused = new Style(Color.Black, Color.Cyan),
        TabActive = new Style(Color.Black, Color.Cyan),
        TabActiveUnfocused = new Style(Color.Black, Color.DarkGray),
        TabInactive = Style.Default,
        ModalBorder = new Style(Color.DarkGray),
        ModalTitle = new Style(Color.Cyan),
        ScrollbarTrack = new Style(Color.DarkGray),
        ScrollbarThumb = new Style(Color.DarkGray),
        GaugeFilled = new Style(Color.Green),
        GaugeEmpty = new Style(Color.DarkGray),
        Success = new Style(Color.Green),
        Warning = new Style(Color.Yellow),
        Error = new Style(Color.Red),
        Info = new Style(Color.Cyan),
    };

    /// <summary>No colors, only modifiers. Used automatically when the terminal has no colors (<c>NO_COLOR</c>).</summary>
    public static Theme Monochrome { get; } = new()
    {
        Text = Style.Default,
        TextMuted = Style.Default.Dim(),
        Border = Style.Default,
        BorderFocused = Style.Default.Bold(),
        Title = Style.Default,
        TitleFocused = Style.Default.Bold(),
        Selection = Style.Default.Reversed(),
        SelectionUnfocused = Style.Default.Underlined(),
        TableHeader = Style.Default.Bold(),
        Input = Style.Default,
        InputFocused = Style.Default,
        Prompt = Style.Default.Bold(),
        Cursor = Style.Default.Reversed(),
        StatusBar = Style.Default.Reversed(),
        Button = Style.Default,
        ButtonFocused = Style.Default.Reversed(),
        TabActive = Style.Default.Reversed(),
        TabActiveUnfocused = Style.Default.Underlined(),
        TabInactive = Style.Default,
        ModalBorder = Style.Default,
        ModalTitle = Style.Default.Bold(),
        ScrollbarTrack = Style.Default.Dim(),
        ScrollbarThumb = Style.Default,
        GaugeFilled = Style.Default,
        GaugeEmpty = Style.Default.Dim(),
        Success = Style.Default,
        Warning = Style.Default.Bold(),
        Error = Style.Default.Bold().Underlined(),
        Info = Style.Default,
    };

    /// <summary>
    /// The theme widgets draw with: the running app's theme on its UI thread, otherwise <see cref="Default"/>.
    /// </summary>
    public static Theme Current => t_current ?? Default;

    /// <summary>Change the current theme inside an active <see cref="Use"/> scope, which still restores the previous one.</summary>
    internal static void ReplaceCurrent(Theme theme) => t_current = theme;

    /// <summary>Make <paramref name="theme"/> current on this thread until the scope is disposed.</summary>
    internal static Scope Use(Theme theme) => new(theme);

    internal readonly struct Scope : IDisposable
    {
        private readonly Theme? _previous;

        public Scope(Theme theme)
        {
            _previous = t_current;
            t_current = theme;
        }

        public void Dispose() => t_current = _previous;
    }
}
