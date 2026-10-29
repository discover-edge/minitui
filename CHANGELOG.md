# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Components (§9): `Component` (keys, `HandleKey`, `CapturesText`, children, `Lifetime`, `Load`, mount/activate hooks, `Component.From`), `RenderContext` and `FocusScope`.
- `Screen` and `App.Screens` (`ScreenStack`: `Push`, `Pop`, `Replace`, `PopTo`). With screens, the app draws the top screen and a status bar with key hints and a breadcrumb (`WithChrome`), and quits when the last screen is popped (`QuitWhenEmpty`).
- `Overlay` and `App.Overlays` (`OverlayStack`: `Show`, `Close`, `ConfirmAsync`, `ShowMessageAsync`, `ShowHelp`), with the built-in `ConfirmDialog`, `MessageDialog`, `HelpOverlay` and `CommandLineOverlay`. Overlays are modal and work in render-callback apps too.
- Key routing (§9.4): the `OnKey` hook, then the top overlay, or the focus chain innermost first, then `App.Keys` (with `?` for help and `ctrl+c` built in), then Esc pops the screen. Text-capturing components keep single-character bindings from firing while you type.
- `App.Toasts`, drawn by the app over everything else.
- `ComponentHarness` for testing screens: `Press`, `Type`, `RunUntilIdle`, `Lines`, `CellAt`.
- `Theme` (`Default`, `Monochrome`, `Current`) and `App.WithTheme`. Every widget draws with the theme; backgrounds are left to the terminal except for bars, buttons and selections. Apps use `Monochrome` automatically when the terminal has no colors (`NO_COLOR`).
- Widget builders: `Widget.Block` (`Borders`), `Paragraph` (`WrapMode`, `Paragraph.MeasureLines`), `Table` (`Row`, column constraints, sort markers, only visible rows formatted), `Scrollbar`, `Gauge`, `Sparkline`, `Spinner` (`SpinnerFrames`), `Toasts` (`ToastQueue`, `ToastLevel`), `TextField`, `CommandLine`, `TabBar`, `Button` and `Checkbox`.
- `List` builder: `Items(list, format)` (only visible items are formatted), `State(SelectionState)`, `HighlightSymbol`, `Block`, `Style` and `HighlightStyle`.
- State classes that handle their own keys: `SelectionState`, `ScrollState`, `TextFieldState`, `CommandLineState` and `TabBarState`.
- Text editing (§8.4): any non-control character including surrogate pairs, cursor movement and deletion by grapheme cluster, Ctrl+←/→ by word, Ctrl+Backspace/Ctrl+W, Ctrl+U and Ctrl+K, and horizontal scrolling by display width. The text field shows the terminal cursor.
- `KeyMap` (`Add`, `Handle`, `Hints`, `HelpLines`) and `KeyBinding` (`Parse`, `Matches`).
- Spinners and visible toasts make the app tick while they're drawn, even with `WithTickRate(null)`.
- In conhost (the classic Windows console, whose fonts have no fallback), widgets draw only glyphs its fonts have: gauges and sparklines use half and full blocks, the braille spinner becomes the line spinner, and rounded borders become single. Windows Terminal and other terminals keep the full glyphs.
- Example: **Services** (the §3.1 async program against a fake HTTP API) and **Widgets** (every widget, theme toggle, Unicode panel) demos.
- `Constraint` (`Length`, `Percentage`, `Ratio`, `Min`, `Max`, `Fill(weight)`), `Flex` and `Direction` for layouts.
- `Layout.Add(name, Constraint)`, `Spacing`, `Margin`, `Flex` and `Layout.Split(area, direction, constraints)`.
- Event loop (§6): keys, ticks, resizes, posted actions and redraw requests go through one queue, handled on the thread that called `Run()`. Keys are read on a background thread.
- `App.OnKey`, `OnEvent` (`KeyEvent`, `ResizeEvent`, `TickEvent`), `OnError` (`ErrorAction.Continue`/`Quit`/`Rethrow`) and `OnSetup(Action<App>)`.
- `App.Post`, `RequestRedraw` and `Quit` are thread-safe. `App.RunTask` starts async work whose continuations resume on the UI thread (a `SynchronizationContext` is installed while running); its exceptions go to `OnError`.
- `App.RunAsync`, `Stopping` (cancelled on quit), `Size` and `Log`.
- `App.WithTickRate` (default 250 ms; `null` redraws only on events), `WithTimeProvider`, `WithBackend`, `WithInput`, `WithColorMode` and `CaptureConsoleOutput`.
- `Loadable<T>` and `LoadStatus`: async load state with stale values, last-load-wins, cancellation and `Match`/`Switch`.
- `LogBuffer` and `LogEntry`. While an app runs, `Console.Out` and `Console.Error` go into `app.Log`; error lines are written to stderr on exit.
- `InputRouter.Global`, `InputRouter.Refresh` and `Add(name, handler, capturesText)`.
- `ITerminalBackend` and `CellUpdate`. `Terminal(ITerminalBackend)` draws through any backend.
- `AnsiBackend`, the new default: one buffered UTF-8 write per frame, synchronized output (`?2026`), cursor and SGR tracking so only changes are emitted, colors downsampled to the detected `ColorMode`. An unchanged frame writes zero bytes and allocates nothing.
- `LegacyConsoleBackend`, used automatically on Windows consoles where VT can't be enabled (16 colors, no alternate screen). Line wrap is turned off while it runs, so the bottom-right cell doesn't scroll the screen.
- `TestBackend`, an in-memory screen for testing apps (`Lines`, `CellAt`, `CursorPosition`, `CursorVisible`).
- `IInputSource`, `ConsoleInputSource` and `ScriptedInputSource`.
- `TerminalNotSupportedException`, thrown when stdin or stdout is redirected.
- `TerminalLease`: terminals share a process-wide, ref-counted session.
- `tests/MiniTui.Tests` xUnit project with smoke tests, included in `src/MiniTui.sln`.
- CI workflow (`.github/workflows/ci.yml`): build with `-warnaserror`, test, and pack dry run on Ubuntu, Windows and macOS; code coverage collected on Ubuntu.
- Symbol packages (`.snupkg`), SourceLink repository metadata and embedded untracked sources.
- `Color` (reset, 16 named, 256-indexed and RGB, `FromHex`), with implicit conversion from `ConsoleColor`.
- `ColorMode` and `ColorCapability.Detect()`, which reads `NO_COLOR`, `COLORTERM`, `WT_SESSION`, `TERM_PROGRAM` and `TERM`. Colors are downsampled to the detected mode at encode time.
- `Style` and `Modifiers`. A style is a patch: unset colors keep the cell's existing ones.
- `Span`, `Line`, `Text` and `Alignment` for styled text.
- `UnicodeWidth` (`Of`, `OfGrapheme`, `Graphemes`, `Truncate`), with East Asian Width and emoji tables generated from Unicode 16.0 by `tools/GenWidthTable.cs`.
- `Buffer.SetString`, `SetLine`, `SetStyle`, `SetCursor`/`CursorRequest` and `ToLines`.
- `Rect.IsEmpty`, `Intersect`, `Clamp`, `Inner(Margin)`, `Rows()` and `Columns()`, and the `Margin` type.

