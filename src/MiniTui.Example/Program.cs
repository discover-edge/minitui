using MiniTui;
using MiniTui.Example;

// === Demo Application ===
// One app with a stack of screens: the menu is the first screen, and each demo is pushed on top of it.
// Esc (or q) goes back; ? shows the keys for the current screen; Ctrl+C quits from anywhere.

var app = App.Create().WithTheme(Theme.Default);
app.Keys.Add("q", "Back", () => app.Screens.Pop());   // popping the menu quits
app.Screens.Push(new MenuScreen());
app.Run();
