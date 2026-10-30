# MiniTui 1.x — Post-1.0 Spec

| | |
|---|---|
| **Status** | §2 (Phase 8) done; §3 (Phase 9) done; the other roadmap items are to be specced |
| **Baseline** | `discover-edge/minitui` @ `v1.0.0` |
| **Target** | v1.1.0 and later minor releases |
| **Builds on** | [`docs/SPEC.md`](SPEC.md) (the 1.0 spec). Section references like "§9.4" without a file name point there. |

---

## 0. Instructions for Claude Code

The rules in SPEC.md §0 still apply (one branch and PR per phase, test-first defect fixes, the build gate, no
runtime dependencies, match Ratatui when this spec is silent, stop and ask before deviating from a
**Decision**, CHANGELOG and README in every PR, Windows-only code listed for manual verification). These replace
or add to them:

1. **Additive only.** 1.x follows semantic versioning: no public API is removed or changed incompatibly. New
   public members go in `PublicAPI.Unshipped.txt`; they move to `PublicAPI.Shipped.txt` when the version that
   ships them is tagged. No `[Obsolete]` shims are needed, because nothing is replaced.
2. **Behavior changes** that existing apps could notice (a key that used to do one thing now does another) are
   called out under "Changed" in the CHANGELOG and in the PR description.
3. **Phases are numbered on from SPEC.md §11**, starting at 8. Each roadmap item (§1) gets its own section in
   this file before work on it starts.

---

## 1. Roadmap

| Phase | Scope | Depends on | Spec | Version |
|---|---|---|---|---|
| **8. Raw input** | Read Unix terminal input as bytes and decode it ourselves: reliable Esc, Alt, function keys and modifier combinations, and no stray Esc from split sequences. Groundwork for paste and mouse. | — | §2 | **1.1.0** |
| **9. Bracketed paste** | `?2004`: a paste arrives as one `PasteEvent` instead of thousands of keys; text fields insert it in one step. | 8 | §3 | 1.1.0 |
| **10. Mouse and Windows input** | Windows reads `ReadConsoleInput` records directly. Mouse on both platforms (SGR 1006 on Unix, `MOUSE_EVENT` records on Windows): `MouseEvent`, click to focus, wheel scrolling in lists, tables and paragraphs, clicking buttons, checkboxes and tabs. | 8 | To be specced | 1.2.0 |
| **11. Suspend and resume** | Ctrl+Z suspends the app (SIGTSTP), restoring the terminal; resuming (SIGCONT) re-enters it and redraws. Also `app.Suspend(Action)` for running a child process such as `$EDITOR` in the real terminal. | 8 | To be specced | 1.2.0 |
| **12. TextArea** | Multi-line text editing widget and `TextAreaState`: wrapping, vertical scrolling, selection-free editing with the §8.4 keys, paste of multi-line text. | 9 | To be specced | 1.3.0 |
| **13. PopupMenu and Select** | A popup menu overlay (context menus, Enter/Esc, type-ahead) and a `Select` dropdown field built on it. | 6 (overlays) | To be specced | 1.3.0 |
| **14. Canvas and charts** | `Canvas` with braille (2×4 dots per cell) drawing, line `Chart` with axes and labels, and `BarChart`. | — | To be specced | 1.4.0 |
| **15. Debug overlay** | A toggleable overlay showing layout rects, the focus chain and frame time / bytes per frame. | — | To be specced | 1.4.0 |

Phases 8–11 are input work, so they go first: each depends on Phase 8, and Phase 8 fixes a known bug (a fast
burst of arrow keys can produce a stray Esc, found in 1.0 QA). Phases 12–15 are independent of each other and
can be reordered.

---

## 2. Raw input (Phase 8)

### 2.1 Purpose

On Unix, MiniTui 1.0 reads keys through `Console.ReadKey`, which decodes the terminal's escape sequences inside
.NET. That decoder loses information and can't be extended:
- **Split sequences.** When a burst of input crosses a read boundary (about 3 KB of arrow keys at once in tmux),
  the tail of one sequence and the head of the next decode as a stray Esc plus printable characters. Esc pops
  the screen in a MiniTui app, so this is a real bug.
- **Alt and modifiers.** Alt+letter, Alt+arrow and Ctrl+Shift combinations come through inconsistently across
  terminals.
