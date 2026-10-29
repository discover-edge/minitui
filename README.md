# MiniTui

A minimal, immediate-mode TUI (Text User Interface) framework for C#. No dependencies, no magic.

## Philosophy

- **Immediate mode**: You describe the UI every frame. No hidden state, no retained widgets.
- **Double-buffered**: Only changed cells write to the terminal. Fast and flicker-free.
- **Fluent APIs**: Chainable builders guide you through configuration.
- **Composable**: Everything builds from simple primitives.

## Quick Start

```bash
dotnet run --project src/MiniTui.Example
```

This launches a demo app showcasing counters, lists, forms, progress bars, and a dashboard, plus:

- **Services**: an async table of 500 services from a fake HTTP API (slow, and failing 20% of the time), with
  refresh, cancellation, error toasts and a `?` key-help modal.
- **Widgets**: every widget on one screen, a theme toggle (`t`) and a Unicode sample panel.

## Usage Pattern

```csharp
using MiniTui;

var count = 0;

App.Create()
    .Render((buf, screen) =>
    {
        buf.DrawBox(new Rect(2, 1, 30, 5), ConsoleColor.Cyan);
        buf.DrawText(4, 2, $"Count: {count}", ConsoleColor.White);
        buf.DrawText(4, 3, "[j] dec  [k] inc  [q] quit", ConsoleColor.DarkGray);
    })
    .RunWhile(() =>
    {
        var key = Input.Poll();
        if (Input.IsQuit(key)) return false;

        if (key?.KeyChar == 'j') count--;
        if (key?.KeyChar == 'k') count++;

        return true;
    });
```

## Core Types

### `App`

Fluent builder for TUI applications. It runs an event loop: keys, ticks, resizes and posted actions
go through one queue, and a frame is drawn only when something happened. All callbacks run on the
thread that called `Run()`.

```csharp
App.Create()
    .UseRouter(router)                         // Optional: attach input router
    .OnSetup(app => { })                       // Optional: run once before the first frame
    .OnKey(key => false)                       // Optional: see keys first; true = handled
    .OnEvent(e => { })                         // Optional: KeyEvent, ResizeEvent, TickEvent
    .OnUpdate(() => { })                       // Optional: run each loop iteration before render
    .OnError(ex => ErrorAction.Continue)       // Optional: default restores the terminal and rethrows
    .WithTickRate(TimeSpan.FromMilliseconds(100)) // Optional: default 250 ms; null = redraw only on events
    .Render((buf, screen) => { })
    .Run();                                    // Or .RunWhile(() => condition), or await .RunAsync()
```

Ctrl+C always quits (unless `OnKey` takes it). In `RunWhile`, the condition runs once per key
(`Input.Poll()` returns that key) and once per wake-up without one; a key the condition takes
isn't passed to the router.

From any thread, `app.Post(action)` runs code on the UI thread, `app.RequestRedraw()` asks for a
frame and `app.Quit()` stops the loop. `app.RunTask(ct => WorkAsync(ct))` starts async work whose
`await`s resume on the UI thread; its exceptions go to `OnError`, and `ct` is `app.Stopping`.

While running, `Console.Out` and `Console.Error` are redirected into `app.Log` (a `LogBuffer` of
the last 1,000 lines) so stray writes can't corrupt the screen. Error lines are written to stderr
on exit. Turn this off with `.CaptureConsoleOutput(false)`.

### `Loadable<T>`

The state of an async load, for drawing loading, loaded and failed states:

```csharp
var services = new Loadable<IReadOnlyList<Service>>();

app.OnSetup(a => services.Load(a, ct => api.ListAsync(ct)))  // cancels any load in flight
   .Render((buf, screen) => services.Switch(
       idle:    () => { },
       loading: stale => DrawTable(buf, screen, stale),       // previous value, if any
       loaded:  list  => DrawTable(buf, screen, list),
       failed:  ex    => buf.SetString(0, 0, ex.Message, new Style(Color.Red))));
```

State changes happen on the UI thread and redraw. A newer load replaces an older one, and a failed
fetch sets `Failed` instead of going to `OnError`. Pass a `lifetime` token to ignore results once
its owner is gone.

### `InputRouter`

Routes keyboard input to named focus elements with Tab navigation.

