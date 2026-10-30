# MiniTui

A minimal, immediate-mode TUI (Text User Interface) framework for .NET, inspired by
[Ratatui](https://ratatui.rs). No runtime dependencies.

- **Immediate mode:** you describe the UI every frame. No retained widget tree, no diffing of components.
- **Fast, flicker-free output:** double-buffered; only changed cells are sent to the terminal, in one write.
- **Correct text:** display-width measuring for CJK and emoji, and no escape-sequence injection from data.
- **Async-first:** HTTP calls never block the UI; results come back on the UI thread.
- **Scales up:** screens, components, focus scopes, overlays and scoped key bindings for larger tools.
- **Safe terminal:** restored on exit, Ctrl+C, crashes, SIGTERM/SIGHUP and window close.

## Install

```bash
dotnet add package MiniTui
```

Requires .NET 10. Works in Windows Terminal, conhost (with reduced glyphs), macOS Terminal, iTerm2, tmux,
SSH sessions and Linux terminals. It's trim- and AOT-compatible.

## Quick Start

```bash
dotnet run --project src/MiniTui.Example
```

This launches one app with a stack of screens: a menu, and a demo screen for counters, lists, a form,
progress bars, a dashboard, a command line and tabs, plus:

- **Services**: an async table of 500 services from a fake HTTP API (slow, and failing 20% of the time),
  with refresh and cancellation, a detail screen (Enter), delete with a confirm dialog (`d`) and error toasts.
- **Widgets**: every widget on one screen, a theme toggle (`t`) and a Unicode sample panel.

Esc or `q` goes back, `?` lists the keys for the current screen, and Ctrl+C quits.

## A small app

A small app is one render callback and a `KeyMap`:

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

## Async data and HTTP

Load data with a `Loadable<T>` and draw each state. Loads run off the UI thread; their results, and every
`await` in `RunTask`, come back on it.

```csharp
var services = new Loadable<IReadOnlyList<ServiceDto>>();
var selection = new SelectionState();
var toasts = new ToastQueue();
var app = App.Create();

var keys = new KeyMap()
    .Add("r", "Refresh", () => services.Load(app, ct => api.ListAsync(ct)))   // cancels a load in flight
    .Add("j,down", "Down", () => selection.Next(services.Value?.Count ?? 0))
    .Add("k,up", "Up", () => selection.Previous(services.Value?.Count ?? 0))
    .Add("q,ctrl+c", "Quit", () => app.Quit());

app.OnSetup(a => services.Load(a, ct => api.ListAsync(ct)))
    .OnKey(keys.Handle)
    .OnError(ex => { toasts.Push(ex.Message, ToastLevel.Error); return ErrorAction.Continue; })
    .Render((buf, screen) =>
    {
        var layout = Layout.Vertical(screen).Add("body", Constraint.Fill()).Add("status", Constraint.Length(1)).Build();

        services.Switch(
            idle:    () => Widget.Paragraph().Text("Press r to load").Draw(buf, layout["body"]),
            loading: stale => DrawTable(buf, layout["body"], stale),     // the previous value, if any
            loaded:  list  => DrawTable(buf, layout["body"], list),
            failed:  ex    => Widget.Paragraph().Text(new Line(ex.Message, Theme.Current.Error)).Wrap(WrapMode.Word)
                                  .Block(Widget.Block().Title(" Error ")).Draw(buf, layout["body"]));

        Widget.StatusBar().Left(keys.Hints(screen.Width / 2)).Draw(buf, layout["status"]);
        Widget.Toasts().Queue(toasts).Draw(buf, screen);
    })
    .Run();

void DrawTable(Buffer buf, Rect area, IReadOnlyList<ServiceDto>? list) =>
    Widget.Table()
        .Block(Widget.Block().Title(" Services "))
        .Header("Name", "Status", "Version")
        .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12))
        .Rows(list ?? [], s => new Row(s.Name, new Line(s.Status, s.Healthy ? Theme.Current.Success : Theme.Current.Error), s.Version))
        .State(selection)
        .Focused()
        .Draw(buf, area);
```

`Loadable<T>` holds `Status` (`Idle`, `Loading`, `Loaded`, `Failed`), the last `Value` (kept while
reloading and after a failure), `Error` and `LoadedAt`. State changes happen on the UI thread and redraw.
A newer load replaces an older one, and a failed fetch sets `Failed` instead of going to `OnError`. Pass a
`lifetime` token (or use `Component.Load`) to ignore results once their owner is gone.

## Larger apps: screens and components

A larger app is a stack of screens built from components:

```csharp
var app = App.Create().WithTheme(Theme.Default);   // status bar with key hints and a breadcrumb
app.Keys.Add("q", "Quit", app.Quit);               // global; "?" help and "ctrl+c" quit are built in
app.Screens.Push(new ServicesScreen(api));
app.Run();

sealed class ServicesScreen(ServicesClient api) : Screen
{
    readonly Loadable<IReadOnlyList<ServiceDto>> services = new();
    readonly SelectionState selection = new();
    public override string Title => "Services";

    protected override void OnMount()
    {
        Keys.Add("r", "Refresh", () => Load(services, api.ListAsync))
            .Add("enter", "Open", () => App.Screens.Push(new DetailScreen(api, services.Value![selection.Selected])))
            .Add("d", "Delete", () => App.RunTask(_ => DeleteAsync()));
        Load(services, api.ListAsync);                 // cancelled automatically when the screen is popped
    }

    public override bool HandleKey(ConsoleKeyInfo key) => selection.HandleKey(key, services.Value?.Count ?? 0);

    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        Widget.Table().Header("Name", "Status").Rows(services.Value ?? [], s => new Row(s.Name, s.Status))
            .State(selection).Focused(ctx.Focused).Draw(buf, area);

    async Task DeleteAsync()
    {
        var svc = services.Value![selection.Selected];
        if (!await App.Overlays.ConfirmAsync($"Delete {svc.Name}?")) return;
        await api.DeleteAsync(svc.Id, Lifetime);       // resumes on the UI thread
        App.Toasts.Push($"Deleted {svc.Name}", ToastLevel.Success);
        Load(services, api.ListAsync);
    }
}
```

Components are plain classes that hold state, handle keys and render every frame; there's no retained
widget tree. A parent renders its children explicitly with `child.Render(buf, rect, ctx.For(child))`.

```csharp
sealed class SearchBox : Component
{
    readonly TextFieldState text = new();
    public override bool CapturesText => true;           // typing "q" here doesn't trigger global "q"
    public override bool HandleKey(ConsoleKeyInfo key) => text.HandleKey(key);
    public override bool HandlePaste(string pasted) => text.Paste(pasted);
    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        Widget.TextField().State(text).Focused(ctx.Focused).Block(Widget.Block().Title(" Search ")).Draw(buf, area);
}

sealed class FilesScreen : Screen
{
    readonly SearchBox search;
    readonly Component list;

    public FilesScreen()
    {
        search = AddChild(new SearchBox());
        list = AddChild(Component.From((buf, area, ctx) => { /* draw */ }, key => false));
        Focus.Add(list).Add(search);                       // Tab cycles; the first one starts focused
        Keys.Add("ctrl+r", "Reload", Reload);
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var rows = Layout.Split(area, Direction.Vertical, Constraint.Length(3), Constraint.Fill());
        search.Render(buf, rows[0], ctx.For(search));
        list.Render(buf, rows[1], ctx.For(list));
    }

    void Reload() { }
}
```

- **Lifecycle:** `OnMount` runs when a component joins a mounted screen (parents before children);
  `OnUnmount` when it leaves (children first, after `Lifetime` is cancelled). `OnActivated`/`OnDeactivated`
  run when its screen becomes or stops being the top one. `Load(loadable, fetch)` ties a load to `Lifetime`,
  so popping a screen cancels its loads and ignores their results.
- **Keys** go to the `App.OnKey` hook; then, if an overlay is open, only to it (Esc closes it); otherwise
  along the focus chain from the innermost focused component outwards (`HandleKey`, then `Keys`), then
  `App.Keys`; finally an unhandled Esc pops the screen. An unhandled Tab cycles the innermost focus scope.
- **Chrome:** the bottom row shows hints for the bindings in scope (an inner `r` hides an outer `r`) and
  the screen titles. Turn it off with `WithChrome(statusBar: false)`.
- **Overlays:** `app.Overlays.ConfirmAsync(message)`, `ShowMessageAsync(title, text)`, `ShowHelp()`, or
  `Show(new CommandLineOverlay(registry))` and your own `Overlay` subclasses.

## Layout

Fluent builder for creating named layout regions. Sizes are computed with exact integer
arithmetic: regions never overlap, never leave the area, and rounding never drops a cell.

```csharp
var layout = Layout.Vertical(screen)
    .Add("header", Constraint.Length(3))    // Exactly 3 rows (shrunk only if fixed sizes don't fit)
    .Add("content", Constraint.Fill())      // Fill remaining space
    .Add("footer", Constraint.Length(1))
    .Spacing(1)                             // Optional: empty cells between regions
    .Build();                               // Optional: the indexer builds on first use

buf.SetString(layout["header"].X, layout["header"].Y, "Title", Style.Default.Bold());
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

## Widgets

Factory for fluent widget builders. Every widget draws with `Theme.Current`, measures text by display
width (truncating with `…`), and never throws, whatever the area.

```csharp
Widget.Table()
    .Block(Widget.Block().Title(" Services "))
    .Header("Name", "Status", "Version")
    .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12))  // layout constraints
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