- **No room to grow.** Bracketed paste (Phase 9) and mouse reports (Phase 10) are escape sequences too, and
  `Console.ReadKey` can't deliver either.

Phase 8 puts the terminal in raw mode, reads stdin bytes on the input thread and decodes them with a MiniTui
decoder that keeps state across reads. Keys still reach apps as `ConsoleKeyInfo`, so no app code changes. Windows
keeps reading through `Console.ReadKey`, whose key handling is already reliable there; it switches to
`ReadConsoleInput` in Phase 10, where mouse input needs it.

**Decision:** Keys stay `ConsoleKeyInfo` everywhere in the public API (`OnKey`, `HandleKey`, `KeyMap`,
`KeyBinding`). Changing the key type would break every 1.0 app. The decoder maps everything it recognizes onto
`ConsoleKeyInfo`; keys `ConsoleKey` can't name (F13 and above, media keys) are dropped.

### 2.2 Public API

The only new public type is the input source. It becomes the default, so apps get it without changes.

```csharp
/// The default input source from 1.1: raw terminal input decoded by MiniTui on Linux and macOS,
/// Console.ReadKey on Windows (until Phase 10) and on platforms raw mode doesn't support.
public sealed class TerminalInputSource : IInputSource
{
    public static TimeSpan DefaultEscapeTimeout { get; }    // 50 ms
    public TerminalInputSource();
    public TerminalInputSource(TimeSpan escapeTimeout);     // ArgumentOutOfRangeException outside 0–1 s
    public TimeSpan EscapeTimeout { get; }
    public bool TryRead(out ConsoleKeyInfo key);            // never blocks
    public void Dispose();
}
```

- `App` uses a shared `TerminalInputSource` when `WithInput` isn't called. `ConsoleInputSource` stays public and
  unchanged; `app.WithInput(new ConsoleInputSource())` restores the 1.0 behavior.
- `app.WithInput(new TerminalInputSource(TimeSpan.FromMilliseconds(100)))` changes the escape timeout (§2.4).

### 2.3 Internal model

```csharp
// Decodes terminal input bytes. Pure: no I/O, no clock; the caller passes the time.
internal sealed class AnsiInputDecoder(TimeSpan escapeTimeout)
{
    public void Feed(ReadOnlySpan<byte> bytes, TimeSpan now, IInputSink sink);
    public TimeSpan? Deadline { get; }                  // when held bytes must be resolved; null if none are held
    public void Expire(TimeSpan now, IInputSink sink);  // resolve held bytes if now >= Deadline
}

// What the decoder produces. Phase 9 adds Paste(string), Phase 10 adds Mouse(...).
internal interface IInputSink
{
    void Key(ConsoleKeyInfo key);
}

// Optional extras the input pump uses when a source implements them.
internal interface IInputLifecycle
{
    void Activate();    // first subscriber: enter raw mode
    void Deactivate();  // last subscriber gone: restore the terminal mode
}
internal interface IWaitableInputSource
{
    bool WaitForInput(TimeSpan timeout); // block until input may be available, the timeout passes, or Deactivate
}

// libc P/Invokes ([LibraryImport], AOT-safe): isatty, tcgetattr, tcsetattr, cfmakeraw, poll, read.
[SupportedOSPlatform("linux"), SupportedOSPlatform("macos")]
internal static partial class UnixTerminal
{
    public static bool TryEnterRaw(int fd);   // false if fd isn't a terminal or a call fails
    public static void Restore();             // idempotent; never throws; safe from signal handlers
    public static bool IsRaw { get; }
}
```

### 2.4 Decoding rules

The decoder maps input onto `ConsoleKeyInfo(KeyChar, Key, shift, alt, control)`. These rules are the
contract the tests check.

**Plain bytes**

