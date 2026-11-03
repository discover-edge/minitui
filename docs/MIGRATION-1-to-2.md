# Migrating from MiniTui 1.x to 2.0

MiniTui 2.0 renames one type and adds a parameter to one method. Nothing else breaks.

## `Buffer` is now `CellBuffer`

`MiniTui.Buffer` clashed with `System.Buffer`: with implicit usings on (the default in `dotnet new console`), every
`Render(Buffer buf, ...)` override failed to compile until the file added `using Buffer = MiniTui.Buffer;`. The type
is now `MiniTui.CellBuffer`, with the same members.

- Replace `Buffer` with `CellBuffer` where it names MiniTui's type: `Render` overrides, `Draw` helpers,
  `new Buffer(w, h)`, `Action<Buffer, Rect>` render callbacks.
- Delete `using Buffer = MiniTui.Buffer;`.

On Linux or macOS, from your project's folder (check the diff: it also renames a `Buffer` that isn't MiniTui's):

```bash
grep -rlw Buffer --include=*.cs . | xargs sed -i -E '/using Buffer = MiniTui\.Buffer;/d; s/(^|[^.[:alnum:]_])Buffer([^[:alnum:]_]|$)/\1CellBuffer\2/g; s/MiniTui\.Buffer\b/MiniTui.CellBuffer/g'
```

## `ConfirmAsync` takes `yesFirst`

`OverlayStack.ConfirmAsync` and `ConfirmDialog`'s constructor have a new optional parameter, `yesFirst` (true by
default, as before; false selects No first, for destructive actions). Source that calls them compiles unchanged;
code compiled against 1.x has to be rebuilt.