In conhost (the classic Windows console), whose fonts lack eighth blocks, braille and rounded corners,
widgets switch to half and full blocks, the line spinner and single borders. Windows Terminal is unaffected.

Spinners and visible toasts make the app tick (every 100 ms and 250 ms) even with `WithTickRate(null)`,
and only while they're drawn. `toasts.Push(text, ToastLevel.Error)` is thread-safe; each toast shows
for 4 seconds (or the `duration` you pass) from when it first appears.

## Theming

The styles widgets draw with. `Theme.Default` leaves backgrounds to the terminal except for bars,
buttons and selections, so light terminal themes work. `Theme.Monochrome` uses only bold, underline
and reverse. When the terminal has no colors (`NO_COLOR`), the app uses it in place of `Theme.Default`,
even if you chose `Theme.Default` explicitly, so the selection stays visible; custom themes are used as is.

```csharp
app.WithTheme(Theme.Default with { Selection = new Style(Color.Black, Color.Yellow) });
```

`Theme.Current` is the running app's theme on its UI thread. `WithTheme` can be called while running.
Builders take `.Style(...)` (and `HighlightStyle`, `BorderStyle`, …) overrides, patched over the theme.

## Keys and widget state

Widget state is a mutable class that handles its own keys; widgets write render-time facts (viewport
size, scroll offset) back into it while drawing.

