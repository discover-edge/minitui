# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `Widget.TextArea()` and `TextAreaState`: multi-line text input. The text field's editing keys work across lines, plus Enter (new line), ↑ ↓ by wrapped row with a remembered column, PgUp/PgDn, Home/End by row then by line, and Ctrl+Home/End. Lines wrap at words by default (`Wrap(WrapMode.Char)` or `WrapMode.None` to scroll sideways), and the view scrolls to keep the cursor visible. `Paste` keeps line breaks, tabs become spaces, and `HandleMouse` places the cursor on a click and scrolls with the wheel. `TextPosition` is the cursor type.
- Example: the Form demo has a Notes text area.
- `docs/SPEC-1.x.md` §6: the TextArea spec.

## [1.2.0] - 2026-10-30

MiniTui 1.2: Windows reads input the way Linux and macOS do (with bracketed paste), opt-in mouse support everywhere, and suspend and resume.

### Added
- Windows reads the console in VT input mode and decodes keys with MiniTui's own decoder, as Linux and macOS do since 1.1. Waiting for input on Windows no longer wakes every 5 ms, and a window resize is noticed at once.
- Bracketed paste on Windows: in Windows Terminal a paste arrives as one `PasteEvent`. Conhost has no bracketed paste, so there Ctrl+V pastes the clipboard's text as a `PasteEvent`.
- Mouse support, opt-in with `App.WithMouse()`: SGR (and X10) mouse reports on Linux and macOS and in Windows Terminal, and console mouse records in conhost. A `MouseEvent` goes to `App.OnMouse`, then to `Component.HandleMouse` on the component under the pointer and its ancestors; a press focuses that component first. `RenderContext.Render(child, buf, rect)` renders a child and records its `Component.Area` for hit testing.
- `SelectionState.HandleMouse` (lists and tables: the wheel moves the selection, a click selects a row), `ScrollState.HandleMouse` (the wheel scrolls text) and `TabBarState.HandleMouse` (a click selects a tab). `ConfirmDialog` answers clicks on its buttons; `MessageDialog` and `HelpOverlay` scroll with the wheel.
- `ScriptedInputSource.EnqueueMouse`, and `ComponentHarness.Click`, `Scroll` and `Mouse`, for tests.
- Example: the mouse is on; clicks focus fields, toggle the checkbox, select rows and tabs and open menu items, and the Input demo shows mouse events.
- `docs/SPEC-1.x.md` §4: the mouse and Windows input spec.
- `App.Suspend(action)` gives the terminal back while `action` runs, for running `$EDITOR` or another program in it: input stops, mouse reporting and bracketed paste turn off, the terminal mode is restored and the alternate screen left, then all of it comes back and the app redraws. Ctrl+C during the action stops the child program, not the app. Works on every platform.
- `App.SuspendProcess()` stops the process and returns to the shell with the terminal restored (Linux and macOS); `fg` resumes the app, which redraws. `App.WithSuspendKey()` makes an unhandled Ctrl+Z do that; it's off by default.
- `ResumeEvent`, sent to `App.OnEvent` whenever the app takes the terminal back.
- Example: Ctrl+Z suspends the demos, and Ctrl+E in the Input demo edits a file in `$VISUAL` or `$EDITOR`.
- `docs/SPEC-1.x.md` §5: the suspend and resume spec.

### Changed
- On Windows, keys decode by the same rules as on Linux and macOS: Esc is recognized after up to 50 ms, and Ctrl+Enter, Ctrl+Tab and Ctrl+digits arrive without the Ctrl. `MINITUI_INPUT=console` restores the 1.0 input path on Windows too.
- On Windows, pasted text no longer arrives as keys in Windows Terminal, and Ctrl+V in conhost pastes instead of being a key. A component that edits text needs `HandlePaste` to accept pastes, as on Linux and macOS since 1.1.
- On Linux and macOS, a `SIGTSTP` sent to a running app from outside (`kill -TSTP`) restores the terminal before the process stops, and the app redraws when it's continued. Before, the process stopped with the terminal still in raw mode on the alternate screen.

## [1.1.0] - 2026-10-30

MiniTui 1.1: raw terminal input and bracketed paste on Linux and macOS. Windows input is unchanged.

