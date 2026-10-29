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

This launches a demo app showcasing counters, lists, forms, progress bars, and a dashboard.

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

Fluent builder for TUI applications. Handles the main loop, input routing, and modal auto-invalidation.

```csharp
App.Create()
    .UseRouter(router)           // Optional: attach input router
    .OnSetup(() => { })          // Optional: run once before loop
    .OnUpdate(() => { })         // Optional: run each frame before render
    .WithFrameDelay(16)          // Optional: milliseconds between frames (default 16)
    .Render((buf, screen) => { })
    .Run();                      // Or .RunWhile(() => condition)
```

### `InputRouter`

Routes keyboard input to named focus elements with Tab navigation.

```csharp
var router = new InputRouter()
    .Add("list", key => { /* handle list input */ })
    .Add("input", key => { /* handle text input */ })
    .WithModal(() => showHelp, _ => showHelp = false)
    .StartWith("input");

// In render:
if (router.IsFocused("list")) { /* highlight */ }
```

### `Layout`

Fluent builder for creating named layout regions.

```csharp
var layout = Layout.Vertical(screen)
    .Add("header", 3)      // Fixed 3 rows
    .AddFill("content")    // Fill remaining space
    .Add("footer", 1)      // Fixed 1 row
    .Build();

buf.DrawText(layout["header"].X, layout["header"].Y, "Title");
Widget.List().Items(items).Draw(buf, layout["content"]);
```

### `Widget`

Factory for fluent widget builders.

```csharp
Widget.List()
    .Items(items)
    .State(listState)
    .Focused(router.IsFocused("list"))
    .Title("My List")
    .Draw(buf, area);

Widget.CommandInput()
    .State(commandState)
    .Focused()
    .Draw(buf, inputArea);

Widget.StatusBar()
    .Left("Status message")
    .Right("12:34:56")
    .Draw(buf, footerArea);

Widget.Modal()
    .Title("Help")
    .Lines(helpLines)
    .Draw(buf);
```

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

`ScriptedInputSource` (`Enqueue`, `EnqueueText`) provides scripted key presses through the
`IInputSource` interface; the app loop will accept one in a future release.

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

Input handling and state management.

```csharp
var key = Input.Poll();          // Non-blocking
if (Input.IsQuit(key)) break;    // Check for q/Esc/Ctrl+C

// State types
var textState = TextInputState.Empty;
var listState = ListState.Empty;
var cmdState = CommandInputState.Empty;

// Input helpers
textState = InputHelpers.HandleTextInput(key, textState);
listState = InputHelpers.HandleListNavigation(key, listState, itemCount, visibleHeight);
var result = InputHelpers.HandleCommandInput(key, cmdState);
if (result.Command is { } cmd) ProcessCommand(cmd);
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

Stateless widget-drawing functions (alternative to fluent builders).

```csharp
Widgets.Button(buf, rect, "Click Me", focused: true);
Widgets.TextInput(buf, rect, value, focused: true, cursorPos: 5);
Widgets.Checkbox(buf, x, y, "Enable", isChecked: true);
Widgets.List(buf, rect, items, selectedIndex, scrollOffset, focused: true);
Widgets.StatusBar(buf, area, "Left text", "Right text");
Widgets.Modal(buf, "Title", lines);
```

## Extending

### Add a new widget

Widgets are just functions:

```csharp
public static void Spinner(Buffer buf, int x, int y)
{
    var frames = new[] { "|", "/", "-", "\\" };
    var frame = (int)(DateTime.Now.Ticks / 1000000) % frames.Length;
    buf.SetString(x, y, frames[frame], new Style(Color.Cyan));
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
│   │   ├── App.cs          # Fluent app builder
│   │   ├── Core/           # Color, Style, Cell, Buffer, Rect/Vec2/Margin
│   │   ├── Text/           # Span, Line, Text, sanitizing, Unicode width
│   │   ├── Layout/         # Layout builder
│   │   ├── Terminal/       # Terminal, backends, ANSI encoder, session, input sources
│   │   ├── Input.cs        # Input, InputHelpers, InputRouter, state types
│   │   └── Widgets.cs      # Widget drawing functions and builders
│   └── MiniTui.Example/
│       └── Program.cs      # Demo application
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