| State | Keys | Used by |
|---|---|---|
| `SelectionState` | ↑ ↓ j k PgUp PgDn Home End, plus `Next`/`Previous`/`PageDown`/`First`/`Last` | List, Table |
| `ScrollState` | ↑ ↓ j k PgUp PgDn Home End | Paragraph, MessageDialog |
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
_ = app.Overlays.ShowMessageAsync("Keys", string.Join("\n", keys.HelpLines()));
```

A binding is `(ctrl|alt|shift "+")* name`, where `name` is a printable character or one of `enter esc
tab backspace delete insert up down left right home end pgup pgdn space f1`–`f12`. A character binding
matches what was typed, so `"R"` and `"shift+r"` both mean Shift+R. `KeyBinding.Parse` throws
`FormatException` for anything else.

## Drawing on the `Buffer`

Your canvas. Draw to it, the terminal handles the rest.

```csharp
buf.SetString(x, y, "Hello", new Style(Color.Green).Bold());
buf.SetLine(x, y, new Line([new Span("Status: ", Style.Default), new Span("OK", new Style(Color.Green))]), maxWidth: 20);
buf.SetStyle(rect, new Style(Bg: Color.Blue));   // restyle an area, keep its text
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

## The terminal

Manages the actual console. Double-buffers for performance: each frame, only the cells that changed
are sent to the backend, as one write.

```csharp
using var terminal = new Terminal();
terminal.Draw(buf => { /* render here */ });
terminal.Invalidate(); // Force full redraw next frame
```

MiniTui runs on the terminal's alternate screen, so your shell's screen and scrollback are left
untouched. The terminal is restored on normal exit, Ctrl+C, unhandled exceptions, and
SIGTERM/SIGHUP. Ctrl+C is delivered as a key and quits `App.Run`. An app started from
inside another app shares its terminal, and the outer app redraws itself when the inner one exits,
so no `Invalidate()` is needed.

