# Drawing on the CellBuffer

Draw directly: cells, strings, colors and styled text.

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

## [`Color`](xref:MiniTui.Color), [`Style`](xref:MiniTui.Style) and text

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

[`Span`](xref:MiniTui.Span) (styled text), [`Line`](xref:MiniTui.Line) (spans) and [`Text`](xref:MiniTui.Text) (lines) build styled content. Strings convert
implicitly. `UnicodeWidth.Of(text)` and `UnicodeWidth.Truncate(text, maxWidth)` measure and cut text
by display width.

## Building text

A [`Text`](xref:MiniTui.Text) is a list of lines, a [`Line`](xref:MiniTui.Line) a list of styled spans. Strings
convert to all three, and a string with line breaks becomes several lines:

```csharp
Text plain = "first line\nsecond line";
var text = new Text(
[
    new Line("Plain"),
    new Line("Failed", Theme.Current.Error),
    new Line(["Name: ", new Span("auth", Style.Default.Bold())]),
]);
Widget.Paragraph().Text(text).Draw(buf, area);
```
