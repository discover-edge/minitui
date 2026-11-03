# MiniTui

Terminal UIs for .NET: an immediate-mode TUI framework inspired by [Ratatui](https://ratatui.rs), with no runtime
dependencies. Describe the screen every frame, and MiniTui sends only the cells that changed.

Widgets (lists, tables, trees, text fields and editors, charts), layout and split panes, screens and focus, dialogs
and menus, the mouse and the clipboard, light and dark themes, and a test harness. Windows, Linux and macOS;
trim- and AOT-compatible.

**Documentation, guides and the API reference: [minitui.org](https://minitui.org)**

```csharp
using MiniTui;

var count = 0;
var app = App.Create();
var keys = new KeyMap()
    .Add("k,up", "+1", () => count++)
    .Add("j,down", "-1", () => count--)
    .Add("q", "Quit", app.Quit);

app.OnKey(keys.Handle)
    .Render((buf, screen) =>
    {
        var inner = Widget.Block().Title(" Counter ").Draw(buf, new Rect(2, 1, 30, 5));
        buf.SetString(inner.X + 1, inner.Y + 1, $"Count: {count}", Style.Default.Bold());
        Widget.StatusBar().Left(keys.Hints(screen.Width)).Draw(buf, screen);
    })
    .Run();
```

- [Getting started](https://minitui.org/guides/getting-started.html)
- [Changelog](https://minitui.org/changelog.html)
- [Source and issues on GitHub](https://github.com/minitui-org/minitui)

MIT licensed, © 2026 Discover Edge LLC.
