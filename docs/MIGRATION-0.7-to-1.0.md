# Migrating from MiniTui 0.7 to 1.0

MiniTui 1.0 has one way to do each thing. The 0.7 APIs listed here were obsolete in 0.8 and 0.9 and are
removed in 1.0. Each section shows what to use instead.

The quickest path is to upgrade to **0.9.0** first: every API below still compiles there, with an
`[Obsolete]` warning naming its replacement. Fix the warnings, then move to 1.0.

## Running the app

| 0.7 | 1.0 |
|---|---|
| `app.RunWhile(() => { var key = Input.Poll(); ... })` | `app.OnKey(key => ...)` or a `KeyMap`, then `app.Run()` (or `await app.RunAsync()`) |
| `Input.Poll()`, `Input.Read()` | Keys arrive through `OnKey`, `KeyMap.Handle` or a component's `HandleKey` |
| `app.WithFrameDelay(ms)` | `app.WithTickRate(TimeSpan.FromMilliseconds(ms))`; the app now redraws only when something happens |
| `app.Invalidate()` after a nested app exits | Not needed; nested apps share the terminal and the outer one redraws itself |

```csharp
// 0.7
App.Create().Render(Draw).RunWhile(() =>
{
    var key = Input.Poll();
    if (Input.IsQuit(key)) return false;
    if (key?.KeyChar == 'k') count++;
    return true;
});

// 1.0
var app = App.Create();
var keys = new KeyMap()
    .Add("k", "+1", () => count++)
    .Add("q,esc", "Quit", app.Quit);
app.OnKey(keys.Handle).Render(Draw).Run();
```

Ctrl+C always quits (unless `OnKey` handles it), so you don't need to check for it.

## Keys

| 0.7 | 1.0 |
|---|---|
| `Input.IsKey(key, ConsoleKey.Enter)` | `KeyBinding.Parse("enter").Matches(key)`, or a `KeyMap` binding |
| `Input.IsChar(key, 'r')` | `KeyBinding.Parse("r").Matches(key)` |
| `Input.IsQuit(key)` (q, Esc, Ctrl+C) | `keys.Add("q,esc", "Quit", app.Quit)`; Ctrl+C is built in |
| `InputHelpers.IsListNavigationKey(key)` | The return value of `SelectionState.HandleKey(key, count)` |
| `InputHelpers.IsTextEditKey(key)` | The return value of `TextFieldState.HandleKey(key)` |
| `InputHelpers.IsModalDismissKey(key)` | Overlays close on Esc by themselves (`Overlay.CloseOnEscape`) |

A `KeyMap` also gives you hints for a status bar (`keys.Hints(width)`) and help text (`keys.HelpLines()`).

## Widget state

The value-type states with `InputHelpers.Handle*` functions are replaced by classes that handle their own keys.
Widgets write the viewport and scroll offset back into them while drawing, so you no longer pass a visible height.

| 0.7 | 1.0 |
|---|---|
| `ListState`, `ListState.Empty` | `new SelectionState()` |
| `InputHelpers.HandleListNavigation(key, state, count, visibleHeight)` | `selection.HandleKey(key, count)` |
| `state.SelectedIndex`, `state.ScrollOffset` | `selection.Selected`, `selection.Offset` |
| `TextInputState`, `TextInputState.Empty` | `new TextFieldState()` |
| `InputHelpers.HandleTextInput(key, state)` | `field.HandleKey(key)`; it also accepts non-ASCII text, moves by grapheme and word, and supports Ctrl+W/U/K |
| `state.Value`, `state.CursorPos` | `field.Text`, `field.Cursor` |
| `CommandInputState`, `CommandInputState.WithHistory(list)` | `new CommandLineState(history)` (the history is copied into an `ImmutableList`) |
| `InputHelpers.HandleCommandInput(key, state)` → `CommandInputResult` | `line.HandleKey(key, out ParsedCommand? submitted)` |
| `TabsState`, `TabsState.Create(n)` | `new TabBarState(n)` |
| `InputHelpers.HandleTabsNavigation(key, state)` | `tabs.HandleKey(key)` |
| `ConfirmState`, `InputHelpers.HandleConfirmInput` | `await app.Overlays.ConfirmAsync(message)` |

