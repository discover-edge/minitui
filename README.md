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
buf.DrawText(x, y, "Hello", ConsoleColor.Green);
buf.DrawBox(rect, ConsoleColor.Cyan);
buf.DrawBoxWithTitle(rect, "Title", ConsoleColor.Cyan);
buf.DrawProgressBar(rect, 0.75f);
buf.FillRect(rect, new Cell(' ', ConsoleColor.Blue, ConsoleColor.Black));
```

### `Terminal`

Manages the actual console. Double-buffers for performance.

```csharp
using var terminal = new Terminal();
terminal.Draw(buf => { /* render here */ });
terminal.Invalidate(); // Force full redraw next frame
```

### `Rect` & `Vec2`

Geometry helpers with layout utilities.

```csharp
var (top, bottom) = area.SplitHorizontal(10);
var (left, right) = area.SplitVertical(area.Width / 2);
var inner = area.Shrink(2); // 2-cell margin on all sides
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
    var frames = new[] { '|', '/', '-', '\\' };
    var frame = (int)(DateTime.Now.Ticks / 1000000) % frames.Length;
    buf.Set(x, y, new Cell(frames[frame], ConsoleColor.Cyan, ConsoleColor.Black));
}
```

### Add a new drawing primitive

Extend `Buffer`:

```csharp
public static class BufferExtensions
{
    public static void DrawCircle(this Buffer buf, int cx, int cy, int r, ConsoleColor fg)
    {
        for (var angle = 0.0; angle < Math.PI * 2; angle += 0.1)
        {
            var x = (int)(cx + r * 2 * Math.Cos(angle)); // *2 for aspect ratio
            var y = (int)(cy + r * Math.Sin(angle));
            buf.Set(x, y, new Cell('*', fg, ConsoleColor.Black));
        }
    }
}
```

## Box Styles

```csharp
buf.DrawBox(rect, ConsoleColor.White, BoxStyle.Single);   // +-+
buf.DrawBox(rect, ConsoleColor.White, BoxStyle.Double);   // +=+
buf.DrawBox(rect, ConsoleColor.White, BoxStyle.Rounded);  // ,-,
buf.DrawBox(rect, ConsoleColor.White, BoxStyle.Heavy);    // +~+
```

## Project Structure

```
MiniTui/
+-- src/
|   +-- MiniTui/
|   |   +-- App.cs          # Fluent app builder
|   |   +-- Buffer.cs       # Cell, Buffer, BoxStyle
|   |   +-- Geometry.cs     # Vec2, Rect, Layout
|   |   +-- Input.cs        # Input, InputHelpers, InputRouter, state types
|   |   +-- Terminal.cs     # Terminal rendering
|   |   +-- Widgets.cs      # Widget drawing functions and builders
|   +-- MiniTui.Example/
|       +-- Program.cs      # Demo application
+-- README.md
```

## License

Do whatever you want with this. It's a starting point, not a product.
