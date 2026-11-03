namespace MiniTui.Example;

/// <summary>
/// What the demos take from outside: the clock, random numbers and the time of day, and the app's shared setup.
/// The site's recordings (tools/SiteMedia) replace the first three so every run draws the same frames (1.x spec §21.4).
/// </summary>
internal static class Demo
{
    public static TimeProvider Time { get; set; } = TimeProvider.System;
    public static Random Random { get; set; } = Random.Shared;
    public static DateTime Now => Time.GetLocalNow().DateTime;

    /// <summary>The keys and the menu bar every run of the demos has.</summary>
    public static App Configure(App app)
    {
        app.Keys.Add("q", "Back", () => app.Screens.Pop());   // popping the menu quits
        app.Keys.Add("ctrl+p", "Commands", () => app.Overlays.ShowPalette());
        return app.WithMenuBar(AppMenu.Create(app));          // F10 or Alt+F/V/H
    }
}
