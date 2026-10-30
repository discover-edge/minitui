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

- [x] Raw input parser on Unix (`TerminalInputSource`); Windows `ReadConsoleInput` comes with mouse support
- [ ] Mouse support - clicking to focus, scrolling (SGR 1006)
- [ ] Bracketed paste (`?2004`)
- [ ] Popup menu - context menus, dropdowns (PopupMenu/Select)
- [ ] Multi-line text input widget (TextArea)
- [ ] Canvas (braille 2×4), line Chart and BarChart
- [ ] Debug mode - visualize focus, layout boundaries and frame time
- [ ] Ctrl+Z suspend/resume (SIGTSTP/SIGCONT)