Colors are downsampled to what the terminal supports (`NO_COLOR`, `COLORTERM`, `TERM`, ...; see
`ColorCapability.Detect`). Stdin and stdout must be a terminal: if either is redirected,
`TerminalNotSupportedException` is thrown.

### Keyboard input

MiniTui puts the terminal in raw mode (on Windows, the console in VT input mode) and decodes its input
itself (`TerminalInputSource`, the default). Esc, Alt combinations, function keys and arrows with
Shift/Ctrl/Alt arrive reliably and the same way on every platform, and a fast burst of keys never
produces a stray Esc. Keys that terminals can't encode, such as Ctrl+Enter, Ctrl+Tab and Ctrl+digits,
arrive without the Ctrl.

- **Esc** is recognized 50 ms after it's pressed, because terminals send other keys as sequences that
  start with Esc. Change it with `app.WithInput(new TerminalInputSource(TimeSpan.FromMilliseconds(100)))`.
- **Ctrl+C** quits. **Ctrl+Z**, Ctrl+\, Ctrl+S and Ctrl+Q reach the app as keys instead of suspending
  the process or pausing output.
- **macOS:** raw input is tested in CI on a pseudo-terminal, but not yet by hand in Terminal.app or
  iTerm2. Terminal.app sends Alt (Option) combinations only with "Use Option as Meta key" turned on.
- **Paste** arrives as one call, not a key per character, so a pasted line break doesn't submit and pasted
  letters don't fire bindings. A component accepts pastes by overriding `HandlePaste`; for a text field,
  `public override bool HandlePaste(string text) => State.Paste(text);` inserts the paste as one line.
  A paste nothing handles is dropped. `App.OnPaste` sees pastes first, and `ComponentHarness.Paste` tests
  them. Conhost (the classic Windows console) can't mark pastes, so there MiniTui pastes the clipboard
  itself on Ctrl+V; right-click paste in conhost still arrives as keys. With `MINITUI_INPUT=console`,
  pasted text arrives as keys, as in 1.0.
- If a terminal's keys decode wrongly, set `MINITUI_INPUT=console` to use the `Console.ReadKey` path,
  and please report it: the Example's **Input** demo shows exactly how each key was decoded.

### Backends

`Terminal` draws through an `ITerminalBackend`:

