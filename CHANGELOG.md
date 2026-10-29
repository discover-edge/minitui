# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `tests/MiniTui.Tests` xUnit project with smoke tests, included in `src/MiniTui.sln`.
- CI workflow (`.github/workflows/ci.yml`): build with `-warnaserror`, test, and pack dry run on Ubuntu, Windows and macOS; code coverage collected on Ubuntu.
- Symbol packages (`.snupkg`), SourceLink repository metadata and embedded untracked sources.

### Changed
- `MiniTui.csproj` now builds with `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, missing XML docs (CS1591) as errors, `IsAotCompatible`, and deterministic/CI builds.
- The publish workflow runs tests before packing and takes the package version from the tag (`v1.2.3` → `1.2.3`).
- `Terminal.Dispose()` calls `GC.SuppressFinalize`.

### Removed
- Stale duplicate solution file `src/MiniTui/MiniTui.sln` (D19). `src/MiniTui.sln` is the canonical solution.

### Fixed
- README license section now states MIT, matching `LICENSE` and the package metadata (D18).
- README box-style examples show the actual box-drawing glyphs (D18).

## [0.7.0]

### Added
- Tabs widget for multi-panel UIs.