| Bytes | Key | KeyChar | Modifiers |
|---|---|---|---|
| Printable ASCII (0x20–0x7E) | Letters `A`–`Z`, digits `D0`–`D9`, space `Spacebar`, others `0` | the character | Shift for `A`–`Z` |
| UTF-8 sequence (2–4 bytes) | `0` | the character; non-BMP characters become two keys, high then low surrogate | none |
| Invalid UTF-8 | `0` | U+FFFD | none |
| 0x0D, 0x0A | `Enter` | `\r` | none |
| 0x09 | `Tab` | `\t` | none |
| 0x7F | `Backspace` | `\b` | none |
| 0x08 | `Backspace` | `\b` | Control (terminals send ^H for Ctrl+Backspace; §8.4 already handles it) |
| 0x00 | `Spacebar` | `\0` | Control |
| 0x01–0x1A, except the above | `A`–`Z` | the control character | Control |
| 0x1C–0x1F | `0` | the control character | Control |
| 0x1B | see Esc and Alt below | | |

**Esc and Alt**
- ESC followed by a byte that starts a key (not `[` or `O`) is that key with Alt added: `ESC a` is Alt+A,
  `ESC A` is Alt+Shift+A, `ESC 0x01` is Ctrl+Alt+A, `ESC 0x7F` is Alt+Backspace.
- `ESC [` and `ESC O` start a sequence (below). `ESC ESC` followed by a sequence is that sequence with Alt
  added (iTerm2 and some xterm settings send Alt+arrow this way).
- **Escape timeout.** Bytes that could still be the start of a sequence are held. If no byte arrives for
  `EscapeTimeout` (default 50 ms) after the last held byte, they resolve as typed: a lone ESC becomes `Escape`
  (`KeyChar` U+001B), ESC followed by any one byte becomes that byte with Alt, ESC ESC becomes two `Escape`s,
  `ESC [` becomes Alt+`[`, and an incomplete sequence becomes `Escape` followed by its bytes decoded as plain
  input. An incomplete UTF-8 sequence becomes U+FFFD.
- Bytes are never resolved early just because a read ended. A sequence split across reads is reassembled.
  **This fixes the stray Esc from arrow-key bursts.**

**Sequences.** CSI is `ESC [`, then parameter bytes (0x30–0x3F), intermediate bytes (0x20–0x2F) and one final
byte (0x40–0x7E). SS3 is `ESC O` and one byte.

| Sequence | Key |
|---|---|
| `CSI A` `B` `C` `D`, `SS3 A` `B` `C` `D` | `UpArrow`, `DownArrow`, `RightArrow`, `LeftArrow` |
| `CSI H`, `CSI F`, `SS3 H`, `SS3 F` | `Home`, `End` |
| `SS3 P` `Q` `R` `S`, `CSI P` `Q` `R` `S` (with modifiers) | `F1`–`F4` |
| `CSI 1~` `7~` / `4~` `8~` | `Home` / `End` |
| `CSI 2~` `3~` `5~` `6~` | `Insert`, `Delete`, `PageUp`, `PageDown` |
| `CSI 11~`–`15~`, `17~`–`21~`, `23~`, `24~` | `F1`–`F5`, `F6`–`F10`, `F11`, `F12` |
| `CSI [ A`–`E` (Linux console) | `F1`–`F5` |
| `CSI Z` | `Tab` with Shift |
| `CSI 1;m X`, `CSI n;m ~` | the key above with modifiers from `m` |

- **Modifier parameter `m`:** take `m − 1` as bits: 1 Shift, 2 Alt, 4 Control, 8 Meta (treated as Alt).
  `CSI 1;5C` is Ctrl+Right.
- `KeyChar` for named keys is `\0`, except `Tab` (`\t`).
- `CSI 1;mR` is F3 with modifiers. MiniTui never requests a cursor position report, so this form is never
  a report.
- **Unknown or malformed sequences are dropped whole**, never leaked as characters: any CSI whose final byte
  isn't in the table, SS3 with another byte, OSC (`ESC ]` … BEL or `ESC \`), DCS (`ESC P` … `ESC \`), SGR mouse
  reports (`CSI <` …, enabled in Phase 10) and paste markers (`CSI 200~`/`201~`, Phase 9). A CSI longer than 64
  bytes without a final byte is dropped, and decoding resumes at the next byte.

### 2.5 Terminal mode

- **Raw mode** applies on Linux (x64, arm64, arm) and macOS (x64, arm64) when stdin is a terminal. Elsewhere,
  or if any call fails, `TerminalInputSource` behaves exactly like `ConsoleInputSource`.
