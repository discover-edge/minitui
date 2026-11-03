using MiniTui;
using MiniTui.Example;

// === Demo Application ===
// One app with a stack of screens: the menu is the first screen, and each demo is pushed on top of it.
// Esc (or q) goes back; ? shows the keys for the current screen; Ctrl+P searches and runs them and the menus;
// F10 opens the menu bar; Ctrl+C quits from anywhere.
// The mouse works too: click to focus, select and toggle, and scroll lists with the wheel.
// On Linux and macOS, Ctrl+Z suspends to the shell, and fg brings the demos back.

// Fixed dark and light themes, picked by the terminal's background
var app = App.Create().WithTheme(Theme.Dark, Theme.Light).WithHover().WithClipboardRead().WithSuspendKey().WithDebugKey();
Demo.Configure(app);                                   // q goes back, Ctrl+P, and the menu bar (F10 or Alt+F/V/H)
app.Screens.Push(new MenuScreen());
app.Run();
