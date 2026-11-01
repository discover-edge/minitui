# MiniTui TODO

## Done in 1.0
- [x] Scrollbar indicator for lists - show position in long lists (e.g., `[1-10 of 50]`)
- [x] Color themes - configurable color schemes instead of hardcoded colors (`Theme`, `App.WithTheme`)
- [x] Confirmation dialog (`app.Overlays.ConfirmAsync`)
- [x] Tabs widget (`Widget.TabBar`)
- [x] Table widget - columns with headers, sorting markers, constraint widths, scrolling (`Widget.Table`)
- [x] Toast/notification widget - auto-dismiss visual messages (`ToastQueue`, `Widget.Toasts`)
- [x] Spinner/loading indicator - for async operations (`Widget.Spinner`, `Loadable<T>`)
- [x] Handle terminal resize - detect and respond to window size changes
- [x] Better text wrapping - for modals with long content (`Widget.Paragraph`, `MessageDialog`)

## Post-1.0
Planned in [docs/SPEC-1.x.md](docs/SPEC-1.x.md), one phase each.

- [x] Raw input parser (`TerminalInputSource`): Unix raw mode in 1.1, Windows VT input in Phase 10a
- [x] Mouse support - clicking to focus, scrolling (`App.WithMouse`) — [docs/SPEC-1.x.md](docs/SPEC-1.x.md) §4
- [x] Bracketed paste (`?2004`) — [docs/SPEC-1.x.md](docs/SPEC-1.x.md) §3
- [x] Popup menu - context menus, dropdowns (`PopupMenu`, `Select`, `SelectState`) — [docs/SPEC-1.x.md](docs/SPEC-1.x.md) §7
- [x] Multi-line text input widget (`TextArea`, `TextAreaState`) — [docs/SPEC-1.x.md](docs/SPEC-1.x.md) §6
- [x] Canvas (braille 2×4), line Chart and BarChart (`Canvas`, `Chart`, `BarChart`) — [docs/SPEC-1.x.md](docs/SPEC-1.x.md) §8
- [x] Debug mode - visualize focus, layout boundaries and frame time (`App.WithDebugKey`, `ShowDebug`, `LastFrame`) — [docs/SPEC-1.x.md](docs/SPEC-1.x.md) §9
- [ ] Text selection, copy/cut and the system clipboard: keys, clipboard and undo in 16a (`Clipboard`, `Select`, `Undo`); mouse drag in 16b — [docs/SPEC-1.x.md](docs/SPEC-1.x.md) §10
- [x] Ctrl+Z suspend/resume (SIGTSTP/SIGCONT; `App.Suspend`, `App.WithSuspendKey`) — [docs/SPEC-1.x.md](docs/SPEC-1.x.md) §5