- **Entering:** `tcgetattr(0)` into a saved buffer, copy it, `cfmakeraw` on the copy, then `tcsetattr(0,
  TCSANOW)`. The `termios` struct is handled as an opaque, zero-initialized 256-byte buffer (larger than the
  Linux and macOS layouts), so MiniTui never depends on field offsets or flag values.
- **Decision:** raw mode clears `ISIG`, so Ctrl+C (0x03), Ctrl+Z (0x1A) and Ctrl+\ (0x1C) arrive as keys.
  Ctrl+C keeps quitting through the §9.4 routing. Ctrl+Z does nothing until Phase 11 adds suspend; until then,
  suspending would leave the shell on MiniTui's alternate screen. Clearing `IXON` also makes Ctrl+S and Ctrl+Q
  ordinary keys instead of freezing output.
- **Restoring:** `tcsetattr` with the saved buffer. `Deactivate` restores, and so do the existing restore
  paths in `TerminalSession` (SIGTERM, SIGHUP, SIGQUIT, process exit, unhandled exception), which call
  `UnixTerminal.Restore()` before `Leave()`. Restore is idempotent.
- **.NET resetting the mode.** .NET's console code reapplies its own terminal settings in some cases (after a
  child process started with `Process.Start` exits, and on SIGCONT). While raw mode is active, the reader
  checks the mode with `tcgetattr` at most every 250 ms while idle and reapplies it if it changed.
- MiniTui doesn't call `Console.ReadKey`, `Console.KeyAvailable` or `Console.In` while raw mode is active.

### 2.6 Reading

- `InputPump` calls `Activate` when its reader thread starts and `Deactivate` when the last subscriber leaves,
  so nested apps share one raw-mode session.
- When the source implements `IWaitableInputSource`, the pump calls `WaitForInput` instead of sleeping 5 ms.
  `TerminalInputSource` implements it with `poll(0, POLLIN, timeout)`, where the timeout is the decoder's
  deadline or 100 ms, whichever is sooner. This also lowers idle wakeups from 200/s to 10/s.
- `TryRead` first returns any decoded keys, then reads what's available (`read(0, buf, 4096)`, only after
  `poll` reported input, so it never blocks), feeds the decoder and calls `Expire` with the current time.
  Time comes from `Stopwatch.GetElapsedTime`, not the App's `TimeProvider`, because the escape timeout is about
  real terminal latency.
- `EINTR` retries. `read` returning 0 (the terminal closed) or an error stops reading: `TryRead` returns false
  from then on, and the existing SIGHUP handling restores the terminal.
- The decoder allocates nothing per key: keys go to the sink as structs, and the read buffer is reused.

### 2.7 Configuration

