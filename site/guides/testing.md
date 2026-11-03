# Testing

Test screens, components and whole apps without a terminal.

## Screens and components

Test a screen with [`ComponentHarness`](xref:MiniTui.ComponentHarness), which runs the app one step at a time on a test terminal:

```csharp
using var harness = new ComponentHarness(new ServicesScreen(fakeApi), width: 80, height: 24);
harness.Press("j j enter");                       // KeyBinding names
Assert.IsType<DetailScreen>(harness.App.Screens.Top);
harness.Type("hello");
Assert.Contains(harness.Lines, l => l.Contains("hello"));
```

Each call handles the keys, posted work and `await` continuations until nothing is in flight, then draws
a frame.

`ComponentHarness` makes its own app, so the setup in your `Program.cs` (global `app.Keys`, a menu bar, a
theme) isn't there. Add what a test needs to `harness.App`, or move the setup into a method both call.

## Whole apps and rendering

To test a whole app, give it a [`TestBackend`](xref:MiniTui.TestBackend) and a [`ScriptedInputSource`](xref:MiniTui.ScriptedInputSource) (`Enqueue`, `EnqueueText`):

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

To test your own drawing code, draw into a [`Terminal`](xref:MiniTui.Terminal) over a `TestBackend`:

```csharp
var backend = new TestBackend(40, 10);
using (var terminal = new Terminal(backend))
    terminal.Draw(buf => buf.SetString(0, 0, "Hello", Style.Default));

Assert.Equal("Hello", backend.Lines[0].TrimEnd());
Assert.Equal(Color.Reset, backend.CellAt(0, 0).Fg);
```
