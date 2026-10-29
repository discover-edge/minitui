# MiniTui 1.0 — Production Readiness Spec

| | |
|---|---|
| **Status** | Ready for implementation |
| **Baseline** | `discover-edge/minitui` @ `0ccd083` (v0.7.0) |
| **Target** | v1.0.0 |
| **Primary use case** | Interactive team TUIs (k9s-style panes, tables, commands) backed by async HTTP calls to C# APIs |
| **Runtime** | .NET 10 (`net10.0`), Windows Terminal / conhost (VT), Linux, macOS, WSL, tmux, SSH |

---

## 0. Instructions for Claude Code

Read this whole document before writing code.

1. **Work phase by phase** (§11), in order. Each phase is one branch and one PR: `phase-N-<slug>`. Don't start phase N+1 until phase N's exit criteria pass.
2. **Fix defects test-first.** For every defect in §2, first write a test that reproduces it and fails, then fix it. Reference the defect ID in the test name, e.g. `D6_List_DoesNotThrow_WhenInnerWidthIsZero`.
3. **Gate.** `dotnet build src/MiniTui.sln -warnaserror && dotnet test src/MiniTui.sln` must pass before you call a phase done.
4. **Compatibility.** Through 0.9, existing public APIs must keep compiling unless this spec explicitly marks them changed. To replace an API, keep the old one as a shim with `[Obsolete("Use X instead.")]`, and have the shim delegate to the new code rather than keep its own logic. Phase 7 removes every shim before 1.0 is tagged, so 1.0 has exactly one way to do each thing (§9.7, Open Question 7).
5. **No runtime dependencies** in `src/MiniTui`. Test and benchmark projects may take dependencies.
6. **Ambiguity.** If this spec doesn't settle a behavior, match Ratatui's behavior and note the choice in the PR description under "Spec interpretations". If you'd deviate from a **Decision** in this spec, stop and ask.
7. **Every PR** updates `CHANGELOG.md` (Keep a Changelog format) and any README sections it affects.
8. **Open questions** (§13) have defaults. Use the default unless the maintainer has answered otherwise.
9. **Environment.** You run in WSL (Linux). Windows-only code paths (§5.5) must compile on all platforms, be guarded with `OperatingSystem.IsWindows()`, and be listed in the PR for manual verification on Windows.

---

## 1. Goals and non-goals

### Goals
- **G1. Safe terminal.** The terminal is always restored: on normal exit, Ctrl+C, unhandled exception, SIGTERM/SIGHUP, and window close.
- **G2. Async-first.** HTTP calls never block the UI. Results reach the UI thread through a supported API.
- **G3. Correct rendering.** Diffed ANSI output, no flicker, correct Unicode width, 16/256/truecolor with downsampling, and no escape-sequence injection from data.
- **G4. Real layout.** Constraint-based layout with predictable rounding and overflow behavior.
- **G5. Widget set for team tools.** Tables with column constraints and scrolling, wrapped text, blocks, toasts, spinners, themes, key hints.
- **G6. Never crash on geometry.** No drawing API throws for any `Rect`, including zero-size, negative-size or off-screen rects.
- **G8. Scales to large apps.** Components, screens and overlays, key bindings scoped to the focused component, and async work tied to component lifetime. A 20-view tool stays as maintainable as a 2-pane one.
- **G9. One API path.** 1.0 ships one documented way to do each thing, and one consistent state model.
- **G7. Quality bar.** Unit tests, a virtual-terminal test harness, a CI matrix on three OSes, AOT/trim compatibility, a tracked public API, and complete XML docs.

### Non-goals (1.0)
- A retained widget tree, CSS or declarative styling. MiniTui stays immediate-mode.
- Mouse, bracketed paste, canvas/chart, multi-line text area, popup menus. These are deferred to Phase 8 / 1.1 (§11).
- Right-to-left text and bidirectional layout.
- Terminals without VT support, except through the legacy fallback backend (§5.6).
- Screen-reader accessibility.

---

## 2. Baseline defects (all must be fixed, each with a regression test)