### Changed
- The static `Widgets` functions and `Buffer.DrawBox`, `DrawBoxWithTitle` and `DrawProgressBar` draw through the new builders, so they're themed, width-aware and never throw on small or off-screen areas (D6). Titles and cells that don't fit end in `…`.
- An unfocused list shows its selection with the `SelectionUnfocused` style instead of hiding it (D11).
- `Widgets.Table` sizes columns with the layout solver (one column of spacing) and scrolls to keep the selected row visible (D15).
- `InputHelpers.HandleTextInput` and `IsTextEditKey` accept non-ASCII characters (D8).
- `InputHelpers.HandleCommandInput` no longer changes the history list it was given, and `CommandInputState.WithHistory` copies the list (D13).
- `StatusBar` draws on the area's row when the area is one row high, and truncates the left text when both sides don't fit.
- `Modal` wraps its text, scrolls it with `State(ScrollState)`, and leaves a 2-cell margin around it.
- `Layout` uses a deterministic constraint algorithm (§7.2). The indexer builds the layout if needed, and an unknown region name throws `KeyNotFoundException` naming it. Adding two regions with the same name throws `ArgumentException`.
- Version 0.9.0.
- The Example is one app with a stack of screens, with no nested `App.Run`; every demo uses the current APIs. Services gains a detail screen and delete with a confirm dialog.
- `App.RunTask` runs its work under the app's synchronization context even when called from another context, so its awaits resume on the UI thread.
- The app draws a frame only when something happened (input, tick, posted action, resize, redraw request) instead of every 16 ms.
- In `RunWhile`, the condition is called once per key, with `Input.Poll()` returning that key, and once per wake-up without one. While an app reads the console, `Input.Poll()` elsewhere returns null instead of stealing its keys.
- An app started from inside another app receives the keys the outer app hadn't handled yet, and returns the ones it didn't handle when it exits.
- `OnSetup` runs on the UI thread after the terminal is entered, so it can `Post` and start loads.
- On Windows, VT processing is enabled and the output encoding is set to UTF-8 while an app runs; both are restored afterwards.
- `Buffer.Set` and `FillRect` store a symbol that starts with a zero-width character (a lone combining mark, ZWJ, VS16) as a space, and `SetString` skips such graphemes, so they can't merge with the neighbouring cell on screen.
- `MiniTui.csproj` now builds with `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, missing XML docs (CS1591) as errors, `IsAotCompatible`, and deterministic/CI builds.
- The publish workflow runs tests before packing and takes the package version from the tag (`v1.2.3` → `1.2.3`).
- `Terminal.Dispose()` calls `GC.SuppressFinalize`.
- `Cell` is now `Cell(string Symbol, Color Fg, Color Bg, Modifiers Modifiers)`: one grapheme cluster per cell, and wide characters are followed by a continuation cell. `Cell.Fg`/`Bg` are `Color` instead of `ConsoleColor`.
- `Cell.Empty` uses the terminal's default colors instead of gray on black (D10).
- `Buffer.DrawText` and `DrawTextCentered` take `ConsoleColor? bg = null`. Null keeps the existing background; previously it was painted black (D10). `DrawHLine`, `DrawVLine`, `DrawBox`, `DrawBoxWithTitle` and `DrawProgressBar` keep the background too.
- `Rect.SplitHorizontal`/`SplitVertical` clamp the split offset, so neither part has a negative size.
- `Layout` moved to `Layout/Layout.cs`, `Buffer`/`Geometry` to `Core/` (no API change).

### Deprecated
- `App.RunWhile`, `Input.Poll` and `Input.Read`. Use `Run` or `RunAsync` with `OnKey`, a `KeyMap` or screens.
- `Input.IsKey`, `IsChar`, `IsQuit` and `InputHelpers.IsListNavigationKey`. Use `KeyMap` and `KeyBinding`.
- `InputRouter`, `App.UseRouter` and `FocusManager`. Use screens, components and `FocusScope`.
- `Widget.Modal`, `Widget.Confirm`, `ModalBuilder`, `ConfirmBuilder`, `ConfirmState`, `InputHelpers.HandleConfirmInput`, `InputRouter.WithModal` and `InputHelpers.IsModalDismissKey`. Use `App.Overlays`.
- The static `Widgets` class. Use the `Widget.*` builders; `Widgets` becomes internal in 1.0.
- `Buffer.DrawText`, `DrawTextCentered`, `DrawBox`, `DrawBoxWithTitle` and `DrawProgressBar`. Use `SetString`, `Widget.Paragraph`, `Widget.Block` and `Widget.Gauge`.
- `ListState`, `TextInputState`, `CommandInputState`, `CommandInputResult`, `TabsState` and `InputHelpers.HandleListNavigation`, `HandleTextInput`, `IsTextEditKey`, `HandleCommandInput`, `HandleTabsNavigation`. Use `SelectionState`, `TextFieldState`, `CommandLineState` and `TabBarState`.
- `Widgets.TextInput`, `Widgets.CommandInput`, `Widgets.Tabs`, `Widget.CommandInput()`, `Widget.Tabs()`, `CommandInputBuilder`, `TabsBuilder` and `ListBuilder.State(ListState)`. Use `Widget.TextField()`, `Widget.CommandLine()`, `Widget.TabBar()` and `State(SelectionState)`.
- `Layout.Add(name, int)` and `Layout.AddFill(name)`. Use `Add(name, Constraint.Length(n))` and `Add(name, Constraint.Fill())`.
- `App.WithFrameDelay`. Use `WithTickRate`.
- `Cell(char, ConsoleColor, ConsoleColor)`, `Cell.Char` and `Cell.From(char)`. Use `new Cell(string, Color, Color, Modifiers)`, `Cell.Symbol` and `Cell.Empty with { Symbol = ... }`.

### Removed
- Stale duplicate solution file `src/MiniTui/MiniTui.sln` (D19). `src/MiniTui.sln` is the canonical solution.

### Fixed
- Ctrl+C is delivered as input (`TreatControlCAsInput`) and quits `App.Run`/`RunWhile`, instead of killing the process with the terminal left in TUI mode. `Input.IsQuit` also recognises Ctrl+C sent as `\x03` (D1).
- Apps run on the alternate screen and never call `Console.Clear()`, so the user's screen and scrollback are preserved (D2).
- A frame is one write instead of thousands of console calls (D5).
- The terminal is restored on SIGTERM, SIGHUP, SIGQUIT, process exit and unhandled exceptions.
- Nested apps share the terminal session: the inner app doesn't clear the screen, and the outer app redraws fully when it exits, without `app.Invalidate()` (D17).
- `Layout` never gives a region a negative size when fixed sizes exceed the space (they shrink proportionally), and the integer-division remainder is distributed instead of dropped: three fills in 10 rows are 4/3/3 (D7).
- All pending keys are handled before the next frame (up to 256 per frame), so pasted text and key repeat are no longer throttled to one key per frame (D3).
- `RunWhile` no longer drops keys: a key is either taken by the condition through `Input.Poll()` or passed to the router, never lost (D4).
- `InputRouter` recomputes the modal stack every loop iteration (`Refresh`), so a modal opened from code is active at the next render without a key press (D12).
- An idle app no longer redraws every 16 ms (D16).
- README license section now states MIT, matching `LICENSE` and the package metadata (D18).
- README box-style examples show the actual box-drawing glyphs (D18).
- Drawing never throws for any coordinates or `Rect`, and `Rect.Shrink` never returns a negative size; an over-shrunk rect collapses to its center (D6). `Widgets.List`, `Table` and `Modal` no longer throw on narrow areas.
- Wide characters and surrogate pairs are no longer split. Text, truncation and centering use display width, including in the existing widgets (D9).
- Control characters (including ESC) in drawn text are replaced with U+FFFD, so data can't inject escape sequences (D14).

## [0.7.0]

### Added
- Tabs widget for multi-panel UIs.