| Setting | Effect |
|---|---|
| `new TerminalInputSource(TimeSpan)` | Escape timeout, 0–1 s. 0 resolves held bytes at the end of each read (crossterm's behavior). |
| `app.WithInput(new ConsoleInputSource())` | The 1.0 input path. |
| `MINITUI_INPUT=console` | The default input source becomes `ConsoleInputSource`, without recompiling. An escape hatch for terminals the decoder mishandles. |

### 2.8 Example

Add an **Input** demo screen to `MiniTui.Example`. It lists the last 20 keys as decoded (`Key`, `KeyChar` as a
code point, and modifiers), with the Esc-pops-screen binding replaced by `q`, so every key can be inspected.
It's how the QA checks below are run, and how users report a terminal the decoder gets wrong.

### 2.9 Tests and acceptance

Automated (all platforms unless noted):
- **Decoder table tests** for every row in §2.4, with and without each modifier.
- **Round-trip property test:** 10,000 seeded cases. Generate a random key sequence (printable, UTF-8, emoji,
  control keys, named keys with random modifiers, Alt combinations), encode it with a test-only encoder that
  produces xterm's bytes, split the bytes at random points into chunks fed with time advancing less than the
  escape timeout, and check that the decoded keys equal the input.
- **Burst test:** 3,000 bytes of arrow-key sequences, split at every possible offset into two reads: exactly
  1,000 arrows and no `Escape`.
- **Timeout tests** with explicit times: lone ESC resolves at exactly the deadline, `ESC [` resolves to Alt+`[`,
  an incomplete CSI resolves to Escape plus characters, and a byte arriving before the deadline continues the
  sequence.
- **Unknown sequences** (CSI with unknown finals, OSC, DCS, SGR mouse, paste markers, an overlong CSI) produce no
  keys, and the input after them decodes normally.
- **Allocation test:** decoding 1 MB of mixed input allocates nothing per key
  (`GC.GetAllocatedBytesForCurrentThread` delta below a small fixed bound).
- **Pseudo-terminal test** (Linux and macOS CI): open a pty with `posix_openpt` in the test project, enter raw
  mode on the follower, check that bytes written to the leader arrive unchanged (no echo, no CR translation, 0x03
  and 0x1A delivered), restore, and check that `tcgetattr` matches the saved state byte for byte.
- `TerminalInputSource` on a non-terminal stdin (CI) behaves like `ConsoleInputSource` and doesn't throw.
- Existing tests keep passing unchanged; `ScriptedInputSource` and `ComponentHarness` aren't affected.

Manual QA (results in the PR), in WSL in Windows Terminal, tmux on Linux and SSH to Linux, using the Input demo.
There's no Mac to test on, so on macOS the pseudo-terminal tests in CI are the check: they cover entering raw
mode, decoding, reapplying and restoring there. The decoder is the same code on every platform, and Terminal.app
and iTerm2 send xterm sequences it already handles. Until someone checks a Mac by hand, the README says so.
1. A 3 KB burst of arrow keys (e.g. `tmux send-keys` or a held key) produces only arrows and never leaves the
   screen.
2. Alt+letter, Alt+Shift+letter, Alt+arrows, Ctrl+arrows, Shift+arrows, Ctrl+Shift+arrows, Shift+Tab, F1–F12,
   Insert, Delete, Home, End, PgUp and PgDn decode as named.
3. Esc responds without a noticeable delay.
4. Ctrl+C quits, and Ctrl+Z, Ctrl+S and Ctrl+Q do nothing harmful.
5. After a normal exit, Ctrl+C, the forced exception, and `kill -TERM` / `kill -HUP` from another shell, the
   shell echoes typing and `stty -a` matches its output from before the run.
6. A screen that runs `Process.Start("true")` and waits for it still decodes arrows afterwards (§2.5).
7. Pasting 1,000 characters into a text field loses none, and checks 1–10 from SPEC.md §10.5 still pass.
8. Windows Terminal and conhost: the Example behaves as in 1.0 (no Windows changes in this phase).

### 2.10 Migration impact

- No public API changes or removals; one new type in `PublicAPI.Unshipped.txt`.
- **Behavior changes on Linux and macOS** (CHANGELOG "Changed"): Esc resolves after up to 50 ms; Ctrl+Z,
  Ctrl+\, Ctrl+S, Ctrl+Q, Ctrl+V and Ctrl+O reach the app as keys instead of being handled by the terminal; keys
  that previously decoded inconsistently now decode per §2.4.
- README: the keys/terminal section describes the escape timeout, `MINITUI_INPUT=console` and Ctrl+Z.

### 2.11 Implementation order

1. `AnsiInputDecoder` and `IInputSink`, with the table, round-trip, burst, timeout, unknown-sequence and
   allocation tests. Pure code, so it's built and tested first.
2. `UnixTerminal` P/Invokes and the pty test. Verify that `[LibraryImport("libc")]` resolves on glibc and macOS
   CI; if it doesn't, add a `NativeLibrary` resolver mapping `libc` to `libc.so.6` / `libSystem.dylib`.
3. `TerminalInputSource`, `IInputLifecycle` and `IWaitableInputSource`, and the `InputPump` changes.
4. The restore hook in `TerminalSession`, the mode re-check, and the default-source switch with
   `MINITUI_INPUT`.
5. Input demo, README, CHANGELOG, manual QA.

### 2.12 Out of scope

- Windows `ReadConsoleInput` (Phase 10).
- Enabling bracketed paste or mouse reporting; the decoder only drops their sequences (Phases 9 and 10).
- The kitty keyboard protocol and xterm `modifyOtherKeys`, which would distinguish Ctrl+I from Tab or report
  key releases. They could be added later behind an opt-in, since `ConsoleKeyInfo` can't carry most of what
  they report.
- rxvt-specific modifier forms (`CSI a`, `CSI 2$`), which are dropped as unknown.
- Suspend on Ctrl+Z (Phase 11).
- Terminal queries (cursor position, colors, capabilities).

### 2.13 Open questions (defaults apply if unanswered)

| # | Question | Default |
|---|---|---|
| 1 | Move Windows to `ReadConsoleInput` in this phase, as SPEC.md §11 originally listed, or with mouse in Phase 10? Windows keys already work through `Console.ReadKey`, and I can't test Windows from WSL. | **Phase 10**, together with Windows mouse input. |
| 2 | Escape timeout default? Lower makes Esc snappier; higher protects slow links from split sequences. | **50 ms.** |
| 3 | Until Phase 11, Ctrl+Z is a key that does nothing. Alternative: keep `ISIG` and let Ctrl+Z suspend without restoring the terminal. | **Clear `ISIG`** (no broken shell). |
| 4 | Ship `MINITUI_INPUT=console` as an escape hatch? | **Yes.** |
| 5 | Release cadence: one minor version per group of phases, as in §1, or one per phase? | **Per group** (§1). |

---

## 3. Bracketed paste (Phase 9)

### 3.1 Purpose

In 1.0 and Phase 8, pasted text arrives as one key per character. That's slow for a large paste (each key is
routed and can redraw), and it's wrong: a pasted line break is Enter, which submits a form or runs a command
halfway through the paste, and a pasted `q` or `?` can fire a global binding when no text field has focus.

