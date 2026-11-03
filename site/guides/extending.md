# Extending

Write your own widgets and drawing primitives.

## Add a new widget

Widgets are just functions. Take styles from `Theme.Current` so yours follow the app's theme:

```csharp
public static void Badge(Buffer buf, int x, int y, string text, bool ok)
{
    var style = ok ? Theme.Current.Success : Theme.Current.Error;
    buf.SetString(x, y, $"[{text}]", style.Bold());
}
```

## Add a new drawing primitive

Extend [`Buffer`](xref:MiniTui.Buffer):

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
