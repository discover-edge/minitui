# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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
- README license section now states MIT, matching `LICENSE` and the package metadata (D18).
- README box-style examples show the actual box-drawing glyphs (D18).
- Drawing never throws for any coordinates or `Rect`, and `Rect.Shrink` never returns a negative size; an over-shrunk rect collapses to its center (D6). `Widgets.List`, `Table` and `Modal` no longer throw on narrow areas.
- Wide characters and surrogate pairs are no longer split. Text, truncation and centering use display width, including in the existing widgets (D9).
- Control characters (including ESC) in drawn text are replaced with U+FFFD, so data can't inject escape sequences (D14).

## [0.7.0]

### Added
- Tabs widget for multi-panel UIs.
