using MiniTui;

namespace MiniTui.Example;

/// <summary>
/// The example's menu bar (1.x spec §15.7): F10, Alt+F/V/H or a click opens a menu. Its items run the same code as
/// the keys, and the View menu is built at each opening so its marks follow the app.
/// </summary>
internal static class AppMenu
{
    private static readonly string[] ThemeNames = ["&Auto (dark or light)", "&Default", "Dar&k", "&Light", "&Monochrome"];
    private static int s_theme;

    public static MenuBar Create(App app) => new(
        new Menu("&File",
        [
            new("&Open demo") { Submenu = [.. MenuScreen.Demos.Select(d => new MenuItem(d.Name) { Run = () => app.Screens.Push(d.Create()) })] },
            MenuItem.Separator,
            new("&Quit") { Hint = "Ctrl+C", Run = app.Quit },
        ]),
        new Menu("&View", () =>
        [
            new("&Theme") { Submenu = [.. ThemeNames.Select((name, i) => new MenuItem(name) { Radio = true, Checked = s_theme == i, Run = () => UseTheme(app, i) })] },
            new("&Debug overlay") { Hint = "F12", Checked = app.ShowDebug, Run = () => app.ShowDebug = !app.ShowDebug },
        ]),
        new Menu("&Help",
        [
            new("&Keys") { Hint = "?", Run = app.Overlays.ShowHelp },
            new("&About") { Run = () => _ = app.Overlays.ShowMessageAsync("About", "MiniTui demos\n\nF10 or Alt+letter opens a menu; Ctrl+P searches every command.") },
        ]));

    private static void UseTheme(App app, int theme)
    {
        s_theme = theme;
        if (theme == 0) app.WithTheme(Theme.Dark, Theme.Light);
        else app.WithTheme(theme switch { 1 => Theme.Default, 2 => Theme.Dark, 3 => Theme.Light, _ => Theme.Monochrome });
    }
}