### Added
- `TerminalInputSource`, the new default input source. On Linux and macOS it reads the terminal in raw mode and decodes keys with MiniTui's own decoder: Esc, Alt and Alt+Shift letters, Ctrl+letters, Shift/Ctrl/Alt arrows, Home/End/PgUp/PgDn/Insert/Delete, Shift+Tab and F1–F12 decode the same way in every terminal. On Windows it reads keys as before.
- Bracketed paste on Linux and macOS: a paste arrives as one `PasteEvent` instead of a key per character. `Component.HandlePaste` and `App.OnPaste` receive it, `TextFieldState.Paste` inserts it as one line, and `CommandLineOverlay` handles it. `ScriptedInputSource.EnqueuePaste` and `ComponentHarness.Paste` script pastes in tests.
- The `MINITUI_INPUT=console` environment variable selects the 1.0 input path (`ConsoleInputSource`) without recompiling.
- Example: an **Input** demo that shows how each key and paste was decoded, and the Form screen accepts pastes.
- `docs/SPEC-1.x.md`: the post-1.0 roadmap and the raw input and bracketed paste specs.

### Changed
- On Linux and macOS, Esc is recognized after up to 50 ms (`TerminalInputSource.EscapeTimeout`), and Ctrl+Z, Ctrl+\, Ctrl+S, Ctrl+Q, Ctrl+V and Ctrl+O reach the app as keys instead of being handled by the terminal.
- Waiting for input no longer wakes every 5 ms on Linux and macOS.
- On Linux and macOS, pasted text no longer arrives as keys, so a pasted line break doesn't submit and pasted letters don't fire key bindings. A component that edits text needs `HandlePaste` (for example `=> State.Paste(text)`) to accept pastes; a paste nothing handles is dropped. On Windows and with `MINITUI_INPUT=console`, pastes still arrive as keys.

### Fixed
- A fast burst of keys (about 1,000 arrow keys at once) could decode as a stray Esc and leave the current screen. Key sequences split across reads are now reassembled.

## [1.0.0] - 2026-10-29

MiniTui 1.0: an event loop with async support, constraint layout, a themed widget set, and screens and
components for larger apps. It removes the 0.7 APIs that 0.8 and 0.9 marked obsolete (they were never
released separately); [docs/MIGRATION-0.7-to-1.0.md](docs/MIGRATION-0.7-to-1.0.md) maps each one to its
replacement.