```csharp
var router = new InputRouter()
    .Add("list", key => { /* handle list input */ })
    .Add("input", key => { /* handle text input */ })
    .Add("search", key => { /* handle typing */ }, capturesText: true)
    .Global(key => key.KeyChar == '?' && (showHelp = true)) // runs before focus routing, not while typing
    .WithModal(() => showHelp, _ => showHelp = false)
    .StartWith("input");

// In render:
if (router.IsFocused("list")) { /* highlight */ }
```

### `Layout`

Fluent builder for creating named layout regions. Sizes are computed with exact integer
arithmetic: regions never overlap, never leave the area, and rounding never drops a cell.

```csharp
var layout = Layout.Vertical(screen)
    .Add("header", Constraint.Length(3))    // Exactly 3 rows (shrunk only if fixed sizes don't fit)
    .Add("content", Constraint.Fill())      // Fill remaining space
    .Add("footer", Constraint.Length(1))
    .Spacing(1)                             // Optional: empty cells between regions
    .Build();                               // Optional: the indexer builds on first use

buf.DrawText(layout["header"].X, layout["header"].Y, "Title");
Widget.List().Items(items).Draw(buf, layout["content"]);

var columns = Layout.Split(area, Direction.Horizontal, Constraint.Percentage(30), Constraint.Fill());
```

| Constraint | Size |
|---|---|
| `Length(n)` | n cells |
| `Percentage(p)`, `Ratio(a, b)` | that share of the space, rounded down |
| `Min(n)` | at least n, then grows like `Fill(1)` |
| `Max(n)` | grows like `Fill(1)`, up to n |
| `Fill(weight)` | shares the leftover in proportion to `weight` |

When the fixed sizes don't fit, `Length`, `Percentage` and `Ratio` shrink proportionally first,
then `Min`. Space that nothing can grow into is placed by `.Flex(Flex.Start | End | Center |
SpaceBetween)`. `.Margin(new Margin(1))` insets the area first.

### `Widget`

Factory for fluent widget builders. Every widget draws with `Theme.Current`, measures text by display
width (truncating with `…`), and never throws, whatever the area.

```csharp
Widget.Table()
    .Block(Widget.Block().Title(" Services "))
    .Header("Name", "Status", "Version")
    .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12))  // §7 constraints
    .Rows(services, s => new Row(s.Name, new Line(s.Status, Theme.Current.Success), s.Version))
    .State(selection)                       // SelectionState: scrolled to keep the selection visible
    .Focused()
    .Draw(buf, area);
```

| Builder | What it draws |
|---|---|
| `Block()` | Borders (`Borders.All`, `Top`, …), `BorderType(BoxStyle)`, `Title`/`TitleBottom`, `Padding`, `Focused`. `Draw` returns the inner rect. |
| `Paragraph()` | `Text` with `Wrap(WrapMode.None/Word/Char)`, `Alignment`, `Scroll(n)` or `State(ScrollState)`. `Paragraph.MeasureLines` measures without drawing. |
| `List()` | `Items(list)` or `Items(list, format)` (only visible rows are formatted), `State(SelectionState)`, `HighlightSymbol("> ")`, `Title`. |
| `Table()` | `Header`, `Columns(constraints)`, `ColumnSpacing`, `Rows`, `State` (▲/▼ on `SortColumn`), `HighlightSymbol`. 100,000 rows cost the same per frame as 100. |
| `Scrollbar()` | `Vertical`/`Horizontal`, `ContentLength`, `Position`, `ViewportLength`. Hidden when the content fits. |
| `Gauge()` | `Ratio(0..1)` with eighth-cell precision and a percentage (or custom) `Label`. |
| `Sparkline()` | `Data(values)` as `▁▂▃▄▅▆▇█`, newest at the right; optional `Max`. |
| `Spinner()` | `Frames(SpinnerFrames.Braille/Line/Dots)` and a `Label`. Animates from the app's clock. |
| `Toasts()` | `Queue(toastQueue)`: up to 3 pop-ups in the top-right corner that expire on their own. |
| `StatusBar()` | `Left` and `Right` text; the right side wins when both don't fit. |
| `TextField()`, `CommandLine()` | Single-line input from `TextFieldState` / `CommandLineState`, scrolled by display width, with the terminal cursor shown. |
| `TabBar()` | `Tabs(...)` with `TabBarState`, scrolled to keep the selected tab visible. |
| `Button()`, `Checkbox()` | Themed controls. |
| `Modal()`, `Confirm()` | Centered dialogs; modal text wraps and scrolls with `State(ScrollState)`. |