Terminals solve this with bracketed paste (`CSI ?2004h`): while it's on, the terminal wraps a paste in
`CSI 200~` … `CSI 201~`. Phase 9 turns it on wherever Phase 8's raw input is active, decodes the markers, and
delivers the paste as one event. Text fields insert it in one step.

**Decision:** a paste nobody handles is dropped, never replayed as keys (§3.4). Pasted text is never run as
commands, as in Textual, Bubble Tea and the shells. A component that edits text with `TextFieldState.HandleKey`
needs one line to accept pastes: `public override bool HandlePaste(string text) => State.Paste(text);`.

### 3.2 Public API

```csharp
/// A paste from the terminal. Line breaks are normalized to "\n".
public sealed record PasteEvent(string Text) : Event;

public abstract class Component
{
    /// Handle pasted text. Return true if handled; otherwise the paste goes on up the focus chain (§3.4).
    public virtual bool HandlePaste(string text) => false;
}

public sealed class App
{
    /// Called for each paste before the focus chain sees it; return true to consume it. Like OnKey.
    public App OnPaste(Func<string, bool> handler);
}

public class TextFieldState
{
    /// Insert pasted text at the cursor: leading and trailing line breaks are dropped, each other line break
    /// and each tab becomes a space, and other control characters are removed. Always returns true, so a
    /// component can write `HandlePaste(t) => State.Paste(t)`.
    public bool Paste(string text);
}

public sealed class ScriptedInputSource
{
    /// Queue a paste, in order with the queued keys.
    public ScriptedInputSource EnqueuePaste(string text);
}

public sealed class ComponentHarness
{
    /// Paste text, then RunUntilIdle.
    public void Paste(string text);
}
```

- `CommandLineOverlay` handles pastes with `CommandLineState.Paste` (inherited from `TextFieldState`).
- `IInputSource` doesn't change. A custom source can't produce pastes; only `TerminalInputSource` (on raw-mode
  terminals) and `ScriptedInputSource` do. A public way for custom sources to produce events can come with
  mouse events in Phase 10, if needed.

### 3.3 Internal model

```csharp
// What the input pump carries: a key or a paste, in arrival order. Phase 10 adds mouse.
internal readonly struct InputItem
{
    public static InputItem FromKey(ConsoleKeyInfo key);
    public static InputItem FromPaste(string text);
    public ConsoleKeyInfo Key { get; }
    public string? Paste { get; }        // null for a key
}

internal interface IInputSink
{
    void Key(ConsoleKeyInfo key);
    void Paste(string text);             // new
}

// Sources that can produce more than keys. TerminalInputSource and ScriptedInputSource implement it.
internal interface IInputItemSource
{
    bool TryReadItem(out InputItem item);
}
```