### Added
- `docs/MIGRATION-0.7-to-1.0.md`.
- The public API is tracked with `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.Shipped.txt`); changing it fails the build until the file is updated.
- CI requires at least 85% line coverage of `src/MiniTui`.
- The publish workflow also pushes to nuget.org with trusted publishing (no stored API key) when the repository has a `NUGET_USER` variable.
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
- Version 1.0.0.
- The app draws a frame only when something happened (input, tick, posted action, resize, redraw request) instead of every 16 ms.
- `OnKey` sees every key first; keys it doesn't handle are routed to overlays, screens and `App.Keys` (§9.4).
- An app started from inside another app receives the keys the outer app hadn't handled yet, and returns the ones it didn't handle when it exits.
- `OnSetup` runs on the UI thread after the terminal is entered, so it can `Post` and start loads.
- `App.RunTask` runs its work under the app's synchronization context even when called from another context, so its awaits resume on the UI thread.
- On Windows, VT processing is enabled and the output encoding is set to UTF-8 while an app runs; both are restored afterwards.
- `Layout` uses a deterministic constraint algorithm (§7.2). The indexer builds the layout if needed, and an unknown region name throws `KeyNotFoundException` naming it. Adding two regions with the same name throws `ArgumentException`.
- `Cell` is now `Cell(string Symbol, Color Fg, Color Bg, Modifiers Modifiers)`: one grapheme cluster per cell, and wide characters are followed by a continuation cell. `Cell.Fg`/`Bg` are `Color` instead of `ConsoleColor`.
- `Cell.Empty` uses the terminal's default colors instead of gray on black (D10).
- `Buffer.DrawHLine` and `DrawVLine` take a `Style` instead of a `ConsoleColor`, and keep the existing background.
- `Buffer.Set` and `FillRect` store a symbol that starts with a zero-width character (a lone combining mark, ZWJ, VS16) as a space, and `SetString` skips such graphemes, so they can't merge with the neighbouring cell on screen.
- Widgets draw with the theme, measure text by display width, end truncated text with `…`, and never throw on small or off-screen areas.
- An unfocused list or table shows its selection with the `SelectionUnfocused` style instead of hiding it (D11).
- `StatusBar` draws on the area's row when the area is one row high, and truncates the left text when both sides don't fit.
- `Rect.SplitHorizontal`/`SplitVertical` clamp the split offset, so neither part has a negative size.
- The Example is one app with a stack of screens, with no nested `App.Run`, and adds the Services and Widgets demos.
- `MiniTui.csproj` builds with `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, missing XML docs (CS1591) as errors, `IsAotCompatible`, and deterministic/CI builds.
- The publish workflow runs tests before packing and takes the package version from the tag (`v1.2.3` → `1.2.3`).
- `Terminal.Dispose()` calls `GC.SuppressFinalize`.
- Source files moved into `App/`, `Core/`, `Text/`, `Layout/`, `Terminal/`, `Components/`, `Input/` and `Widgets/` (no API change).

### Removed
See [docs/MIGRATION-0.7-to-1.0.md](docs/MIGRATION-0.7-to-1.0.md) for the replacement of each.
- `App.RunWhile`, `App.WithFrameDelay`, `App.UseRouter`, and the static `Input` class (`Poll`, `Read`, `IsKey`, `IsChar`, `IsQuit`).
- `InputRouter`, `FocusManager` and `InputHelpers`.
- `ListState`, `TextInputState`, `CommandInputState`, `CommandInputResult`, `TabsState` and `ConfirmState`.
- The static `Widgets` class (now internal), `Widget.Modal`, `Widget.Confirm`, `Widget.CommandInput`, `Widget.Tabs`, `ModalBuilder`, `ConfirmBuilder`, `CommandInputBuilder`, `TabsBuilder` and `ListBuilder.State(ListState)`.
- `Buffer.DrawText`, `DrawTextCentered`, `DrawBox`, `DrawBoxWithTitle` and `DrawProgressBar`.
- `Layout.Add(name, int)` and `Layout.AddFill(name)`.
- `Cell(char, ConsoleColor, ConsoleColor)`, `Cell.Char` and `Cell.From(char)`.
- `Command`, which had no public members, is internal.
- Stale duplicate solution file `src/MiniTui/MiniTui.sln` (D19). `src/MiniTui.sln` is the canonical solution.

### Fixed
- `LICENSE` contained the AGPL-3.0 text while the package and README declared MIT; it is now the MIT license (copyright Discover Edge LLC), and the package lists Discover Edge LLC as its author.
- Ctrl+C is delivered as input (`TreatControlCAsInput`) and quits `App.Run`, instead of killing the process with the terminal left in TUI mode; Ctrl+C sent as `\x03` is recognised too (D1).
- Apps run on the alternate screen and never call `Console.Clear()`, so the user's screen and scrollback are preserved (D2).
- A frame is one write instead of thousands of console calls (D5).
- The terminal is restored on SIGTERM, SIGHUP, SIGQUIT, process exit and unhandled exceptions.
- Nested apps share the terminal session: the inner app doesn't clear the screen, and the outer app redraws fully when it exits, without `app.Invalidate()` (D17).
- `Layout` never gives a region a negative size when fixed sizes exceed the space (they shrink proportionally), and the integer-division remainder is distributed instead of dropped: three fills in 10 rows are 4/3/3 (D7).
- All pending keys are handled before the next frame (up to 256 per frame), so pasted text and key repeat are no longer throttled to one key per frame (D3).
- Keys are no longer dropped: each key goes through one path (`OnKey`, then routing) exactly once (D4).
- A dialog opened from code (for example by an HTTP error) is modal and drawn at the next frame, without a key press (D12).
- An idle app no longer redraws every 16 ms (D16).
- README license section now states MIT, matching `LICENSE` and the package metadata (D18).
- README box-style examples show the actual box-drawing glyphs (D18).
- Drawing never throws for any coordinates or `Rect`, and `Rect.Shrink` never returns a negative size; an over-shrunk rect collapses to its center (D6). No widget throws on narrow areas.
- Wide characters and surrogate pairs are no longer split. Text, truncation and centering use display width, including in the existing widgets (D9).
- Control characters (including ESC) in drawn text are replaced with U+FFFD, so data can't inject escape sequences (D14).
- With `NO_COLOR`, an explicit `WithTheme(Theme.Default)` also becomes `Theme.Monochrome`, so the selection stays visible.
- Text input accepts any non-control character, including accented letters, CJK and emoji (D8).
- Widgets no longer paint a black background over light or custom terminal themes (D10).
- Command history is an immutable list that copies its seed, so copies of a state never share or change each other's history (D13).
- Tables size columns with constraints instead of equal widths, show rows beyond the area by scrolling, and keep the selected row visible (D15).

## [0.7.0]

### Added
- Tabs widget for multi-panel UIs.
