using MiniTui;
using MiniTui.Example;

// === Demo Application ===
// One app with a stack of screens: the menu is the first screen, and each demo is pushed on top of it.
// Esc (or q) goes back; ? shows the keys for the current screen; Ctrl+C quits from anywhere.
// The mouse works too: click to focus, select and toggle, and scroll lists with the wheel.
// On Linux and macOS, Ctrl+Z suspends to the shell, and fg brings the demos back.

var app = App.Create().WithTheme(Theme.Default).WithMouse().WithSuspendKey();
app.Keys.Add("q", "Back", () => app.Screens.Pop());   // popping the menu quits
app.Screens.Push(new MenuScreen());
app.Run();