- `InputPump` reads `InputItem`s (from `IInputItemSource.TryReadItem` when the source implements it, otherwise
  wrapping `IInputSource.TryRead` keys), and its batches, orphans and the nested-app handoff in `AppLoop`
  (`RemoveQueuedKeys`/`ReturnKeys`) carry `InputItem[]` instead of `ConsoleKeyInfo[]`. A paste handed back to
  an outer app stays a paste.
- `IInputSource.TryRead(out ConsoleKeyInfo)` on `TerminalInputSource` and `ScriptedInputSource` still works for
  callers outside the pump: it returns the next key and skips any pastes ahead of it.

### 3.4 Routing

A paste goes through these steps, stopping at the first that handles it:

1. `App.OnEvent` sees `PasteEvent` (it observes; it can't consume, as for keys).
2. `App.OnPaste`, if set.
3. The focus chain, innermost first: `Component.HandlePaste`. With an overlay open, only the overlay's chain,
   as for keys. Key maps (`Component.Keys`, `App.Keys`) aren't consulted: a paste is never a binding.
4. Nobody handled it: the paste is dropped. With no text field focused, a paste does nothing.

### 3.5 Decoding

- On `CSI 200~` the decoder switches to paste mode and collects bytes until `CSI 201~`, decoding nothing in
  between: Esc, control bytes and other sequences inside a paste are text. The end marker may be split across
  reads at any byte, like any sequence.
- At the end marker the bytes are decoded as UTF-8 (invalid bytes become U+FFFD), `\r\n` and lone `\r` become
  `\n`, and the result goes to `IInputSink.Paste`. An empty paste (`CSI 200~ CSI 201~`) is dropped.
- The escape timeout doesn't apply in paste mode: a paste over a slow link can pause mid-way.
- **No size limit:** one paste is always one `PasteEvent`, however large, as in crossterm, Bubble Tea and
  Textual. A terminal that never sends `201~` leaves the decoder in paste mode until the app exits; terminals
  generate both markers themselves, so this isn't guarded against.
- A stray `CSI 201~` outside a paste is dropped, as in Phase 8. A `CSI 200~` inside a paste is text.
- The paste buffer is reused between pastes and grows as needed; the only per-paste allocation is the
  delivered string (and the buffer's growth for a paste larger than any before it).

### 3.6 Terminal mode

- `UnixTerminal.TryEnterRaw` writes `CSI ?2004h` to stdout after entering raw mode, and `Restore` writes
  `CSI ?2004l` before restoring the mode, both only when stdout is a terminal (`isatty(1)`). So bracketed
  paste is on exactly while raw input is, and every Phase 8 restore path (Deactivate, signals, exit, unhandled
  exception) turns it off.
- The write is a direct `write(1, …)`, retried on `EINTR`. `TryEnterRaw` runs on the app thread before the
  first frame, so it can't interleave with frame output.
- **Decision:** bracketed paste is never enabled without raw input (Windows until Phase 10,
  `MINITUI_INPUT=console`, a custom source): `Console.ReadKey` would deliver the markers as a stray Esc and
  `[200~`. Pastes there stay keys, as in 1.0.
- A child process started while the app runs inherits bracketed paste mode. `true` and most programs don't
  care; Phase 11's `app.Suspend` restores it fully for programs like `$EDITOR`.

### 3.7 Configuration

| Setting | Effect |
|---|---|
| `MINITUI_INPUT=console` | Also turns bracketed paste off (no raw input). |

No new settings.

### 3.8 Example

- The Form screen's text fields (`TextInput` in `Controls.cs`) handle pastes with `State.Paste`.
- The Input demo lists pastes as `Paste  N chars, M lines: <first 40 chars>`, so a terminal's paste support can
  be checked.

### 3.9 Tests and acceptance

Automated (all platforms unless noted):
- **Decoder:** a paste with plain text, UTF-8, Esc, control bytes, `CSI A` and `CSI 200~` inside it decodes as
  one `Paste` with that text; `\r\n` and `\r` become `\n`; the start and end markers split at every byte offset;
  keys before and after a paste stay in order; an empty paste produces nothing; a stray `201~` is dropped; a
  10 MiB paste arrives as one paste with the same text; a paste held across a pause longer than the escape
  timeout isn't resolved early; invalid UTF-8 becomes U+FFFD.
- **Routing** (`ComponentHarness`): a focused component's `HandlePaste` gets the text; `OnPaste` returning true
  stops it; an overlay's chain gets it while open; `PasteEvent` reaches `OnEvent`; a paste is never matched
  against key maps; with no handler, a paste of `"q?\n"` fires nothing and changes nothing.
- **TextFieldState.Paste:** inserts at the cursor; `"\nab\r\ncd\tef\n"` inserts `"ab cd ef"`; other controls
  are removed; the cursor ends after the text on a cluster boundary.
- **Pump:** `ScriptedInputSource` pastes and keys arrive in order; a paste queued for a nested app that exits
  goes back to the outer app as a paste.
- **Pseudo-terminal** (Linux and macOS CI): entering raw mode writes `CSI ?2004h` to a terminal stdout and
  restoring writes `CSI ?2004l`. The pty test points stdout at the pty leader via an internal fd parameter, as
  Phase 8's tests do for stdin.
- `CommandLineOverlay` inserts a paste instead of running a command at its line break.

Manual QA (results in the PR), in WSL in Windows Terminal, tmux on Linux and SSH to Linux:
1. Pasting 1,000 characters with line breaks into a Form field inserts them in one step, as one line, without
   submitting.
2. The same paste on the Counter screen (no text field) does nothing.
3. The Input demo shows one `Paste` entry with the right counts.
4. After exit, by every Phase 8 restore path, pasting into the shell isn't wrapped in `^[[200~` (bracketed
   paste is off again, unless the shell itself turns it on).
5. In tmux, pasting with `prefix ]` produces one paste (tmux brackets when the app asked for it).
6. Windows Terminal and conhost: pastes behave as in 1.0.

### 3.10 Migration impact

- New public API (§3.2) in `PublicAPI.Unshipped.txt`. Nothing changes incompatibly: `HandlePaste` is a new
  virtual with a default, and `IInputSource` is unchanged.
- **Behavior changes on Linux and macOS** (CHANGELOG "Changed"): pastes no longer arrive as keys. A component
  that edits text needs `HandlePaste` to accept them; without it, a paste is dropped. On Windows and with
  `MINITUI_INPUT=console`, pastes still arrive as keys until Phase 10.
- README: a "Paste" paragraph under "Keyboard input" with `HandlePaste`, `TextFieldState.Paste` and the drop
  rule.

### 3.11 Implementation order

1. `IInputSink.Paste` and paste mode in `AnsiInputDecoder`, with the decoder tests.
2. `InputItem` and `IInputItemSource`, and carrying items through `InputPump` and `AppLoop`
   (including the nested-app handoff), with `ScriptedInputSource.EnqueuePaste`. Existing tests must pass
   unchanged at this step.
3. `PasteEvent`, `OnPaste`, `Component.HandlePaste`, routing, `ComponentHarness.Paste`, with the
   routing tests.
4. `TextFieldState.Paste`, `CommandLineOverlay`.
5. `CSI ?2004h`/`l` in `UnixTerminal`, with the pty test.
6. Example, README, CHANGELOG, manual QA.

### 3.12 Out of scope

- Windows paste events (Phase 10 decides, with `ReadConsoleInput`).
- Multi-line paste into a text area (Phase 12 builds on `HandlePaste`).
- A public way for custom input sources to produce pastes.
- Filtering pasted escape sequences ("paste injection" through a pasted `CSI 201~`): the terminal is
  responsible, and most strip the markers from pasted text. Text after an early end marker decodes as keys, as
  in every terminal app.
- Clipboard access (OSC 52) or a copy command.

### 3.13 Decisions taken

| # | Question | Decision |
|---|---|---|
| 1 | A paste nobody handles: replay it as keys (1.0 behavior), or drop it? | **Drop** (§3.1), as Textual and crossterm apps do. |
| 2 | Normalize line breaks in `PasteEvent.Text` to `\n`, or deliver them as the terminal sent them (usually `\r`)? | **Normalize**, as prompt_toolkit does. |
| 3 | A single-line field given pasted line breaks: spaces, first line only, or reject? | **Spaces**, as Bubbles' `textinput` does, after dropping leading and trailing line breaks. |
| 4 | A size limit for very large pastes? | **None**, as in crossterm, Bubble Tea and Textual. |