```csharp
// 0.7
listState = InputHelpers.HandleListNavigation(key, listState, items.Count, 10);
Widget.List().Items(items).State(listState).Draw(buf, area);

// 1.0
selection.HandleKey(key, items.Count);
Widget.List().Items(items).State(selection).Draw(buf, area);
```

## Widgets

| 0.7 | 1.0 |
|---|---|
| `Widgets.Button(buf, rect, label, focused)` | `Widget.Button().Label(label).Focused(focused).Draw(buf, rect)` |
| `Widgets.Checkbox(buf, x, y, label, isChecked, focused)` | `Widget.Checkbox().Label(label).Checked(isChecked).Focused(focused).Draw(buf, rect)` |
| `Widgets.List(buf, rect, items, selected, offset, focused, title)` | `Widget.List().Items(items).State(selection).Focused(focused).Title(title).Draw(buf, rect)` |
| `Widgets.Table(buf, rect, headers, rows, selected)` | `Widget.Table().Header(...).Columns(...).Rows(rows, format).State(selection).Draw(buf, rect)` |
| `Widgets.StatusBar(buf, rect, left, right)` | `Widget.StatusBar().Left(left).Right(right).Draw(buf, rect)` |
| `Widgets.TextInput(...)` | `Widget.TextField().State(field).Block(Widget.Block()).Focused(focused).Draw(buf, rect)` |
| `Widgets.CommandInput(...)`, `Widget.CommandInput()` | `Widget.CommandLine().State(line).Prompt("> ").Draw(buf, rect)` |
| `Widgets.Tabs(...)`, `Widget.Tabs()` | `Widget.TabBar().Tabs("A", "B").State(tabs).Draw(buf, rect)` |
| `Widgets.Modal(...)`, `Widget.Modal()` | `await app.Overlays.ShowMessageAsync(title, text)`, or your own `Overlay` |
| `Widgets.Confirm(...)`, `Widget.Confirm()` | `await app.Overlays.ConfirmAsync(message, yes, no)` |
| `Widget.List().State(listState)` | `Widget.List().State(selection)` |

Widgets now draw with `Theme.Current` instead of hardcoded `ConsoleColor`s. Use `app.WithTheme(...)` to change
the colors, or each builder's `.Style(...)` to override one widget.

## Drawing on the buffer

| 0.7 | 1.0 |
|---|---|
| `buf.DrawText(x, y, text, fg, bg)` | `buf.SetString(x, y, text, new Style(fg, bg))` |
| `buf.DrawTextCentered(rect, text, fg)` | `Widget.Paragraph().Text(text).Alignment(Alignment.Center).Draw(buf, rect)` |
| `buf.DrawBox(rect, fg, BoxStyle.Rounded)` | `Widget.Block().BorderType(BoxStyle.Rounded).BorderStyle(new Style(fg)).Draw(buf, rect)` |
| `buf.DrawBoxWithTitle(rect, title, fg, titleFg)` | `Widget.Block().Title(title).Draw(buf, rect)` (it returns the inner rect) |
| `buf.DrawProgressBar(rect, 0.5f, fg)` | `Widget.Gauge().Ratio(0.5).FilledStyle(new Style(fg)).Draw(buf, rect)` |
| `buf.DrawHLine(x, y, n, '─', ConsoleColor.Gray)` | `buf.DrawHLine(x, y, n, '─', new Style(Color.Gray))` |
| `buf.DrawVLine(x, y, n, '│', ConsoleColor.Gray)` | `buf.DrawVLine(x, y, n, '│', new Style(Color.Gray))` |
| `new Cell('x', ConsoleColor.Red, ConsoleColor.Black)` | `new Cell("x", Color.Red, Color.Black, Modifiers.None)` |
| `cell.Char` | `cell.Symbol` (a whole grapheme cluster; wide characters are followed by a continuation cell) |
| `Cell.From('x')` | `Cell.Empty with { Symbol = "x" }` |

