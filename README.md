# MiniTui

A minimal, immediate-mode TUI (Text User Interface) framework for C#. No dependencies, no magic — you control everything.

## Philosophy

- **Immediate mode**: You describe the UI every frame. No hidden state, no retained widgets.
- **Double-buffered**: Only changed cells write to the terminal. Fast and flicker-free.
- **Composable**: Everything builds from simple primitives.
- **Understandable**: ~500 lines of code you can read and modify.

## Quick Start

```bash
dotnet run
```

This launches a demo app showcasing counters, lists, forms, progress bars, and a dashboard.

## Usage Pattern

```csharp
using MiniTui;

using var terminal = new Terminal();
var count = 0;

while (true)
{
    // Render - describe UI each frame
    terminal.Draw(buf =>
    {
        buf.DrawBox(new Rect(2, 1, 30, 5), ConsoleColor.Cyan);
        buf.DrawText(4, 2, $"Count: {count}", ConsoleColor.White);
        buf.DrawText(4, 3, "[j] dec  [k] inc  [q] quit", ConsoleColor.DarkGray);
    });

    // Input - you handle it
    var key = Input.Poll();
    if (Input.IsQuit(key)) break;
    
    if (key?.KeyChar == 'j') count--;
    if (key?.KeyChar == 'k') count++;

    Thread.Sleep(16); // ~60fps
}
```

## Core Types

### `Buffer`
Your canvas. Draw to it, the terminal handles the rest.

```csharp
buf.DrawText(x, y, "Hello", ConsoleColor.Green);
buf.DrawBox(rect, ConsoleColor.Cyan);
buf.DrawProgressBar(rect, 0.75f);
buf.FillRect(rect, new Cell('█', ConsoleColor.Blue, ConsoleColor.Black));
```

### `Terminal`
Manages the actual console. Double-buffers for performance.

```csharp
using var terminal = new Terminal();
terminal.Draw(buf => { /* render here */ });
```

### `Rect` & `Vec2`
Geometry helpers with layout utilities.

```csharp
var (top, bottom) = area.SplitHorizontal(10);
var (left, right) = area.SplitVertical(area.Width / 2);
var inner = area.Shrink(2); // 2-cell margin on all sides
```

### `Input`
Simple input handling.

```csharp
var key = Input.Poll();          // Non-blocking
if (Input.IsQuit(key)) break;    // Check for q/Esc/Ctrl+C
```

### `Widgets`
Stateless widget-drawing functions.

```csharp
Widgets.Button(buf, rect, "Click Me", focused: true);
Widgets.TextInput(buf, rect, value, focused: true, cursorPos: 5);
Widgets.Checkbox(buf, x, y, "Enable", isChecked: true);
Widgets.List(buf, rect, items, selectedIndex);
Widgets.StatusBar(buf, area, "Left text", "Right text");
```

## Extending

### Add a new widget

Widgets are just functions:

```csharp
public static void Spinner(Buffer buf, int x, int y)
{
    var frames = new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };
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
            buf.Set(x, y, new Cell('●', fg, ConsoleColor.Black));
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
├── MiniTui.csproj
├── Program.cs          # Demo application
├── README.md
└── src/
    ├── Geometry.cs     # Vec2, Rect
    ├── Buffer.cs       # Cell, Buffer, BoxStyle
    ├── Terminal.cs     # Terminal rendering
    ├── Input.cs        # Input handling, FocusManager
    └── Widgets.cs      # Widget drawing functions
```

## License

Do whatever you want with this. It's a starting point, not a product.