| ID | Location | Defect |
|---|---|---|
| D1 | `Terminal.cs` ctor, `Input.IsQuit` | Ctrl+C is never set up as input (`Console.TreatControlCAsInput` is never set and there's no `CancelKeyPress` handler). Ctrl+C kills the process without running `Dispose`, which leaves the cursor hidden and colors stuck. The Ctrl+C branch in `IsQuit` can never fire. |
| D2 | `Terminal.cs` ctor and `Dispose` | The code never enters the alternate screen; the constructor comment claims it does. `Console.Clear()` on startup and exit wipes the user's visible screen. |
| D3 | `App.Run`, `App.RunWhile` | Reads only one key per frame, then calls `Thread.Sleep(16)`. Pasted text and key repeat are throttled to about 60 keys/s. |
| D4 | `App.RunWhile` | Polls twice per frame: once in the user's condition (the README pattern) and once in the loop. If no router is attached, a key taken by the loop's poll is discarded. |
| D5 | `Terminal.Flush` | Each changed cell costs a `Console.SetCursorPosition` plus a `Console.Write`, plus color property sets. A full redraw means thousands of console calls. |
| D6 | `Widgets.List`, `Widgets.Table`, `Widgets.Modal`, `Rect.Shrink` | Throws `ArgumentOutOfRangeException` on narrow areas. Examples: `text[..(inner.Width - 1)]` when `inner.Width <= 0`, and `headers[col][..(colWidth - 2)]` when `colWidth < 2`. `Shrink` returns negative sizes. |
| D7 | `Layout.Build` | When fixed sizes exceed the available space, fill regions get a negative size. The integer-division remainder is dropped (3 fills in 10 rows come out 3/3/3). |
| D8 | `InputHelpers.HandleTextInput`, `IsTextEditKey` | Only accepts `KeyChar` in 32–126. Non-ASCII characters (é, ü, CJK, emoji) can't be typed. |
| D9 | `Cell`, `Buffer.DrawText`, all widgets | `Cell` holds a single UTF-16 `char`, and every width calculation uses `string.Length`. Surrogate pairs get split, and wide characters break alignment. |
| D10 | `Cell.Empty`, `Buffer` primitives, `Widgets.*` | Background is hardcoded to `ConsoleColor.Black`, so the library paints over light or custom terminal themes. |
| D11 | `Widgets.List` | `isSelected = focused && ...` hides the selection whenever the list is unfocused. |
| D12 | `InputRouter.Handle` | Modal activation is only detected inside `Handle`, so `IsModalActive` stays stale until the next key press. A modal opened programmatically (e.g. by an HTTP error) isn't treated as active for rendering or auto-invalidation. |
| D13 | `CommandInputState`, `HandleCommandInput` | `History` is a mutable `List<string>` inside a `record struct`. Copies share the list, and Enter mutates it in place. |
| D14 | All text drawing | Control characters (including ESC) in drawn strings go straight to the terminal. Untrusted API data can inject escape sequences. |
| D15 | `Widgets.Table` | Columns all get equal width. Rows beyond the area height are silently dropped. A selected row outside the visible area is never shown. |
| D16 | `App.Run`, `App.RunWhile` | Redraws every 16 ms even when idle. |
| D17 | `Terminal` | Nested `App` instances (as in `MiniTui.Example`) each clear the screen on entry and exit. The outer app relies on a manual `app.Invalidate()` afterwards. |
| D18 | `README.md` | The license section ("Do whatever you want") conflicts with the MIT license declared in `MiniTui.csproj` and `LICENSE`. The box-style examples show ASCII (`+-+`) instead of the real glyphs. |
| D19 | `src/MiniTui/MiniTui.sln` | A stale duplicate solution file. `src/MiniTui.sln` is the canonical one. |

---

## 3. Target usage (end state)

Both programs below must compile and work against 1.0. Runnable versions ship in the Example app (§10.2).

### 3.1 Small app: one render callback

```csharp
using MiniTui;

var api = new ServicesClient(httpClient);                    // user code
var services = new Loadable<IReadOnlyList<ServiceDto>>();
var selection = new SelectionState();
var toasts = new ToastQueue();
var app = App.Create();                                       // builder methods mutate and return this

var keys = new KeyMap()
    .Add("r", "Refresh", () => services.Load(app, ct => api.ListAsync(ct)))
    .Add("j,down", "Down", () => selection.Next(services.Value?.Count ?? 0))
    .Add("k,up", "Up", () => selection.Previous(services.Value?.Count ?? 0))
    .Add("q,ctrl+c", "Quit", () => app.Quit());

app.WithTheme(Theme.Default)
    .OnSetup(a => services.Load(a, ct => api.ListAsync(ct)))
    .OnKey(key => keys.Handle(key))
    .OnError(ex => { toasts.Push(ex.Message, ToastLevel.Error); return ErrorAction.Continue; })
    .Render((buf, screen) =>
    {
        var layout = Layout.Vertical(screen)
            .Add("body", Constraint.Fill())
            .Add("status", Constraint.Length(1))
            .Build();

        services.Switch(
            idle:    () => Widget.Paragraph().Text("Press r to load").Draw(buf, layout["body"]),
            loading: stale => DrawTable(buf, layout["body"], stale, loading: true),
            loaded:  list  => DrawTable(buf, layout["body"], list, loading: false),
            failed:  ex    => Widget.Paragraph()
                                  .Text(new Line(ex.Message, Theme.Current.Error))
                                  .Wrap(WrapMode.Word)
                                  .Block(Widget.Block().Title(" Error "))
                                  .Draw(buf, layout["body"]));

        Widget.StatusBar().Left(keys.Hints(screen.Width / 2)).Draw(buf, layout["status"]);
        Widget.Toasts().Queue(toasts).Draw(buf, screen);
    });

app.Run();

void DrawTable(Buffer buf, Rect area, IReadOnlyList<ServiceDto>? list, bool loading) =>
    Widget.Table()
        .Block(Widget.Block().Title(loading ? " Services (refreshing…) " : " Services "))
        .Header("Name", "Status", "Version")
        .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12))
        .Rows(list ?? [], s => new Row(
            s.Name,
            new Line(s.Status, s.Healthy ? Theme.Current.Success : Theme.Current.Error),
            s.Version))
        .State(selection)
        .Focused()
        .Draw(buf, area);
```

### 3.2 Large app: screens and components (§9)

```csharp
using MiniTui;

var app = App.Create().WithTheme(Theme.Default);   // chrome on by default: hints + breadcrumb status bar
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
            .Add("enter", "Open", Open)
            .Add("d", "Delete", () => App.RunTask(_ => DeleteAsync()));
        Load(services, api.ListAsync);                 // cancelled automatically when the screen is popped
    }

    public override bool HandleKey(ConsoleKeyInfo key) =>
        selection.HandleKey(key, services.Value?.Count ?? 0);

    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        services.Switch(
            idle:    () => { },
            loading: stale => DrawTable(buf, area, ctx, stale),
            loaded:  list  => DrawTable(buf, area, ctx, list),
            failed:  ex    => Widget.Paragraph().Text(new Line(ex.Message, ctx.Theme.Error)).Draw(buf, area));

    void Open()
    {
        if (services.Value is { Count: > 0 } list)
            App.Screens.Push(new ServiceDetailScreen(api, list[selection.Selected].Id)); // Esc pops back
    }

    async Task DeleteAsync()
    {
        if (services.Value is not { Count: > 0 } list) return;
        var svc = list[selection.Selected];
        if (!await App.Overlays.ConfirmAsync($"Delete {svc.Name}?")) return;
        await api.DeleteAsync(svc.Id, Lifetime);       // continuation resumes on the UI thread
        App.Toasts.Push($"Deleted {svc.Name}", ToastLevel.Success);
        Load(services, api.ListAsync);
    }

    void DrawTable(Buffer buf, Rect area, RenderContext ctx, IReadOnlyList<ServiceDto>? list) =>
        Widget.Table()
            .Header("Name", "Status", "Version")
            .Columns(Constraint.Fill(), Constraint.Length(10), Constraint.Length(12))
            .Rows(list ?? [], s => new Row(s.Name, s.Status, s.Version))
            .State(selection)
            .Focused(ctx.Focused)
            .Draw(buf, area);
}
```

---

## 4. Core model (Phase 1)

### 4.1 Color

```csharp
public readonly record struct Color
{
    public ColorKind Kind { get; }          // Reset, Named, Indexed, Rgb
    public static Color Reset { get; }
    // 16 named colors, same names as ConsoleColor:
    public static Color Black, DarkBlue, DarkGreen, DarkCyan, DarkRed, DarkMagenta, DarkYellow,
                        Gray, DarkGray, Blue, Green, Cyan, Red, Magenta, Yellow, White;
    public static Color Indexed(byte index);
    public static Color Rgb(byte r, byte g, byte b);
    public static Color FromHex(string hex);   // "#RRGGBB" or "RRGGBB"; throws FormatException otherwise
    public static implicit operator Color(ConsoleColor c);
}
public enum ColorKind { Reset, Named, Indexed, Rgb }
```

`ConsoleColor` → SGR foreground code (background code = foreground code + 10):

| ConsoleColor | FG | ConsoleColor | FG |
|---|---|---|---|
| Black | 30 | DarkGray | 90 |
| DarkRed | 31 | Red | 91 |
| DarkGreen | 32 | Green | 92 |
| DarkYellow | 33 | Yellow | 93 |
| DarkBlue | 34 | Blue | 94 |
| DarkMagenta | 35 | Magenta | 95 |
| DarkCyan | 36 | Cyan | 96 |
| Gray | 37 | White | 97 |

`Reset` encodes as FG `39` / BG `49`. `Indexed(n)` encodes as `38;5;n` / `48;5;n`. `Rgb` encodes as `38;2;r;g;b` / `48;2;r;g;b`.

### 4.2 Color capability and downsampling

```csharp
public enum ColorMode { None, Ansi16, Ansi256, TrueColor }
```

Detection runs in this order, and the first match wins:
1. Explicit `App.WithColorMode(mode)`
2. `NO_COLOR` is set and non-empty → `None`
3. `COLORTERM` is `truecolor` or `24bit` → `TrueColor`
4. `WT_SESSION` is set (Windows Terminal) → `TrueColor`
5. `TERM_PROGRAM` is `iTerm.app`, `WezTerm` or `vscode` → `TrueColor`
6. `TERM` contains `256color` → `Ansi256`
7. Otherwise → `Ansi16`

Downsampling happens at encode time and never changes what's stored in the buffer:
- `Rgb` → `Indexed`: nearest match in the xterm 6×6×6 cube (16–231) or the grayscale ramp (232–255), by squared RGB distance.
- `Indexed` → `Named`: indices 0–15 map directly. Above 15, convert to RGB and pick the nearest of the 16 standard xterm colors.
- `None`: emit no color codes; keep modifiers.

**Decision:** When `ColorMode` is `None` and the active theme is `Theme.Default`, the App switches to `Theme.Monochrome` (§8.1).

### 4.3 Style

```csharp
[Flags] public enum Modifiers : ushort
{ None = 0, Bold = 1, Dim = 2, Italic = 4, Underlined = 8, Reversed = 16, CrossedOut = 32 }

public readonly record struct Style(Color? Fg = null, Color? Bg = null,
                                    Modifiers Add = Modifiers.None, Modifiers Remove = Modifiers.None)
{
    public static Style Default { get; }               // all null/None: changes nothing
    public Style Patch(Style other);                   // other's non-null values win; Add/Remove combine
    public Style WithFg(Color c); public Style WithBg(Color c);
    public Style Bold(); public Style Dim(); public Style Italic();
    public Style Underlined(); public Style Reversed(); public Style CrossedOut();
}
```

**Patch semantics** (this is how D10 gets fixed). Drawing with a style changes only the attributes the style specifies:
`cell.Fg = style.Fg ?? cell.Fg; cell.Bg = style.Bg ?? cell.Bg; cell.Mods = (cell.Mods | style.Add) & ~style.Remove`.
So text drawn over a selection bar keeps the bar's background.

### 4.4 Cell

```csharp
public readonly record struct Cell(string Symbol, Color Fg, Color Bg, Modifiers Modifiers)
{
    public static Cell Empty { get; }        // " ", Reset, Reset, None
    public int Width { get; }                // 0 for continuation, else 1 or 2
    public bool IsContinuation { get; }      // Symbol == ""
    // Compat:
    [Obsolete] public Cell(char c, ConsoleColor fg, ConsoleColor bg);
    [Obsolete] public char Char { get; }     // first UTF-16 unit of Symbol, or ' '
    [Obsolete] public static Cell From(char c);
}
```

- `Symbol` is exactly one grapheme cluster, already sanitized (§4.6). A wide (width-2) cell is followed by a continuation cell (`Symbol == ""`) that copies its style.
- Single-character strings for U+0020–U+007E come from a static cache, so ASCII rendering doesn't allocate.

### 4.5 Buffer

Existing members stay. New and changed members:

```csharp
public class Buffer
{
    public void Set(int x, int y, Cell cell);                 // enforces wide-char invariants
    public int SetString(int x, int y, ReadOnlySpan<char> text, Style style, int maxWidth = int.MaxValue); // returns columns written
    public int SetLine(int x, int y, Line line, int maxWidth);
    public void SetStyle(Rect area, Style style);             // patch styles in area, keep symbols
    public void SetCursor(int x, int y);                      // request the visible terminal cursor this frame
    public (int X, int Y)? CursorRequest { get; }             // cleared by Terminal at frame start
    public string[] ToLines();                                // for tests: symbols only, continuations omitted
    // Changed (source-compatible): bg defaults to null, meaning "keep the existing background"
    public void DrawText(int x, int y, string text, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor? bg = null);
}
```

Invariants (enforced in `Set` and `SetString`):
- **B1.** Out-of-bounds writes are ignored. No method throws for any coordinates or `Rect`.
- **B2.** Writing a cell over the continuation of a wide character turns the wide head at `x-1` into `" "` (keeping its style).
- **B3.** Writing over a wide head at `x` turns its continuation at `x+1` into `" "`.
- **B4.** A wide grapheme that would start in the last column is written as `" "`. It is never split.
- **B5.** `SetString` stops at `maxWidth` and the buffer edge, and never writes half of a wide character.

### 4.6 Text sanitization (fixes D14)

Every text-drawing path (`DrawText`, `SetString`, `Line`, all widgets) applies these rules before measuring or storing text:
- C0 controls (U+0000–U+001F), DEL (U+007F) and C1 controls (U+0080–U+009F) become U+FFFD.
- Exceptions: `\t` becomes one space in single-line APIs (Paragraph expands it to the next multiple of 4). `\n` and `\r\n` are line breaks in `Text`/`Paragraph`, and a single space in single-line APIs.
- **Acceptance:** drawing `"\x1b[31mX"` produces the cells `�`, `[`, `3`, `1`, `m`, `X`. The encoder's output for that frame contains ESC (0x1B) only inside sequences the framework itself generated. The test checks this by parsing the output with the VirtualTerminal (§10.1).

### 4.7 Unicode width (fixes D9)

```csharp
public static class UnicodeWidth
{
    public static int Of(ReadOnlySpan<char> text);           // sum over grapheme clusters
    public static int OfGrapheme(ReadOnlySpan<char> grapheme);
    public static IEnumerable<string> Graphemes(string text); // via StringInfo text elements (UAX #29)
    public static string Truncate(string text, int maxWidth, string ellipsis = "…");
}
```

Width of a grapheme cluster:
1. Empty → 0.
2. Contains U+FE0F (VS16) → 2.
3. First rune is a Regional Indicator and the cluster has ≥2 runes → 2.
4. Contains U+200D (ZWJ) and the first rune has `Emoji_Presentation` → 2.
5. First rune has general category `Mn`, `Me` or `Cf`, or is U+200B → 0.
6. First rune has `East_Asian_Width` W or F, or `Emoji_Presentation=Yes` → 2.
7. Otherwise → 1.

**Decision:** No runtime dependency. Range tables for EAW W/F and Emoji_Presentation are generated from Unicode 16.0 `EastAsianWidth.txt` and `emoji-data.txt` by a .NET 10 file-based app, `tools/GenWidthTable.cs` (`dotnet run tools/GenWidthTable.cs`). The output is committed as `src/MiniTui/Text/UnicodeWidthTable.g.cs`, and lookups use binary search. Use `Rune.GetUnicodeCategory` for rule 5.

Required test cases: `"abc"`=3, `"日本"`=4, `"é"` (precomposed and decomposed)=1, `"👍"`=2, `"👍🏽"`=2, `"👨‍👩‍👧"`=2, `"🇺🇸"`=2, `"❤️"`=2, `"​"`=0, `"⠿"` (braille)=1, `"─│┌"`=3.

### 4.8 Text model

```csharp
public readonly record struct Span(string Content, Style Style);   // implicit from string
public sealed record Line(IReadOnlyList<Span> Spans, Alignment Alignment = Alignment.Left)
{
    public Line(string content, Style style = default);
    public int Width { get; }
    public static implicit operator Line(string s);
}
public sealed record Text(IReadOnlyList<Line> Lines)
{
    public static Text From(string s);   // splits on \n / \r\n
    public static implicit operator Text(string s);
    public static implicit operator Text(Line line);
}
public enum Alignment { Left, Center, Right }
```

### 4.9 Geometry fixes

- `Rect.Shrink(...)` clamps width and height to ≥0, and an over-shrunk rect collapses to its center (fixes part of D6).
- New members: `bool IsEmpty`, `Rect Intersect(Rect)`, `Rect Clamp(Rect bounds)`, `Rect Inner(Margin)`, `IEnumerable<Rect> Rows()`, `IEnumerable<Rect> Columns()`.
- `public readonly record struct Margin(int Left, int Top, int Right, int Bottom)`, with constructors `Margin(int all)` and `Margin(int horizontal, int vertical)`.

---

## 5. Terminal backend and session (Phase 2)

### 5.1 Abstractions

```csharp
public interface ITerminalBackend : IDisposable
{
    (int Width, int Height) GetSize();
    void Enter();                                    // modes in §5.3; idempotent
    void Leave();                                    // restore in §5.3; idempotent, never throws
    void Draw(IReadOnlyList<CellUpdate> updates, (int X, int Y)? cursor, bool fullRedraw);
}
public readonly record struct CellUpdate(int X, int Y, Cell Cell);

public interface IInputSource : IDisposable
{
    bool TryRead(out ConsoleKeyInfo key);            // non-blocking
}
```

Implementations:
- `AnsiBackend` is the default.
- `LegacyConsoleBackend` is the existing `System.Console` path, used as a fallback (§5.6).
- `TestBackend` lives in the main assembly so consumers can test their own apps. It holds an in-memory grid with the diffs applied and exposes `Lines`, `CellAt(x, y)`, `CursorPosition` and `CursorVisible`.
- `ConsoleInputSource` and `ScriptedInputSource` (`Enqueue(params ConsoleKeyInfo[])`, `EnqueueText(string)`).

`Terminal` owns the double buffer and the diffing and calls the backend with the changed cells. Continuation cells are never emitted. When a wide cell changes, its continuation is covered implicitly.

### 5.2 ANSI encoder (`AnsiBackend.Draw`, fixes D5)

- If there are no updates, the cursor request hasn't changed and `fullRedraw` is false, write **zero bytes**.
- Build the whole frame into one pooled buffer, then do **one** write and flush to `Console.OpenStandardOutput()`, encoded as UTF-8 regardless of `Console.OutputEncoding`.
- Wrap each frame in synchronized output, `CSI ?2026h` … `CSI ?2026l`. Terminals that don't support it ignore it.
- `fullRedraw`: emit `CSI 2J` first, then every cell.
- **Cursor tracking:** track the cursor position after each write. Emit `CSI {y+1};{x+1}H` only when the next update's position differs from it. After writing a cell, advance by `cell.Width`.
- **SGR:** track the current style. When the next cell's (Fg, Bg, Modifiers) differ, emit one full sequence: `CSI 0;{mods};{fg};{bg}m`. Modifier codes: Bold 1, Dim 2, Italic 3, Underlined 4, Reversed 7, CrossedOut 9.
- End of frame: `CSI 0m`. If `cursor` is set, move there and `CSI ?25h`. Otherwise `CSI ?25l`.
- **Performance:** for an unchanged frame, `Terminal.Flush` allocates nothing. Test this with `GC.GetAllocatedBytesForCurrentThread()` deltas.

### 5.3 Terminal modes (fixes D1, D2)

`Enter()` does the following, in order:
1. If `Console.IsOutputRedirected || Console.IsInputRedirected`, throw `TerminalNotSupportedException` with the message "MiniTui requires an interactive terminal (stdin/stdout must not be redirected)."
2. Windows: enable VT (§5.5) and set `Console.OutputEncoding = UTF8`, saving the originals.
3. Set `Console.TreatControlCAsInput = true`, saving the original.
4. Emit `CSI ?1049h` (alternate screen), `CSI ?25l` (hide cursor), `CSI ?7l` (disable autowrap) and `CSI 2J`.

`Leave()` does the following, in order. Each step is wrapped in its own try/catch so one failure can't prevent the rest:
`CSI 0m`, `CSI ?7h`, `CSI ?25h`, `CSI ?1049l`, flush. Then restore `TreatControlCAsInput`, `OutputEncoding` and the Windows console modes.

Neither method ever calls `Console.Clear()`. The user's screen and scrollback are preserved.

### 5.4 TerminalSession (fixes D17, G1)

```csharp
internal static class TerminalSession   // process-wide, ref-counted
{
    public static TerminalLease Acquire(ITerminalBackend backend); // first acquire calls Enter()
}
public sealed class TerminalLease : IDisposable { /* last dispose calls Leave() */ }
```

- **Nesting.** An inner `App.Run()` reuses the active session. When the inner app exits, the outer app's next frame is a full redraw. The README and Example must no longer need `app.Invalidate()` after nested apps.
- **Restore on all exit paths.** Register these once, when the session first starts, and dispose them when it ends:
  - `PosixSignalRegistration` for `SIGTERM`, `SIGHUP` and `SIGQUIT` (on Windows, .NET maps these to close, logoff and Ctrl+Break). The handler calls `Leave()` and lets termination proceed.
  - `AppDomain.CurrentDomain.ProcessExit` calls `Leave()`.
  - `AppDomain.CurrentDomain.UnhandledException` calls `Leave()`.
- **Unix resize.** `PosixSignalRegistration` for `SIGWINCH` posts a `ResizeEvent` to the running App (§6). On Windows, the App checks `Console.WindowWidth/Height` on each loop wake, at least every 100 ms.

### 5.5 Windows console

- Use `[LibraryImport]` (source-generated, AOT-safe) for `kernel32!GetStdHandle`, `GetConsoleMode` and `SetConsoleMode`.
- Output handle: OR in `ENABLE_VIRTUAL_TERMINAL_PROCESSING (0x0004)` and `DISABLE_NEWLINE_AUTO_RETURN (0x0008)`. Restore the original mode in `Leave()`.
- Set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` if the generator requires it.

### 5.6 Fallback

If `SetConsoleMode` fails to enable VT on Windows, `App` falls back to `LegacyConsoleBackend` and limits `ColorMode` to `Ansi16`. Unicode width rules still apply, but synchronized output and the alternate screen aren't available. The fallback must still restore the terminal: colors, cursor visibility, and `TreatControlCAsInput`.

---

## 6. Event loop, async and errors (Phase 3)

### 6.1 Events

```csharp
public abstract record Event;
public sealed record KeyEvent(ConsoleKeyInfo Key) : Event;
public sealed record ResizeEvent(int Width, int Height) : Event;
public sealed record TickEvent(TimeSpan Elapsed) : Event;
```

### 6.2 App API (additions; existing builder methods keep working)

```csharp
public class App
{
    public App WithTheme(Theme theme);
    public App WithColorMode(ColorMode mode);
    public App WithTickRate(TimeSpan? rate);           // default 250 ms; null = redraw only on events
    [Obsolete("Use WithTickRate.")] public App WithFrameDelay(int ms);   // maps to WithTickRate
    public App WithTimeProvider(TimeProvider provider);
    public App WithBackend(ITerminalBackend backend);
    public App WithInput(IInputSource input);
    public App CaptureConsoleOutput(bool capture = true); // default true (§6.6)
    public App OnSetup(Action<App> setup);             // existing Action overload kept
    public App OnKey(Func<ConsoleKeyInfo, bool> handler); // true = handled, so the router doesn't see it
    public App OnEvent(Action<Event> handler);
    public App OnError(Func<Exception, ErrorAction> handler);

    public void Run();
    public Task RunAsync(CancellationToken cancellationToken = default);
    public void Post(Action action);                   // thread-safe; runs on the UI thread
    public Task RunTask(Func<CancellationToken, Task> work); // exceptions go to OnError
    public void RequestRedraw();                       // thread-safe
    public void Quit();                                // thread-safe
    public CancellationToken Stopping { get; }         // cancelled when Quit() is called or the loop exits
    public (int Width, int Height) Size { get; }
    public LogBuffer Log { get; }
}
public enum ErrorAction { Continue, Quit, Rethrow }
```

### 6.3 Loop (fixes D3, D4, D12, D16)

- **One UI thread.** All user callbacks (`OnSetup`, `OnKey`, `OnEvent`, `OnUpdate`, `Render`, router handlers, posted actions) run on the thread that called `Run()`.
- **One queue.** A single `Channel<object>` (unbounded, single reader) carries input keys, posted actions, resize events, tick events and redraw requests.
- **Input reader.** A dedicated background thread calls `IInputSource.TryRead` in a loop, sleeping ≤5 ms when it gets nothing, and writes `KeyEvent`s to the channel. It never touches the console output.
- **SynchronizationContext.** `Run()` installs a `MiniTuiSynchronizationContext` that posts continuations to the channel, so `await` inside UI-thread code resumes on the UI thread. The previous context is restored on exit.

Each iteration:
1. Wait until the channel has an item, the next tick is due, or (on Windows) 100 ms has passed for the resize check.
2. Drain up to **256** items in FIFO order:
   - Posted action: invoke it.
   - `KeyEvent`: `OnEvent` → `OnKey`. If not handled → `router.Handle`.
   - `ResizeEvent`: resize the buffers, mark the next frame as a full redraw, then `OnEvent`.
   - `TickEvent`: `OnEvent`.
3. Call `router.Refresh()`, which recomputes the modal stack (fixes D12).
4. Call `OnUpdate`.
5. If anything was processed, a redraw was requested, or this is the first frame: render, then flush.
6. Exceptions thrown from steps 2–5 go to `OnError`. The default is `Rethrow`: leave the terminal, then rethrow from `Run()`.

**`RunWhile` compatibility (fixes D4).** While an App is running, the static `Input.Poll()` dequeues from that App's pending keys instead of reading the console. `RunWhile(condition)` calls `condition()` once per pending `KeyEvent` and once per wake that has no key. The loop doesn't route a key to the router if `condition` already took it with `Input.Poll()`. Every README and Example pattern must lose no keys when fed 1,000 scripted keys at once.

**Acceptance (all use TestBackend, ScriptedInputSource and `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`, which is a test-only dependency):**
- 1,000 keys enqueued at once are all delivered, in order, within ≤4 rendered frames.
- With no input and `WithTickRate(null)`, no frames render after the first.
- `Post` from 8 threads × 1,000 calls: every action runs exactly once, on the UI thread.
- After `await Task.Delay(1)` inside a posted async lambda, the continuation runs on the UI thread.
- A modal opened via `Post` is active (`IsModalActive == true`) at the next render, with no key press.
- A throwing `Render` with the default `OnError` leaves the TestBackend in the left state (`Leave()` called) and propagates the exception from `Run()`.

### 6.4 `Loadable<T>`

```csharp
public enum LoadStatus { Idle, Loading, Loaded, Failed }
public sealed class Loadable<T>
{
    public LoadStatus Status { get; }
    public T? Value { get; }                 // last successful value; kept while reloading or after a failure
    public Exception? Error { get; }
    public DateTimeOffset? LoadedAt { get; } // from the App's TimeProvider
    public void Load(App app, Func<CancellationToken, Task<T>> fetch,
                     CancellationToken lifetime = default);  // cancels any in-flight load
    public void Cancel();
    public TResult Match<TResult>(Func<TResult> idle, Func<T?, TResult> loading,
                                  Func<T, TResult> loaded, Func<Exception, TResult> failed);
    public void Switch(Action idle, Action<T?> loading, Action<T> loaded, Action<Exception> failed);
}
```

- All state changes happen on the UI thread (through `app.Post`), and each one triggers a redraw.
- The fetch token is linked to `app.Stopping`, to `lifetime`, and to this load's own cancellation source. Once `lifetime` is cancelled, results are ignored.
- If a load is replaced by a newer one, its result and any `OperationCanceledException` are ignored. "Last write wins" is enforced with a load generation counter.
- A fetch exception sets `Failed`. It does **not** go to `OnError`, because a failed load is a modeled state.

### 6.5 Router changes

These changes fix D12 for code that uses `InputRouter` during 0.8 and 0.9. `InputRouter` is replaced by focus scopes and the routing in §9.4, and it is removed in 1.0 (§9.7).

```csharp
public InputRouter Add(string name, Action<ConsoleKeyInfo> handler, bool capturesText = false);
public InputRouter Global(Func<ConsoleKeyInfo, bool> handler); // runs before focus routing
public void Refresh();                                         // recompute the modal stack; App calls it each iteration
```

Routing order: active modal → global handlers (skipped when the focused element has `capturesText`) → Tab/Shift+Tab → focused handler.

### 6.6 Console capture

While running with `CaptureConsoleOutput(true)`, `Console.Out` and `Console.Error` are replaced with writers that append to `app.Log`. `LogBuffer` is a thread-safe ring buffer, 1,000 lines by default, with `IReadOnlyList<LogEntry> Snapshot()`, and `LogEntry` has `Timestamp`, `IsError` and `Text`. On exit, the original writers are restored, and lines captured from `Console.Error` are replayed to stderr. This keeps stray `Console.WriteLine` calls (including from libraries) from corrupting the screen.

---

## 7. Layout (Phase 4, fixes D7)

### 7.1 API

```csharp
public abstract record Constraint
{
    public static Constraint Length(int cells);
    public static Constraint Percentage(int percent);   // 0–100; clamped
    public static Constraint Ratio(int num, int den);   // den > 0, else ArgumentOutOfRangeException
    public static Constraint Min(int cells);
    public static Constraint Max(int cells);
    public static Constraint Fill(int weight = 1);      // weight ≥ 1
}
public enum Flex { Start, End, Center, SpaceBetween }

public class Layout
{
    public static Layout Vertical(Rect area); public static Layout Horizontal(Rect area);
    public Layout Add(string name, Constraint constraint);
    [Obsolete] public Layout Add(string name, int size);  // = Length(size)
    [Obsolete] public Layout AddFill(string name);        // = Fill(1)
    public Layout Spacing(int cells);
    public Layout Margin(Margin margin);
    public Layout Flex(Flex flex);
    public Layout Build();                                // idempotent
    public Rect this[string name] { get; }                // KeyNotFoundException naming the missing region
    public static Rect[] Split(Rect area, Direction direction, params Constraint[] constraints);
}
```

The old `Add(string, int)` and `AddFill(string)` are obsolete shims, removed in 1.0 (§9.7, Open Question 5).

### 7.2 Algorithm (deterministic, no solver)

Let `T = max(0, axisLength(area − margin) − spacing × (n − 1))`.

1. **Base sizes:** `Length(n)` = n. `Percentage(p)` = ⌊T·p/100⌋. `Ratio(a,b)` = ⌊T·a/b⌋. `Min(m)` = m. `Max(m)` = 0. `Fill` = 0.
2. **Overflow** (Σbase > T): shrink in this order until it fits. First, `Length`, `Percentage` and `Ratio`, proportionally to their base sizes. Then `Min`, if still needed, also proportionally. Rounding uses the largest-remainder method, with ties going to the later index. No size goes below 0.
3. **Growth:** the leftover `L = T − Σsize` is shared among the growable constraints. `Fill(w)` has weight w, and `Min` and `Max` have weight 1.
   - Compute each growable's **fractional** share, `L·wᵢ/Σw`.
   - While any `Max(m)` has a share greater than `m`, fix that `Max` at `m`, subtract `m` from `L`, drop it from the growable set, and recompute the shares of the rest.
   - Then round the final fractional shares with the largest-remainder method, with ties going to the earlier index.
4. **Flex** (applies only if `L` is still > 0 because nothing could grow): `Start` puts the remainder at the end, `End` at the start, `Center` splits it ⌊L/2⌋ before and the rest after, `SpaceBetween` spreads it across the gaps by largest remainder.
5. Positions are assigned in order, with `spacing` between regions.

**Properties** (checked by seeded random tests: 10,000 cases, fixed seed, no FsCheck):
- Every size is ≥0.
- Σsizes + spacing·(n−1) ≤ axis length.
- If there's at least one growable constraint, Σsizes + spacing·(n−1) = T + spacing·(n−1).
- Regions never overlap, and every region lies inside `area`.
- Same input, same output.

Required examples (vertical, height 10, no spacing):
- `[Fill, Fill, Fill]` → `4,3,3`
- `[Length 3, Fill, Length 1]` → `3,6,1`
- `[Length 8, Fill, Length 8]` → `5,0,5`
- `[Percentage 50, Fill]` → `5,5`
- `[Min 3, Max 2, Fill]` → `6,2,2` (leftover 7; shares 2.33 each; Max is capped at 2; the remaining 5 splits 2.5/2.5 between Min and Fill; the tie goes to the earlier index, Min)
- `[Length 2, Fill]` with `Spacing(1)` → sizes `2,7`, positions `0,3`

---

## 8. Widgets, theme and input (Phase 5)

### 8.1 Theme (fixes D10)

```csharp
public sealed record Theme
{
    public Style Text, TextMuted, Border, BorderFocused, Title, TitleFocused,
                 Selection, SelectionUnfocused, TableHeader, Input, InputFocused, Prompt, Cursor,
                 StatusBar, Button, ButtonFocused, TabActive, TabActiveUnfocused, TabInactive,
                 ModalBorder, ModalTitle, ScrollbarTrack, ScrollbarThumb, GaugeFilled, GaugeEmpty,
                 Success, Warning, Error, Info { get; init; }
    public static Theme Default { get; }
    public static Theme Monochrome { get; }
    public static Theme Current { get; }   // set by App.Run, restored on exit
}
```

- `Theme.Default` reproduces the current look, but with background `Reset` everywhere except bars:
  - `Text` is `Reset`, `TextMuted` is `DarkGray`.
  - `Border` is `DarkGray`, `BorderFocused` is `Cyan`.
  - `Title` is `Cyan`, `TitleFocused` is `Yellow`.
  - `Selection` is Black on Cyan, `SelectionUnfocused` is Black on DarkGray.
  - `TableHeader` is Yellow + Bold. `Prompt` is `Cyan`. `Cursor` is `Reversed`.
  - `StatusBar` is White on DarkGray. `Button` is White on DarkGray, `ButtonFocused` is Black on Cyan.
  - `GaugeFilled` is `Green`, `GaugeEmpty` is `DarkGray`.
  - `Success` Green, `Warning` Yellow, `Error` Red, `Info` Cyan.
- `Theme.Monochrome` uses no colors: `Selection` is `Reversed`, `SelectionUnfocused` is `Underlined`, `BorderFocused` and `TitleFocused` are `Bold`, and `Error` is `Bold|Underlined`.
- Every hardcoded `ConsoleColor` in `Widgets.cs` is replaced by a theme slot. Builders accept `.Style(...)` overrides, which are patched over the theme.

### 8.2 Selection state

**Decision:** The viewport height is only known at render time. So widget state is a mutable class whose offset the widget updates while drawing, which is Ratatui's `StatefulWidget` model. This is the single state model for 1.0 (§9.7). `ListState` and `InputHelpers.HandleListNavigation` become obsolete shims.

```csharp
public sealed class SelectionState
{
    public int Selected { get; set; }          // clamped by the widget at draw
    public int Offset { get; set; }            // adjusted by the widget so Selected is visible
    public int ViewportHeight { get; }         // written by the widget at draw
    public void Next(int count); public void Previous(int count);
    public void PageDown(int count); public void PageUp(int count);
    public void First(); public void Last(int count);
    public bool HandleKey(ConsoleKeyInfo key, int count); // ↑↓ j k PgUp PgDn Home End; true if handled
    public int? SortColumn { get; set; } public bool SortDescending { get; set; } // display only
}
```

Invariant after any draw: `0 ≤ Selected < count` (or 0 when count is 0), and `Offset ≤ Selected < Offset + ViewportHeight`.

### 8.3 Widgets

Every widget follows these rules:
- **W1.** `Draw(buf, area)` never throws for any area (fixes D6). Each widget is tested at all sizes 0×0 through 6×6, plus negative and off-screen rects.
- **W2.** All text is sanitized, measured by width and truncated with `…`.
- **W3.** Colors come from `Theme.Current`.
- **W4.** Builders accept `.Block(BlockBuilder)` where a border makes sense.

| Widget | Requirements |
|---|---|
| **Block** (new) | `Widget.Block().Borders(Borders.All\|Top\|…).BorderType(BoxStyle).Title(Line).TitleAlignment(Alignment).TitleBottom(Line).Padding(Margin).Focused(bool)`. `Draw` returns the inner `Rect`. `DrawBox` and `DrawBoxWithTitle` become thin calls into it. |
| **Paragraph** (new) | `.Text(Text).Wrap(WrapMode.None\|Word\|Char).Alignment(...).Scroll(int)`, plus static `Paragraph.MeasureLines(Text, width, WrapMode)`. Word wrap breaks at whitespace, hard-breaks words wider than the area, preserves explicit newlines, trims trailing whitespace at wrap points, and measures by width. |
| **List** (updated) | `.Items<T>(IReadOnlyList<T>, Func<T, Line>)`, which only formats visible rows. `.State(SelectionState)` and `.HighlightSymbol(string)`. Shows `SelectionUnfocused` when unfocused (fixes D11). Keeps the scroll info title and the scrollbar. `Items(IReadOnlyList<string>)` remains as a convenience. `.State(ListState)` is an obsolete shim. |
| **Table** (new builder; static `Widgets.Table` kept and fixed) | `.Header(params Line[])`, `.Columns(params Constraint[])` (default `Fill(1)` each), `.ColumnSpacing(int)` (default 1), `.Rows(IReadOnlyList<Row>)` or `.Rows<T>(IReadOnlyList<T>, Func<T, Row>)` (only visible rows formatted), `.State(SelectionState)`, `.Focused`, `.HighlightSymbol`. Column widths come from the §7 algorithm. A header shows ▲/▼ when `SortColumn` matches. Scrolls to keep the selection visible (fixes D15). `Row(params Line[] cells)` with an optional `Style`. Must handle 100,000 rows at the same per-frame cost as 100. |
| **Scrollbar** (new) | `.Vertical()/.Horizontal().ContentLength(n).Position(offset).ViewportLength(h)`. Used by List, Table and Paragraph. Hidden when the content fits. |
| **Gauge** (updated) | `Widget.Gauge().Ratio(double).Label(Line?)`. The default label is the percentage. Uses eighth-blocks `▏▎▍▌▋▊▉█` for sub-cell precision. `DrawProgressBar` delegates to it. |
| **Sparkline** (new) | `.Data(IReadOnlyList<double>).Max(double?)`, rendered with `▁▂▃▄▅▆▇█` and right-aligned to the newest value. |
| **Spinner** (new) | `.Frames(SpinnerFrames.Line\|Braille\|Dots).Label(string)`. The frame is derived from the App's `TimeProvider`. The spinner is stateless. |
| **Toasts** (new) | `ToastQueue.Push(string text, ToastLevel level = Info, TimeSpan? duration = 4s)`. The widget draws up to 3 toasts stacked in the top-right corner, styled by level, and expires them using `TimeProvider`. While any toast is visible, the App requests ticks. |
| **StatusBar** (updated) | Draws at `area.Y` when `area.Height == 1`, and keeps the current bottom-row behavior otherwise. Width-aware: when there isn't room for both, the right text wins and the left is truncated. |
| **Modal, Confirm** (updated in Phase 5, replaced in Phase 6) | Phase 5: wraps content with Paragraph, scrolls with `ScrollState` when content overflows, and limits its size to the screen minus a 2-cell margin. Phase 6: replaced by `MessageDialog` and `ConfirmDialog` overlays (§9.3). The builders become obsolete shims. |
| **TextField, CommandLine, TabBar** (new names) | State comes from `TextFieldState`, `CommandLineState` and `TabBarState` (§9.7). Theme-aware and width-aware. TextField and CommandLine request the real terminal cursor via `buf.SetCursor`, in addition to the reversed cursor cell. The old `TextInput`, `CommandInput` and `Tabs` become obsolete shims. |
| **Button, Checkbox** | Move to theme and width-aware rendering. |

### 8.4 Text editing (fixes D8, D13)

These rules apply to `TextFieldState` and `CommandLineState` (§9.7). The obsolete `TextInputState` and `CommandInputState` delegate to them until they're removed.

- Accepts any `KeyChar` where `!char.IsControl(c)`. A high surrogate is held in the state until its low surrogate arrives.
- The cursor moves by grapheme cluster and is always on a cluster boundary. Backspace and Delete remove a whole cluster.
- New keys:
  - Ctrl+←/→: move by word.
  - Ctrl+Backspace or Ctrl+W: delete the previous word. Also accept `ConsoleKey.Backspace` with Control, and the `'\b'` or 0x17 forms terminals send.
  - Ctrl+U: delete to the start of the line.
  - Ctrl+K: delete to the end of the line.
- The text scrolls horizontally by display width.
- `CommandLineState.History` is an `ImmutableList<string>`. Seeding from a `List<string>` copies it. Consecutive duplicates aren't added. History is capped at `MaxHistory` entries (default 100).

### 8.5 KeyMap

```csharp
public sealed class KeyMap
{
    public KeyMap Add(string keys, string description, Action handler,
                      string? group = null, bool showInHints = true);
    public bool Handle(ConsoleKeyInfo key);              // first match wins
    public IEnumerable<string> HelpLines();               // grouped, aligned
    public string Hints(int maxWidth);                    // "r Refresh  q Quit", truncated to fit
}
public readonly record struct KeyBinding(ConsoleKey Key, char? Char, ConsoleModifiers Modifiers)
{
    public static KeyBinding Parse(string s);  // FormatException on invalid input
    public bool Matches(ConsoleKeyInfo key);
}
```

Grammar: `keys := binding ("," binding)*`, `binding := (mod "+")* name`, where `mod ∈ {ctrl, alt, shift}` (case-insensitive). `name` is a single printable character or one of `enter esc tab backspace delete insert up down left right home end pgup pgdn space f1–f12`. A single-character binding like `"r"` matches on `KeyChar`, so `"R"` and `"shift+r"` both match Shift+R.

---

## 9. Components, screens and overlays (Phase 6)

This section adds structure for apps beyond a few panes. It stays immediate-mode: components are plain classes that hold state and handle input, and they render every frame. There's no retained widget tree, and nothing diffs components.

### 9.1 Component

```csharp
public abstract class Component
{
    public KeyMap Keys { get; } = new();
    public virtual string KeysGroup => GetType().Name;             // heading in the help overlay
    public virtual bool CapturesText => false;                     // §9.4
    public virtual bool HandleKey(ConsoleKeyInfo key) => false;    // offered before Keys
    public abstract void Render(Buffer buf, Rect area, RenderContext ctx);

    protected App App { get; }                    // valid from mount to unmount; InvalidOperationException otherwise
    protected CancellationToken Lifetime { get; } // cancelled at unmount
    protected FocusScope Focus { get; }           // this component's focusable children; empty by default
    protected T AddChild<T>(T child) where T : Component;
    protected void RemoveChild(Component child);
    protected void Load<T>(Loadable<T> target, Func<CancellationToken, Task<T>> fetch); // = target.Load(App, fetch, Lifetime)
    protected virtual void OnMount() { }
    protected virtual void OnUnmount() { }
    protected virtual void OnActivated() { }      // its screen became the top screen
    protected virtual void OnDeactivated() { }    // its screen was covered or popped

    public static Component From(Action<Buffer, Rect, RenderContext> render,
                                 Func<ConsoleKeyInfo, bool>? handleKey = null, bool capturesText = false);
}

public sealed class RenderContext
{
    public bool Focused { get; }                  // this component is on the focus chain
    public Theme Theme { get; }
    public App App { get; }
    public RenderContext For(Component child);    // Focused = this.Focused && the owner's scope focuses child
    public string Hints(int maxWidth);            // merged hints for the current focus chain (§9.5)
}
```

Rules:
- **C1.** Rendering is explicit: a parent calls `child.Render(buf, rect, ctx.For(child))`. A child that isn't rendered in a frame still exists and keeps its state.
- **C2.** Every component callback runs on the UI thread.
- **C3.** Lifecycle order: a parent mounts before its children, and children unmount before their parent, in reverse add order. `Lifetime` is cancelled before `OnUnmount` runs.
- **C4.** `AddChild` on an already-mounted parent mounts the child immediately. `RemoveChild` unmounts it, and also removes it from `Focus`.
- **C5.** A component has at most one parent. `AddChild` on a component that already has a parent throws `InvalidOperationException`.
- **C6.** `Component.From` creates an inline component, for small apps that want focus handling without writing classes.

### 9.2 FocusScope

```csharp
public sealed class FocusScope
{
    public FocusScope Add(Component child);       // tab order = add order; child must be a child of the scope's owner
    public Component? Focused { get; }            // the first member added, by default
    public void Focus(Component child);           // InvalidOperationException if child isn't in the scope
    public bool IsFocused(Component child);
    public void Next(); public void Previous();   // wrap around
}
```

The **focus chain** starts at the top screen, then follows `Focus.Focused` from each component until it reaches a component whose scope is empty.

### 9.3 Screens and overlays

```csharp
public abstract class Screen : Component
{
    public virtual string Title => GetType().Name;   // shown in the breadcrumb
    public virtual bool PopOnEscape => true;
}

public abstract class Overlay : Component
{
    public virtual bool CloseOnEscape => true;
    public virtual Rect Placement(Rect screen);      // default: centered, 60%×50%, min 30×7, clamped to the screen minus a 2-cell margin
}

public sealed class ScreenStack
{
    public Screen? Top { get; } public int Count { get; } public IReadOnlyList<Screen> All { get; }
    public void Push(Screen screen);   // mount; deactivate the previous top; activate the new one
    public void Pop();                 // unmount the top; activate the new top; if the stack is now empty and QuitWhenEmpty is set, Quit()
    public void Replace(Screen screen);// pop + push without activating the screen below in between
    public void PopTo(Screen screen);  // pop until screen is on top; InvalidOperationException if it isn't in the stack
}

public sealed class OverlayStack
{
    public Overlay? Top { get; } public bool Any { get; }
    public void Show(Overlay overlay); public void Close(Overlay overlay);
    public Task<bool> ConfirmAsync(string message, string yes = "Yes", string no = "No"); // Enter/y on Yes → true; Esc/n → false
    public Task ShowMessageAsync(string title, Text body);                                // completes when closed
    public void ShowHelp();                                                                // HelpOverlay for the current focus chain
}

// Additions to App (§6.2):
public ScreenStack Screens { get; }
public OverlayStack Overlays { get; }
public ToastQueue Toasts { get; }                 // rendered by the App when screens are in use
public KeyMap Keys { get; }                       // global bindings; built in: "?" → ShowHelp, "ctrl+c" → Quit
public App WithChrome(bool statusBar = true, bool breadcrumb = true);
public App QuitWhenEmpty(bool quit = true);       // default true
```

Built-in overlays:
- `ConfirmDialog`, used by `ConfirmAsync`.
- `MessageDialog`: a wrapped `Paragraph` that scrolls with `ScrollState`.
- `HelpOverlay` (§9.5).
- `CommandLineOverlay(CommandRegistry)`: a `:` command line built on `CommandLineState`. It isn't bound by default; apps add `app.Keys.Add(":", "Command", () => app.Overlays.Show(new CommandLineOverlay(registry)))`.

**App rendering with screens.**
- **Decision:** `App.Render(...)` and `Screens` are mutually exclusive. `Screens.Push` throws `InvalidOperationException` if a `Render` callback is set.
- With the chrome enabled, the layout is `[Fill, Length(1)]`. The status row shows `ctx.Hints` on the left and the breadcrumb on the right: screen titles joined with ` › `, truncated from the left.
- Only the top screen renders. Then each overlay renders, bottom to top: its `Placement` rect is cleared and wrapped in a themed `Block`. Toasts render last.

### 9.4 Key routing (replaces `InputRouter` in 1.0)

For each `KeyEvent`:
1. The `App.OnKey` pre-hook (an escape hatch). If it returns true, routing stops.
2. **If an overlay is open:** the key goes to the top overlay and its focus chain, using the step 3 rules. If nothing handles an Esc and `CloseOnEscape` is true, the overlay closes. **Routing always stops here**, because overlays are modal.
3. **Focus chain, innermost first.** For each component `c`, call `c.HandleKey(key)`, then `c.Keys.Handle(key)`.
   - If nothing on the chain handled Tab or Shift+Tab, the innermost `FocusScope` on the chain with ≥2 members cycles focus.
4. `App.Keys`, the global bindings.
5. Esc: if the top screen has `PopOnEscape` and `Screens.Count > 1`, call `Screens.Pop()`.
6. Anything still unhandled is dropped.

**Text capture.** When the innermost focused component has `CapturesText = true`, steps 3 (for its ancestors) and 4 skip single printable-character bindings that have no Ctrl or Alt modifier. So typing `q` into a field doesn't quit. Ctrl and Alt combinations, function keys and navigation keys still bubble up.

### 9.5 Hints and help

- The bindings in scope are the focus chain's KeyMaps, innermost first, followed by `App.Keys`. If an inner binding uses the same key as an outer one, the outer binding is shadowed and isn't shown.
- `ctx.Hints(maxWidth)` lists the bindings in scope that have `showInHints`, innermost first, without duplicates, truncated to fit.
- `HelpOverlay` groups bindings under each component's `KeysGroup`, in chain order, followed by a "Global" group.

### 9.6 ComponentHarness and acceptance

`ComponentHarness` lives in the main assembly so consumers can test their own screens.

```csharp
public sealed class ComponentHarness : IDisposable
{
    public ComponentHarness(Screen root, int width = 80, int height = 24, TimeProvider? time = null);
    public App App { get; }                 // TestBackend + ScriptedInputSource; never touches the real console
    public void Press(string keys);         // space-separated KeyBinding names: "j j enter", "ctrl+r"
    public void Type(string text);
    public void RunUntilIdle();             // drain posted work and UI-context continuations, then render one frame;
                                            // TimeoutException after 5 s of real time
    public string[] Lines { get; }
    public Cell CellAt(int x, int y);
}
```

`App` exposes an internal single-iteration step that the harness uses (`InternalsVisibleTo` is not needed, because the harness is in the same assembly).

Acceptance tests:
- Push and pop produce the mount, unmount, activate and deactivate order from C3, checked against a recorded call log.
- After a screen is popped, its in-flight `Load` sees a cancelled token and its result is ignored.
- Routing table tests cover every step in §9.4, including:
  - `q` typed into a `CapturesText` field is inserted, while `ctrl+c` still quits;
  - overlays swallow every key;
  - Esc pops the screen, and closes an overlay first if one is open;
  - Tab cycles the innermost scope.
- Hint shadowing: an inner `r` hides an outer `r`.
- `ConfirmAsync` returns true for Enter on Yes and for `y`, false for Esc and `n`, and its continuation runs on the UI thread.
- The §3.2 program, run against a fake API in the harness, renders the table. Enter pushes the detail screen, and Esc returns to the list with the selection unchanged.

### 9.7 One API path and state model

**Decision:** 1.0 ships one documented way to do each thing. The APIs in the "Removed" column become `[Obsolete]` shims in Phases 5 and 6 and are deleted in Phase 7 (Open Question 7). `docs/MIGRATION-0.7-to-1.0.md` maps every one of them.

**Decision (state model):** Every widget state is a mutable sealed class that owns its key handling (`bool HandleKey(ConsoleKeyInfo key, …)`). Widgets write render-time facts back into the state: viewport size, offsets and scroll position. 1.0 has no value-type states with return-a-new-state helpers.

| State class | Members | Replaces |
|---|---|---|
| `SelectionState` | §8.2 | `ListState`, `InputHelpers.HandleListNavigation` |
| `TextFieldState` | `string Text`, `int Cursor`, `int ScrollOffset` (widget-written), `bool HandleKey(key)` | `TextInputState`, `HandleTextInput`, `IsTextEditKey` |
| `CommandLineState : TextFieldState` | `ImmutableList<string> History`, `int MaxHistory`, `bool HandleKey(key, out ParsedCommand? submitted)` | `CommandInputState`, `CommandInputResult`, `HandleCommandInput` |
| `TabBarState` | `int Selected`, `int Count` (widget-written), `bool HandleKey(key)` | `TabsState`, `HandleTabsNavigation` |
| `ScrollState` | `int Offset`, `int ViewportHeight` and `int ContentHeight` (widget-written), `bool HandleKey(key)` | — (used by Paragraph and MessageDialog) |

| Concept | 1.0 | Removed (obsolete in 0.8–0.9) |
|---|---|---|
| Drawing widgets | `Widget.*()` builders | public static `Widgets.*` (made internal) |
| Borders | `Widget.Block()` | `Buffer.DrawBox`, `DrawBoxWithTitle` |
| Text | `Buffer.SetString`, `SetLine` with `Style` | `Buffer.DrawText(…ConsoleColor…)`, `DrawTextCentered`, `Cell(char, ConsoleColor, ConsoleColor)`, `Cell.Char`, `Cell.From` |
| Progress | `Widget.Gauge()` | `Buffer.DrawProgressBar` |
| Layout | `Layout.Add(name, Constraint)`, `Layout.Split` | `Layout.Add(name, int)`, `AddFill` |
| Focus | `FocusScope` and the focus chain | `InputRouter`, `FocusManager` |
| Dialogs | `Overlays.Show`, `ConfirmAsync`, `ShowMessageAsync` | `Widget.Modal`, `Widget.Confirm`, `ConfirmState`, `HandleConfirmInput`, `InputRouter.WithModal`, `IsModalDismissKey` |
| Keys | `KeyMap`, `KeyBinding` | `Input.IsKey`, `Input.IsChar`, `Input.IsQuit`, `InputHelpers.IsListNavigationKey` |
| Run loop | `Run`, `RunAsync`, `OnKey`, `Screens` | `RunWhile`, `Input.Poll`, `Input.Read`, `WithFrameDelay` |
| Text input widgets | `Widget.TextField()`, `Widget.CommandLine()`, `Widget.TabBar()` | `Widget.CommandInput()`, `Widgets.TextInput`, `Widget.Tabs()` |

These stay unchanged and are the canonical APIs: `CommandRegistry`, `ParsedCommand`, `CommandResult`, `TimedMessage`, `Rect`, `Vec2`, `Theme`, `Loadable<T>`, `App.Invalidate` (forces a full redraw).

---

## 10. Quality, testing and packaging (Phases 0 and 7)

### 10.1 Test projects

- `tests/MiniTui.Tests` (xUnit, `net10.0`), added to `src/MiniTui.sln`.
- `VirtualTerminal` (test-only) parses the bytes `AnsiBackend` emits: CUP, SGR, ED 2J, DEC private modes (`?1049`, `?25`, `?7`, `?2026`) and UTF-8 text. It reconstructs a cell grid plus mode flags.
  - **Encoder property test:** 2,000 seeded random sequences of buffers (random symbols including wide and emoji, random styles, random sizes). After each `Draw`, the VirtualTerminal grid must equal the buffer exactly, across all four `ColorMode`s, with expected downsampled colors.
  - **Mode test:** after `Enter()` then `Leave()`, all modes are back to their defaults.
- Snapshot helper: `AssertLines(buffer, """ ... """)` compares `buffer.ToLines()` against a raw string literal, and `AssertStyle(buffer, x, y, expected)` checks a cell's style.
- `ComponentHarness` (§9.6) is used for the routing, lifecycle and overlay tests.
- Coverage target: ≥85% line coverage for `src/MiniTui`, collected with coverlet in CI. A failing coverage check doesn't block the build until Phase 7.

### 10.2 Example

Phase 5 adds two demos to the existing ones:
- **Services.** The §3.1 program against a fake `HttpClient` (a stub `HttpMessageHandler`) with 300–1,500 ms latency, a 20% failure rate and 500 rows. It exercises `Loadable`, cancellation on refresh, error toasts, a 500-row table and the KeyMap help modal (`?`).
- **Widgets.** One screen with every new widget, a theme toggle (`t`: Default / Monochrome) and a Unicode sample panel (CJK, emoji, combining marks).

Phase 6 rewrites `MiniTui.Example` as a single `ScreenStack` app:
- The menu is a `Screen`, and each demo is a `Screen`. No `App.Run` is nested.
- Services follows the §3.2 shape: Enter opens a `ServiceDetailScreen` with its own `Loadable`, `d` confirms through `ConfirmAsync`, and Esc goes back.
- Every demo is migrated off obsolete APIs.

### 10.3 Build and packaging

`MiniTui.csproj` gets:
- `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, and CS1591 (missing XML docs) as an error.
- `IsAotCompatible=true`, which enables the trim and AOT analyzers. The build must be warning-free.
- `Deterministic`, plus `ContinuousIntegrationBuild` when running in CI.
- `IncludeSymbols` with `SymbolPackageFormat=snupkg`, `PublishRepositoryUrl` and `EmbedUntrackedSources`. SourceLink is built into the SDK.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` (private assets) with `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`, introduced in Phase 7.

Remove the stale `src/MiniTui/MiniTui.sln` (D19).

CI and publishing:
- New `.github/workflows/ci.yml`, run on push and PR. Matrix: `ubuntu-latest`, `windows-latest`, `macos-latest` with .NET `10.0.x`. Steps: restore, `build -warnaserror`, `test` (with coverage on Ubuntu), and a `pack` dry run.
- `publish.yml` runs tests before packing and takes the version from the tag (`-p:Version=${GITHUB_REF_NAME#v}`) instead of the hardcoded `<Version>`.

### 10.4 Docs

- README rewritten for 1.0: quick start, async/HTTP pattern (§3.1), structuring larger apps with screens and components (§3.2), layout, theming, testing components with `ComponentHarness`, testing your app with `TestBackend`, the license (MIT, fixes D18), and correct box-style glyphs.
- `docs/MIGRATION-0.7-to-1.0.md` covers every obsolete API and its replacement.
- `CHANGELOG.md`.
- `TODO.md` updated: completed items checked, deferred items moved to the "Post-1.0" section.

### 10.5 Manual QA matrix (Phase 7; results recorded in the PR)

Environments: Windows Terminal (pwsh), conhost (cmd), WSL Ubuntu in Windows Terminal, macOS Terminal.app, iTerm2, tmux on Linux, and SSH to Linux.

Checks in each environment:
1. Startup and exit preserve the prior screen content.
2. Resizing during use reflows the layout with no artifacts.
3. Ctrl+C exits cleanly.
4. A forced exception exits with the terminal restored and the stack trace visible.
5. Closing the window or tab doesn't leave the parent shell broken (tmux/SSH).
6. Pasting 1,000 characters into an input loses none.
7. The CJK and emoji panel aligns.
8. A light terminal theme shows no black blocks.
9. Running with `NO_COLOR=1` gives a monochrome theme with visible selection.
10. Idle CPU is < 1% with the default tick rate.

---

## 11. Phases

| Phase | Scope | Fixes | Exit criteria | Version |
|---|---|---|---|---|
| **0. Infra** | Test project, CI workflow, csproj hardening (except PublicApiAnalyzers), remove the stale sln, fix the README license | D18, D19 | CI green on 3 OSes; a smoke test passes | — |
| **1. Core model** | §4: Color, Style, Cell, Buffer invariants, sanitization, UnicodeWidth plus its generator, Line/Span/Text, Rect fixes | D6 (geometry part), D9, D10 (model), D14 | All §4 tests pass; existing widgets compile through the shims; Example runs | — |
| **2. Backend** | §5: ITerminalBackend, AnsiBackend, TestBackend, LegacyConsoleBackend, TerminalSession, signals, Windows VT, VirtualTerminal tests | D1, D2, D5, D17 | Encoder property test passes; unchanged frame = 0 bytes and 0 allocations; nested demos need no `Invalidate()` | — |
| **3. Loop and async** | §6: event channel, input thread, SynchronizationContext, Post/RunTask/OnError, Loadable, router Refresh/Global, console capture, `Input.Poll` compat | D3, D4, D12, D16 | All §6.3 acceptance tests pass | **0.8.0** |
| **4. Layout** | §7 | D7 | Property tests and the required examples pass | — |
| **5. Widgets** | §8: Theme, state classes (§9.7), all widgets, text editing, KeyMap; Services and Widgets demos | D6 (widgets), D8, D10 (widgets), D11, D13, D15 | W1 sweep passes for every widget; the §3.1 program compiles and runs | — |
| **6. Components** | §9: Component, FocusScope, Screen/ScreenStack, Overlay/OverlayStack and built-in dialogs, key routing, hints and help, chrome, ComponentHarness; Example rewritten as screens; old APIs marked obsolete | — | All §9.6 acceptance tests pass; the §3.2 program compiles and runs | **0.9.0** |
| **7. Release** | Remove obsolete APIs (§9.7), PublicApiAnalyzers and API review, docs, migration guide, coverage ≥85%, manual QA matrix | — | All §10 criteria met; no `[Obsolete]` members remain; tag `v1.0.0` | **1.0.0** |
| **8. Post-1.0** | Raw input parser (Unix stdin bytes; Windows `ReadConsoleInput`): mouse (SGR 1006), bracketed paste (`?2004`), reliable Alt modifiers. Then Canvas (braille 2×4), line Chart, BarChart, TextArea, PopupMenu/Select, a debug overlay (layout rects and frame time), and Ctrl+Z suspend/resume (SIGTSTP/SIGCONT) | — | Separate specs | 1.1+ |

---

## 12. Proposed file layout

Everything stays in `namespace MiniTui`, so consumers don't need new usings.

```
src/MiniTui/
  App/        App.cs, AppLoop.cs, MiniTuiSynchronizationContext.cs, Loadable.cs, LogBuffer.cs, Events.cs
  Core/       Color.cs, ColorMode.cs, Style.cs, Cell.cs, Buffer.cs, Geometry.cs (Vec2, Rect, Margin)
  Text/       Span.cs, Line.cs, Text.cs, Sanitizer.cs, UnicodeWidth.cs, UnicodeWidthTable.g.cs
  Layout/     Constraint.cs, Layout.cs, LayoutSolver.cs
  Terminal/   ITerminalBackend.cs, AnsiBackend.cs, AnsiEncoder.cs, LegacyConsoleBackend.cs, TestBackend.cs,
              TerminalSession.cs, Terminal.cs, ColorCapability.cs, WindowsConsole.cs, IInputSource.cs
  Components/ Component.cs, RenderContext.cs, FocusScope.cs, Screen.cs, ScreenStack.cs, Overlay.cs,
              OverlayStack.cs, ConfirmDialog.cs, MessageDialog.cs, HelpOverlay.cs, CommandLineOverlay.cs,
              KeyRouter.cs, ComponentHarness.cs
  Input/      KeyMap.cs, KeyBinding.cs, Commands.cs, SelectionState.cs, TextFieldState.cs,
              CommandLineState.cs, TabBarState.cs, ScrollState.cs   (+ obsolete shims until Phase 7)
  Widgets/    Theme.cs, Widget.cs (factory), one file per builder   (static Widgets.cs internal in 1.0)
tests/MiniTui.Tests/   ... plus VirtualTerminal.cs, Snapshot.cs
tools/GenWidthTable.cs
docs/SPEC.md, docs/MIGRATION-0.7-to-1.0.md
```

---

## 13. Open questions (defaults apply if unanswered)

| # | Question | Default |
|---|---|---|
| 1 | Publish to nuget.org or keep GitHub Packages? GitHub Packages requires a PAT for every consumer, which adds friction for a team. | **nuget.org.** Keep the GitHub Packages push as well. |
| 2 | Mouse support in 1.0? | **No**, it's Phase 7. |
| 3 | Vendor the width table or depend on the `Wcwidth` NuGet package? | **Vendor** (zero runtime dependencies). |
| 4 | Support legacy conhost without VT? | **Yes**, through `LegacyConsoleBackend`, with reduced features. |
| 5 | Mark the int-based `Layout.Add(name, int)` / `AddFill` obsolete? | **Yes**, to keep one API path (§9.7). |
| 6 | Default tick rate? | **250 ms.** Toasts and spinners request faster ticks while visible: spinners 100 ms. |
| 7 | Remove obsolete APIs at 1.0, or keep them through 1.x? Removing them means your existing MiniTui apps must migrate (the migration guide maps every API). | **Remove at 1.0** (Phase 7). |
