---
_layout: landing
title: Terminal UIs for .NET
---

<div class="mt-hero">
  <h1>MiniTui</h1>
  <p class="mt-tagline">Terminal UIs for .NET: immediate mode, fast, and dependency-free.</p>
  <p class="mt-lede">Describe the screen every frame, and MiniTui draws only what changed. Widgets, layout, screens and
  focus, the mouse and the clipboard, themes, and a test harness, on Windows, Linux and macOS.</p>
  <div class="mt-install"><code>dotnet add package MiniTui</code></div>
  <div class="mt-actions">
    <a class="btn btn-primary btn-lg" href="guides/getting-started.md">Get started</a>
    <a class="btn btn-outline-secondary btn-lg" href="api/MiniTui.yml">API reference</a>
    <a class="btn btn-outline-secondary btn-lg" href="https://github.com/minitui-org/minitui">GitHub</a>
  </div>
</div>

<div class="mt-tour">
  <div id="mt-tour-player" aria-label="A recording of the MiniTui example app: the menu, a dashboard, an editor, a file tree and the widgets"></div>
  <p class="mt-caption">The example app, recorded from its own frames. The text is real: pause and select it.</p>
</div>

<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/asciinema-player@3.17.0/dist/bundle/asciinema-player.css">
<script src="https://cdn.jsdelivr.net/npm/asciinema-player@3.17.0/dist/bundle/asciinema-player.min.js"></script>
<script>
  AsciinemaPlayer.create("media/tour.cast", document.getElementById("mt-tour-player"), {
    autoPlay: true, loop: true, fit: "width", idleTimeLimit: 3, poster: "npt:0:6",
    terminalFontFamily: "ui-monospace, 'Cascadia Mono', 'SF Mono', Menlo, Consolas, 'DejaVu Sans Mono', monospace"
  });
</script>

<div class="mt-features">
  <div class="mt-feature">
    <a href="media/widgets.svg"><img src="media/widgets.svg" alt="The Widgets demo: a list, a gauge, a sparkline, a canvas and Unicode text" loading="lazy"></a>
    <h3>Widgets</h3>
    <p>Lists, tables, trees, tabs, text fields and editors, gauges, sparklines, line and bar charts, and a braille
    canvas. Each is a builder that draws into a buffer.</p>
    <a href="guides/widgets.md">Widgets</a>
  </div>
  <div class="mt-feature">
    <a href="media/screens.svg"><img src="media/screens.svg" alt="The command palette open over the demo menu" loading="lazy"></a>
    <h3>Screens and focus</h3>
    <p>Grow from one render callback to a stack of screens built from components, with focus scopes, dialogs, menus,
    a command palette and scoped key bindings.</p>
    <a href="guides/screens.md">Screens and components</a>
  </div>
  <div class="mt-feature">
    <a href="media/layout.svg"><img src="media/layout.svg" alt="Split panes with a list, a sortable table and a detail view" loading="lazy"></a>
    <h3>Layout</h3>
    <p>Constraints solved with exact integer arithmetic, so regions never overlap or drop a cell, and split panes
    you drag with the mouse.</p>
    <a href="guides/layout.md">Layout</a>
  </div>
  <div class="mt-feature">
    <a href="media/async.svg"><img src="media/async.svg" alt="A table of 500 services loaded from a fake HTTP API" loading="lazy"></a>
    <h3>Async without blocking</h3>
    <p>HTTP calls and slow work run off the UI thread, and their results come back on it. <code>Loadable&lt;T&gt;</code>
    shows loading, errors and retries.</p>
    <a href="guides/async.md">Async data and HTTP</a>
  </div>
  <div class="mt-feature">
    <a href="media/terminal.svg"><img src="media/terminal.svg" alt="The editor demo with line numbers, a scrollbar and the find bar" loading="lazy"></a>
    <h3>The terminal, done right</h3>
    <p>Raw input with the kitty keyboard protocol, bracketed paste, mouse and hover, OSC 52 clipboard, light and dark
    detection, and a terminal that's restored on exit, crashes and Ctrl+Z.</p>
    <a href="guides/terminal.md">The terminal</a>
  </div>
  <div class="mt-feature">
    <pre class="mt-test"><code>var h = new ComponentHarness(new Counter());
h.Press("k k k");
Assert.Contains("Count: 3", h.Lines[2]);</code></pre>
    <h3>Testable</h3>
    <p><code>ComponentHarness</code> runs a screen without a terminal: press keys, click, and assert on the cells it
    drew.</p>
    <a href="guides/testing.md">Testing</a>
  </div>
</div>

<div class="mt-sample">
  <div class="mt-sample-text">
    <h2>A whole app in a screenful</h2>
    <p>A render callback and a key map. The status bar shows the key hints, <code>q</code> quits, and the terminal
    is restored however the app ends.</p>
    <img src="media/counter.svg" alt="The counter app: a box with Count: 3, and the key hints in the status bar" class="mt-counter">
    <p><a href="guides/getting-started.md">Getting started</a></p>
  </div>
  <div class="mt-sample-code">

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

  </div>
</div>
