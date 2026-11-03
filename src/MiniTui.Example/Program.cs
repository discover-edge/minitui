using MiniTui;
using MiniTui.Example;

// === Demo Application ===
// One app with a stack of screens: the menu is the first screen, and each demo is pushed on top of it.
// Esc (or q) goes back; ? shows the keys for the current screen; Ctrl+P searches and runs them and the menus;
// F10 opens the menu bar; Ctrl+C quits from anywhere.
// The mouse works too: click to focus, select and toggle, and scroll lists with the wheel.
// On Linux and macOS, Ctrl+Z suspends to the shell, and fg brings the demos back.

// Fixed dark and light themes, picked by the terminal's background
var app = App.Create().WithTheme(Theme.Dark, Theme.Light).WithHover().WithSuspendKey().WithDebugKey();
app.Keys.Add("q", "Back", () => app.Screens.Pop());   // popping the menu quits
app.Keys.Add("ctrl+p", "Commands", () => app.Overlays.ShowPalette());
app.WithMenuBar(AppMenu.Create(app));                  // F10 or Alt+F/V/H
app.Screens.Push(new MenuScreen());
app.Run();