In conhost (the classic Windows console), whose fonts lack eighth blocks, braille and rounded corners,
widgets switch to half and full blocks, the line spinner and single borders. Windows Terminal is unaffected.

Spinners and visible toasts make the app tick (every 100 ms and 250 ms) even with `WithTickRate(null)`,
and only while they're drawn. `toasts.Push(text, ToastLevel.Error)` is thread-safe; each toast shows
for 4 seconds (or the `duration` you pass) from when it first appears.

### `Theme`

The styles widgets draw with. `Theme.Default` leaves backgrounds to the terminal except for bars,
buttons and selections, so light terminal themes work. `Theme.Monochrome` uses only bold, underline
and reverse; the app picks it automatically when the terminal has no colors (`NO_COLOR`).

```csharp
app.WithTheme(Theme.Default with { Selection = new Style(Color.Black, Color.Yellow) });
```

`Theme.Current` is the running app's theme on its UI thread. `WithTheme` can be called while running.
Builders take `.Style(...)` (and `HighlightStyle`, `BorderStyle`, …) overrides, patched over the theme.

### State classes and `KeyMap`

Widget state is a mutable class that handles its own keys; widgets write render-time facts (viewport
size, scroll offset) back into it while drawing.

| State | Keys | Used by |
|---|---|---|
| `SelectionState` | ↑ ↓ j k PgUp PgDn Home End, plus `Next`/`Previous`/`PageDown`/`First`/`Last` | List, Table |
| `ScrollState` | ↑ ↓ j k PgUp PgDn Home End | Paragraph, Modal |
| `TextFieldState` | Any text (including non-ASCII and emoji), ← → Home End, Ctrl+← → by word, Backspace/Delete by character, Ctrl+Backspace or Ctrl+W (word), Ctrl+U / Ctrl+K (to start / end) | TextField |
| `CommandLineState` | The above, plus Enter (submit), Esc (clear), ↑ ↓ (history: immutable, capped at `MaxHistory`) | CommandLine |
| `TabBarState` | ← → h l | TabBar |

`KeyMap` binds keys to actions and produces hints and help text from the same list:

```csharp
var keys = new KeyMap()
    .Add("r", "Refresh", Refresh)
    .Add("j,down", "Down", () => selection.Next(count), group: "Navigation")
    .Add("ctrl+s", "Save", Save)
    .Add("q,esc", "Quit", app.Quit);

app.OnKey(keys.Handle);                                    // first match wins
Widget.StatusBar().Left(keys.Hints(40)).Draw(buf, status); // "r Refresh  j Down  ctrl+s Save  q Quit"
Widget.Modal().Title(" Keys ").Lines([.. keys.HelpLines()]).Draw(buf);
```

A binding is `(ctrl|alt|shift "+")* name`, where `name` is a printable character or one of `enter esc
tab backspace delete insert up down left right home end pgup pgdn space f1`–`f12`. A character binding
matches what was typed, so `"R"` and `"shift+r"` both mean Shift+R. `KeyBinding.Parse` throws
`FormatException` for anything else.

### `Buffer`

Your canvas. Draw to it, the terminal handles the rest.

```csharp
buf.SetString(x, y, "Hello", new Style(Color.Green).Bold());
buf.SetLine(x, y, new Line([new Span("Status: ", Style.Default), new Span("OK", new Style(Color.Green))]), maxWidth: 20);
buf.SetStyle(rect, new Style(Bg: Color.Blue));   // restyle an area, keep its text
buf.DrawBox(rect, ConsoleColor.Cyan);
buf.DrawBoxWithTitle(rect, "Title", ConsoleColor.Cyan);
buf.DrawProgressBar(rect, 0.75f);
buf.FillRect(rect, Cell.Empty with { Bg = Color.Blue });
```

Drawing never throws, whatever the coordinates: out-of-bounds writes are ignored. Text is measured
by display width, so CJK and emoji take two columns and are never split. Control characters in drawn
text (including ESC) are replaced with `�`, so data from an API can't inject terminal escape sequences.

### `Color`, `Style` and text