- `AnsiBackend`, the default: ANSI/VT escape sequences, synchronized output, 16/256/truecolor.
- `LegacyConsoleBackend`: the `System.Console` API, used automatically on Windows consoles without VT support (16 colors).
- `TestBackend`: an in-memory screen for tests (see [Testing](#testing)).

## Testing

### Screens and components

Test a screen with `ComponentHarness`, which runs the app one step at a time on a test terminal:

```csharp
using var harness = new ComponentHarness(new ServicesScreen(fakeApi), width: 80, height: 24);
harness.Press("j j enter");                       // KeyBinding names
Assert.IsType<DetailScreen>(harness.App.Screens.Top);
harness.Type("hello");
Assert.Contains(harness.Lines, l => l.Contains("hello"));
```

Each call handles the keys, posted work and `await` continuations until nothing is in flight, then draws
a frame.

### Whole apps and rendering

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

To test your own drawing code, draw into a `Terminal` over a `TestBackend`:

```csharp
var backend = new TestBackend(40, 10);
using (var terminal = new Terminal(backend))
    terminal.Draw(buf => buf.SetString(0, 0, "Hello", Style.Default));

Assert.Equal("Hello", backend.Lines[0].TrimEnd());
Assert.Equal(Color.Reset, backend.CellAt(0, 0).Fg);
```

## Reference

### `App`

Fluent builder for TUI applications. It runs an event loop: keys, ticks, resizes and posted actions
go through one queue, and a frame is drawn only when something happened. All callbacks run on the
thread that called `Run()`.

```csharp
App.Create()
    .OnSetup(app => { })                       // Optional: run once before the first frame
    .OnKey(key => false)                       // Optional: see keys first; true = handled
    .OnEvent(e => { })                         // Optional: KeyEvent, ResizeEvent, TickEvent
    .OnUpdate(() => { })                       // Optional: run each loop iteration before render
    .OnError(ex => ErrorAction.Continue)       // Optional: default restores the terminal and rethrows
    .WithTickRate(TimeSpan.FromMilliseconds(100)) // Optional: default 250 ms; null = redraw only on events
    .Render((buf, screen) => { })
    .Run();                                    // Or await .RunAsync()
```

Ctrl+C always quits (unless `OnKey` takes it). An app uses either a `Render` callback or `Screens`;
`Overlays` (dialogs) and `Toasts` work with both.

From any thread, `app.Post(action)` runs code on the UI thread, `app.RequestRedraw()` asks for a
frame and `app.Quit()` stops the loop. `app.RunTask(ct => WorkAsync(ct))` starts async work whose
`await`s resume on the UI thread; its exceptions go to `OnError`, and `ct` is `app.Stopping`.

While running, `Console.Out` and `Console.Error` are redirected into `app.Log` (a `LogBuffer` of
the last 1,000 lines) so stray writes can't corrupt the screen. Error lines are written to stderr
on exit. Turn this off with `.CaptureConsoleOutput(false)`.

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

### `CommandRegistry`

Commands with aliases, usage, descriptions and subcommands, for a `:` command line:

```csharp
var commands = CommandRegistry.Create()
    .Add("add", "a").Usage("<text>").Description("Add a note").Handler(cmd => notes.Add(cmd.ArgsAsString))
    .Add("quit", "q").Handler(_ => CommandResult.Quit())
    .Build();

app.Keys.Add(":", "Command", () => app.Overlays.Show(new CommandLineOverlay(commands)));
```

## Upgrading from 0.7

1.0 removes the 0.7 APIs that were obsolete in 0.8 and 0.9: `RunWhile`/`Input.Poll`, `InputRouter`, the
value-type widget states, the static `Widgets` functions and the `ConsoleColor` drawing methods.
[docs/MIGRATION-0.7-to-1.0.md](docs/MIGRATION-0.7-to-1.0.md) maps each one to its replacement.

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
Widget.Block().BorderType(BoxStyle.Single).Draw(buf, rect);   // ┌─┐
Widget.Block().BorderType(BoxStyle.Double).Draw(buf, rect);   // ╔═╗
Widget.Block().BorderType(BoxStyle.Rounded).Draw(buf, rect);  // ╭─╮
Widget.Block().BorderType(BoxStyle.Heavy).Draw(buf, rect);    // ┏━┓
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
│   │   ├── Components/     # Component, FocusScope, screens, overlays, dialogs, key routing, ComponentHarness
│   │   ├── Input/          # KeyMap, KeyBinding, commands and the widget state classes
│   │   ├── Widgets/        # Theme, the Widget factory and one file per widget builder
│   │   └── PublicAPI.*.txt # The tracked public API (Microsoft.CodeAnalysis.PublicApiAnalyzers)
│   └── MiniTui.Example/
│       ├── Program.cs      # Creates the app and pushes the menu screen
│       └── Screens/        # One screen per demo, plus shared controls
├── tests/
│   └── MiniTui.Tests/      # xUnit tests
├── docs/                   # SPEC.md (1.0), SPEC-1.x.md (post-1.0 roadmap) and the 0.7 → 1.0 migration guide
├── tools/
│   ├── GenWidthTable.cs    # Generates the Unicode width table
│   └── ssh-test/           # An SSH server in Docker for manual QA over SSH
└── README.md
```

## Building and Testing

```bash
dotnet build src/MiniTui.sln -warnaserror
dotnet test src/MiniTui.sln
```

CI builds and tests on Ubuntu, Windows and macOS and requires at least 85% line coverage of `src/MiniTui`.
A change to the public API fails the build (RS0016/RS0017) until `PublicAPI.Unshipped.txt` is updated.

The Unicode width table (`src/MiniTui/Text/UnicodeWidthTable.g.cs`) is generated from the Unicode
Character Database. To regenerate it, run from the repository root:

```bash
dotnet run tools/GenWidthTable.cs
```

To check input over SSH (with optional network delay), `tools/ssh-test/run.sh` starts an SSH server in Docker
with the Example in it; see [tools/ssh-test/README.md](tools/ssh-test/README.md).

## License

MIT. See [LICENSE](LICENSE).
