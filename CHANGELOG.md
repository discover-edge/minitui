# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `ITerminalBackend` and `CellUpdate`. `Terminal(ITerminalBackend)` draws through any backend.
- `AnsiBackend`, the new default: one buffered UTF-8 write per frame, synchronized output (`?2026`), cursor and SGR tracking so only changes are emitted, colors downsampled to the detected `ColorMode`. An unchanged frame writes zero bytes and allocates nothing.
- `LegacyConsoleBackend`, used automatically on Windows consoles where VT can't be enabled (16 colors, no alternate screen).
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
- `Cell(char, ConsoleColor, ConsoleColor)`, `Cell.Char` and `Cell.From(char)`. Use `new Cell(string, Color, Color, Modifiers)`, `Cell.Symbol` and `Cell.Empty with { Symbol = ... }`.

### Removed
- Stale duplicate solution file `src/MiniTui/MiniTui.sln` (D19). `src/MiniTui.sln` is the canonical solution.

### Fixed
- Ctrl+C is delivered as input (`TreatControlCAsInput`) and quits `App.Run`/`RunWhile`, instead of killing the process with the terminal left in TUI mode. `Input.IsQuit` also recognises Ctrl+C sent as `\x03` (D1).
- Apps run on the alternate screen and never call `Console.Clear()`, so the user's screen and scrollback are preserved (D2).
- A frame is one write instead of thousands of console calls (D5).
- The terminal is restored on SIGTERM, SIGHUP, SIGQUIT, process exit and unhandled exceptions.
- Nested apps share the terminal session: the inner app doesn't clear the screen, and the outer app redraws fully when it exits, without `app.Invalidate()` (D17).
- README license section now states MIT, matching `LICENSE` and the package metadata (D18).
- README box-style examples show the actual box-drawing glyphs (D18).
- Drawing never throws for any coordinates or `Rect`, and `Rect.Shrink` never returns a negative size; an over-shrunk rect collapses to its center (D6). `Widgets.List`, `Table` and `Modal` no longer throw on narrow areas.
- Wide characters and surrogate pairs are no longer split. Text, truncation and centering use display width, including in the existing widgets (D9).
- Control characters (including ESC) in drawn text are replaced with U+FFFD, so data can't inject escape sequences (D14).

## [0.7.0]

### Added
- Tabs widget for multi-panel UIs.