`Color` is the terminal default (`Color.Reset`), one of the 16 named colors (same names as
`ConsoleColor`, which converts implicitly), a 256-color index or RGB:

```csharp
Color.Cyan; Color.Indexed(208); Color.Rgb(255, 128, 0); Color.FromHex("#FF8000");
```

A `Style` is a patch: a null foreground or background keeps whatever is already in the cell, so text
drawn over a highlighted row keeps the highlight.

```csharp
var style = new Style(Fg: Color.Yellow).Bold().Underlined();
var merged = baseStyle.Patch(style);
```

`Span` (styled text), `Line` (spans) and `Text` (lines) build styled content. Strings convert
implicitly. `UnicodeWidth.Of(text)` and `UnicodeWidth.Truncate(text, maxWidth)` measure and cut text
by display width.

### `Terminal`

Manages the actual console. Double-buffers for performance: each frame, only the cells that changed
are sent to the backend, as one write.

```csharp
using var terminal = new Terminal();
terminal.Draw(buf => { /* render here */ });
terminal.Invalidate(); // Force full redraw next frame
```

MiniTui runs on the terminal's alternate screen, so your shell's screen and scrollback are left
untouched. The terminal is restored on normal exit, Ctrl+C, unhandled exceptions, and
SIGTERM/SIGHUP. Ctrl+C is delivered as a key and quits `App.Run`/`RunWhile`. An app started from
inside another app shares its terminal, and the outer app redraws itself when the inner one exits,
so no `Invalidate()` is needed.

Colors are downsampled to what the terminal supports (`NO_COLOR`, `COLORTERM`, `TERM`, ...; see
`ColorCapability.Detect`). Stdin and stdout must be a terminal: if either is redirected,
`TerminalNotSupportedException` is thrown.

### Backends and testing

`Terminal` draws through an `ITerminalBackend`:

- `AnsiBackend`, the default: ANSI/VT escape sequences, synchronized output, 16/256/truecolor.
- `LegacyConsoleBackend`: the `System.Console` API, used automatically on Windows consoles without VT support (16 colors).
- `TestBackend`: an in-memory screen for testing your own rendering.

```csharp
var backend = new TestBackend(40, 10);
using (var terminal = new Terminal(backend))
    terminal.Draw(buf => buf.SetString(0, 0, "Hello", Style.Default));

Assert.Equal("Hello", backend.Lines[0].TrimEnd());
Assert.Equal(Color.Reset, backend.CellAt(0, 0).Fg);
```

To test a whole app, give it a `TestBackend` and a `ScriptedInputSource` (`Enqueue`, `EnqueueText`):

```csharp
var backend = new TestBackend(40, 10);
var input = new ScriptedInputSource().EnqueueText("kkq");
var count = 0;

var app = App.Create().WithBackend(backend).WithInput(input).WithTickRate(null);
app.OnKey(key =>
    {
        if (key.KeyChar == 'k') count++;
        if (key.KeyChar == 'q') app.Quit();
        return true;
    })
    .Render((buf, _) => buf.SetString(0, 0, $"Count: {count}", Style.Default))
    .Run();

Assert.Equal(2, count);  // q quits before another frame is drawn; check screens with RunAsync while running
```

`WithTimeProvider(new FakeTimeProvider())` (from `Microsoft.Extensions.TimeProvider.Testing`) controls ticks.

### `Rect` & `Vec2`

Geometry helpers with layout utilities.

```csharp
var (top, bottom) = area.SplitHorizontal(10);
var (left, right) = area.SplitVertical(area.Width / 2);
var inner = area.Shrink(2); // 2-cell margin on all sides; never negative
var content = area.Inner(new Margin(Left: 1, Top: 2, Right: 1, Bottom: 0));
var visible = area.Intersect(screen);
foreach (var row in area.Rows()) { /* one Rect per row */ }
```

### `Input` & `InputHelpers`

`Input.Poll()` and the 0.7 helpers still work. The value-type states (`ListState`, `TextInputState`,
`CommandInputState`, `TabsState`) and their `InputHelpers.Handle*` functions are obsolete: use the state
classes above, which `HandleTextInput` and `HandleCommandInput` now delegate to.

```csharp
var key = Input.Poll();          // Non-blocking; inside RunWhile, the key the app is delivering
if (Input.IsQuit(key)) break;    // Check for q/Esc/Ctrl+C
```