`ConsoleColor` converts to `Color` implicitly, so `new Style(ConsoleColor.Red)` works. Unlike 0.7, drawing
no longer paints a black background, and `Cell.Empty` uses the terminal's default colors.

## Layout

| 0.7 | 1.0 |
|---|---|
| `.Add("header", 3)` | `.Add("header", Constraint.Length(3))` |
| `.AddFill("body")` | `.Add("body", Constraint.Fill())` |

1.0 adds `Percentage`, `Ratio`, `Min`, `Max` and weighted `Fill`, plus `Spacing`, `Margin`, `Flex` and
`Layout.Split`. Fixed sizes that don't fit now shrink instead of pushing other regions out of the area, and
rounding never loses a cell. Adding two regions with the same name throws.

## Focus and modals

| 0.7 | 1.0 |
|---|---|
| `InputRouter`, `app.UseRouter(router)` | Screens and components: `app.Screens.Push(new MyScreen())` |
| `router.Add("name", handler)` | A `Component` child with `HandleKey`, added to the parent's `Focus` scope |
| `router.StartWith(name)`, `SetFocus`, `FocusNext` | `Focus.Focus(child)`, `Focus.Next()`; an unhandled Tab cycles the focus |
| `router.IsFocused("name")` | `ctx.Focused` in the child's `Render`, via `ctx.For(child)` |
| `router.WithModal(isActive, handler)` | `app.Overlays.Show(overlay)`; overlays are modal |
| `router.Global(handler)` | `app.Keys.Add(...)`, or a screen's `Keys` |
| `FocusManager` | `FocusScope` |

```csharp
// 0.7
var router = new InputRouter()
    .Add("name", key => nameState = InputHelpers.HandleTextInput(key, nameState))
    .Add("subscribe", key => { if (key.Key == ConsoleKey.Spacebar) subscribe = !subscribe; })
    .StartWith("name");
App.Create().UseRouter(router).Render(...).RunWhile(...);

// 1.0
sealed class FormScreen : Screen
{
    readonly TextFieldState name = new();
    readonly Component nameField, subscribeBox;
    bool subscribe;

    public FormScreen()
    {
        nameField = AddChild(Component.From(
            (buf, area, ctx) => Widget.TextField().State(name).Focused(ctx.Focused).Draw(buf, area),
            name.HandleKey, capturesText: true));
        subscribeBox = AddChild(Component.From(
            (buf, area, ctx) => Widget.Checkbox().Label("Subscribe").Checked(subscribe).Focused(ctx.Focused).Draw(buf, area),
            key =>
            {
                if (key.Key != ConsoleKey.Spacebar) return false;
                subscribe = !subscribe;
                return true;
            }));
        Focus.Add(nameField).Add(subscribeBox);
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var rows = Layout.Split(area, Direction.Vertical, Constraint.Length(1), Constraint.Length(1));
        nameField.Render(buf, rows[0], ctx.For(nameField));
        subscribeBox.Render(buf, rows[1], ctx.For(subscribeBox));
    }
}

var app = App.Create();
app.Screens.Push(new FormScreen());
app.Run();
```

Nested `App.Run()` calls for sub-views become screens: push a screen instead of starting another app, and
Esc (or `app.Screens.Pop()`) goes back.

## Behavior changes to know about

- The app draws a frame only when something happens (a key, a tick, a posted action, a resize). Animations need
  ticks (`WithTickRate`) or an explicit `app.RequestRedraw()`.
- All callbacks run on the thread that called `Run()`. From other threads, use `app.Post(...)`; for async work,
  `app.RunTask(...)` or `Loadable<T>`.
- `Console.WriteLine` while the app runs goes into `app.Log` instead of corrupting the screen.
- The app runs on the terminal's alternate screen, so the user's scrollback is untouched.
- An unhandled exception restores the terminal and is rethrown from `Run()`. `OnError` can keep the app going.