### `ParsedCommand`

Parse colon-prefixed commands like `:add person note text`.

```csharp
var cmd = ParsedCommand.Parse(":add john Fixed the bug");
// cmd.Name == "add"
// cmd.GetArg(0) == "john"
// cmd.GetArgsFrom(1) == "Fixed the bug"
```

### `TimedMessage`

Status messages that auto-expire.

```csharp
var status = TimedMessage.Show("Saved!", seconds: 3);

// In render:
Widget.StatusBar()
    .Message(status, "Default status text")
    .Draw(buf, area);
```

### `Widgets`

Stateless widget-drawing functions from 0.7. Each one draws through the matching builder, so it is
themed and width-aware. `TextInput`, `CommandInput` and `Tabs` are obsolete.

```csharp
Widgets.Button(buf, rect, "Click Me", focused: true);
Widgets.Checkbox(buf, x, y, "Enable", isChecked: true);
Widgets.List(buf, rect, items, selectedIndex, scrollOffset, focused: true);
Widgets.StatusBar(buf, area, "Left text", "Right text");
Widgets.Modal(buf, "Title", lines);
```

## Extending

### Add a new widget

Widgets are just functions. Take styles from `Theme.Current` so yours follow the app's theme:

```csharp
public static void Badge(Buffer buf, int x, int y, string text, bool ok)
{
    var style = ok ? Theme.Current.Success : Theme.Current.Error;
    buf.SetString(x, y, $"[{text}]", style.Bold());
}
```

### Add a new drawing primitive

Extend `Buffer`:

```csharp
public static class BufferExtensions
{
    public static void DrawCircle(this Buffer buf, int cx, int cy, int r, Color fg)
    {
        for (var angle = 0.0; angle < Math.PI * 2; angle += 0.1)
        {
            var x = (int)(cx + r * 2 * Math.Cos(angle)); // *2 for aspect ratio
            var y = (int)(cy + r * Math.Sin(angle));
            buf.SetString(x, y, "*", new Style(fg));
        }
    }
}
```

## Box Styles

```csharp
buf.DrawBox(rect, ConsoleColor.White, BoxStyle.Single);   // ┌─┐
buf.DrawBox(rect, ConsoleColor.White, BoxStyle.Double);   // ╔═╗
buf.DrawBox(rect, ConsoleColor.White, BoxStyle.Rounded);  // ╭─╮
buf.DrawBox(rect, ConsoleColor.White, BoxStyle.Heavy);    // ┏━┓
```

## Project Structure

```
MiniTui/
├── src/
│   ├── MiniTui.sln
│   ├── MiniTui/
│   │   ├── App/            # App builder and loop, Loadable, LogBuffer, events
│   │   ├── Core/           # Color, Style, Cell, Buffer, Rect/Vec2/Margin
│   │   ├── Text/           # Span, Line, Text, sanitizing, Unicode width
│   │   ├── Layout/         # Layout builder, constraints and solver
│   │   ├── Terminal/       # Terminal, backends, ANSI encoder, session, input sources
│   │   ├── Input/          # KeyMap, KeyBinding and the widget state classes
│   │   ├── Widgets/        # Theme and one file per widget builder
│   │   ├── Input.cs        # Input, InputHelpers, InputRouter, commands, 0.7 state types
│   │   └── Widgets.cs      # Widget factory and the static Widgets functions
│   └── MiniTui.Example/
│       ├── Program.cs      # Demo menu and the 0.7 demos
│       ├── ServicesDemo.cs # Async table against a fake HTTP API
│       └── WidgetsDemo.cs  # Every widget, theme toggle, Unicode panel
├── tests/
│   └── MiniTui.Tests/      # xUnit tests
├── tools/
│   └── GenWidthTable.cs    # Generates the Unicode width table
└── README.md
```

## Building and Testing

```bash
dotnet build src/MiniTui.sln -warnaserror
dotnet test src/MiniTui.sln
```

The Unicode width table (`src/MiniTui/Text/UnicodeWidthTable.g.cs`) is generated from the Unicode
Character Database. To regenerate it, run from the repository root:

```bash
dotnet run tools/GenWidthTable.cs
```

## License

MIT. See [LICENSE](LICENSE).
