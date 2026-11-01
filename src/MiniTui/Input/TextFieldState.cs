using System.Globalization;
using System.Text;

namespace MiniTui;

/// <summary>
/// The text and cursor of a single-line text field (§8.4). The cursor is a UTF-16 index that is always on a
/// grapheme cluster boundary, so editing never splits a character.
/// </summary>
public class TextFieldState
{
    private readonly EditHistory<Snapshot> _history = new();
    private string _text = "";
    private int _cursor;
    private int? _anchor;
    private char? _pendingHighSurrogate;

    /// <summary>Create a state with optional initial text. The cursor starts at the end.</summary>
    public TextFieldState(string text = "") => Text = text;

    /// <summary>The text. Setting it moves the cursor to the end, and clears the selection and the undo history.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            _cursor = _text.Length;
            _anchor = null;
            _pendingHighSurrogate = null;
            _history.Clear();
        }
    }

    /// <summary>
    /// The cursor as a UTF-16 index into <see cref="Text"/>. Set values snap to the next cluster boundary, and clear
    /// the selection.
    /// </summary>
    public int Cursor
    {
        get => _cursor;
        set => MoveTo(SnapToBoundary(_text, Math.Clamp(value, 0, _text.Length)), extend: false);
    }

    /// <summary>
    /// The other end of the selection, or null for none; <see cref="Cursor"/> is the end that moves (1.x spec §10).
    /// </summary>
    public int? SelectionAnchor => _anchor;

    /// <summary>The start of the selection (the lower of the anchor and the cursor), or the cursor without one.</summary>
    public int SelectionStart => Math.Min(_anchor ?? _cursor, _cursor);

    /// <summary>The end of the selection (the higher of the anchor and the cursor), or the cursor without one.</summary>
    public int SelectionEnd => Math.Max(_anchor ?? _cursor, _cursor);

    /// <summary>True if some text is selected.</summary>
    public bool HasSelection => _anchor is { } anchor && anchor != _cursor;

    /// <summary>The selected text, or <c>""</c>.</summary>
    public string SelectedText => _text[SelectionStart..SelectionEnd];

    /// <summary>True if <see cref="Undo"/> has something to take back.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>True if <see cref="Redo"/> has something to do again.</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>The first visible display column. Written by the widget while drawing.</summary>
    public int ScrollOffset { get; internal set; }

    /// <summary>The row the widget last drew the text in, for the mouse.</summary>
    internal Rect ViewArea { get; set; }

    // True between a left press in the text and its release, while drags extend the selection
    private bool _dragging;

    /// <summary>Clear the text.</summary>
    public void Clear() => Text = "";

    /// <summary>
    /// Select from <paramref name="anchor"/> to <paramref name="cursor"/>, both clamped and snapped as
    /// <see cref="Cursor"/> is. Equal values clear the selection.
    /// </summary>
    public void Select(int anchor, int cursor)
    {
        var a = SnapToBoundary(_text, Math.Clamp(anchor, 0, _text.Length));
        MoveTo(a, extend: false);
        _anchor = a;
        MoveTo(SnapToBoundary(_text, Math.Clamp(cursor, 0, _text.Length)), extend: true);
    }

    /// <summary>Select all the text, with the cursor at the end.</summary>
    public void SelectAll() => Select(0, _text.Length);

    /// <summary>Clear the selection, leaving the cursor where it is.</summary>
    public void ClearSelection() => MoveTo(_cursor, extend: false);

    /// <summary>Insert text at the cursor, replacing the selection, and move the cursor past it.</summary>
    public void Insert(string text) => InsertText(text, EditKind.Other);

    /// <summary>
    /// Insert pasted text at the cursor, replacing the selection. Leading and trailing line breaks are dropped, each
    /// other line break and each tab becomes a space, and other control characters are removed, so a multi-line paste
    /// becomes one line.
    /// </summary>
    /// <returns>Always true, so a component can write <c>HandlePaste(text) =&gt; State.Paste(text)</c>.</returns>
    public bool Paste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.AsSpan().Trim("\r\n");
        var line = new StringBuilder(trimmed.Length);
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '\r' && i + 1 < trimmed.Length && trimmed[i + 1] == '\n') continue;
            if (c is '\r' or '\n' or '\t') line.Append(' ');
            else if (!char.IsControl(c)) line.Append(c);
        }
        if (line.Length > 0) InsertText(line.ToString(), EditKind.Other);
        else DeleteSelection();
        return true;
    }

    /// <summary>Copy the selection to the <see cref="MiniTui.Clipboard"/>.</summary>
    /// <returns>False, doing nothing, without a selection.</returns>
    public bool Copy()
    {
        if (!HasSelection) return false;
        MiniTui.Clipboard.SetText(SelectedText);
        return true;
    }

    /// <summary>Copy the selection to the <see cref="MiniTui.Clipboard"/> and delete it.</summary>
    /// <returns>False, doing nothing, without a selection.</returns>
    public bool Cut()
    {
        if (!Copy()) return false;
        DeleteSelection();
        return true;
    }

    /// <summary>
    /// Take back the last edit group (1.x spec §10.6): typed characters undo a word at a time. The text, cursor and
    /// selection return to how they were before it.
    /// </summary>
    /// <returns>False when there's nothing to undo.</returns>
    public bool Undo()
    {
        if (!_history.Undo(Current(), out var snapshot)) return false;
        Restore(snapshot);
        return true;
    }

    /// <summary>Do again the last edit group undone.</summary>
    /// <returns>False when there's nothing to redo.</returns>
    public bool Redo()
    {
        if (!_history.Redo(Current(), out var snapshot)) return false;
        Restore(snapshot);
        return true;
    }

    /// <summary>
    /// A left press in the text moves the cursor there (Shift+press extends the selection to there), and a drag after
    /// it extends the selection, scrolling a column at a time past either edge (1.x spec §10.7). Uses the area from
    /// the last draw.
    /// </summary>
    /// <returns>True if the event did any of these.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        var area = ViewArea;
        switch (mouse.Action)
        {
            case MouseAction.Press when mouse.Button == MouseButton.Left:
                _dragging = area.Contains(mouse.X, mouse.Y);
                if (!_dragging) return false;
                MoveTo(IndexAtColumn(ScrollOffset + mouse.X - area.X), (mouse.Modifiers & ConsoleModifiers.Shift) != 0);
                return true;
            case MouseAction.Drag when mouse.Button == MouseButton.Left && _dragging:
                var column = mouse.X < area.X ? ScrollOffset - 1
                    : mouse.X >= area.Right ? ScrollOffset + area.Width
                    : ScrollOffset + mouse.X - area.X;
                MoveTo(IndexAtColumn(column), extend: true);
                return true;
            case MouseAction.Release:
                _dragging = false;
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// Handle an editing key (§8.4 and 1.x spec §10.3): text, ← → Home End, Ctrl+← →, Shift with those to select,
    /// Backspace, Delete, Ctrl+Backspace / Ctrl+W (delete word), Ctrl+U (delete to start), Ctrl+K (delete to end),
    /// Ctrl+A (select all), Ctrl+C / Ctrl+X (copy and cut, only with a selection), Ctrl+V (paste the clipboard),
    /// Ctrl+Z (undo) and Ctrl+Y (redo).
    /// </summary>
    /// <returns>True if the key was an editing key.</returns>
    public virtual bool HandleKey(ConsoleKeyInfo key)
    {
        var ctrl = KeyInput.HasControl(key);
        var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                if (HasSelection && !shift && !ctrl) return MoveTo(SelectionStart, extend: false);
                return MoveTo(ctrl ? PreviousWord(_cursor) : PreviousBoundary(_text, _cursor), shift);
            case ConsoleKey.RightArrow:
                if (HasSelection && !shift && !ctrl) return MoveTo(SelectionEnd, extend: false);
                return MoveTo(ctrl ? NextWord(_cursor) : NextBoundary(_text, _cursor), shift);
            case ConsoleKey.Home:
                return MoveTo(0, shift);
            case ConsoleKey.End:
                return MoveTo(_text.Length, shift);
            case ConsoleKey.Backspace:
                // Unix terminals send Ctrl+Backspace as ^H (Backspace + Control); Windows reports the modifier directly
                if (DeleteSelection()) return true;
                if (ctrl) DeleteRange(PreviousWord(_cursor), _cursor, EditKind.Other);
                else DeleteRange(PreviousBoundary(_text, _cursor), _cursor, EditKind.DeleteBackward);
                return true;
            case ConsoleKey.Delete:
                if (DeleteSelection()) return true;
                DeleteRange(_cursor, NextBoundary(_text, _cursor), EditKind.DeleteForward);
                return true;
        }

        if (KeyInput.IsCtrl(key, 'W'))
        {
            if (!DeleteSelection()) DeleteRange(PreviousWord(_cursor), _cursor, EditKind.Other);
            return true;
        }
        if (KeyInput.IsCtrl(key, 'U'))
        {
            if (!DeleteSelection()) DeleteRange(0, _cursor, EditKind.Other);
            return true;
        }
        if (KeyInput.IsCtrl(key, 'K'))
        {
            if (!DeleteSelection()) DeleteRange(_cursor, _text.Length, EditKind.Other);
            return true;
        }
        if (KeyInput.IsCtrl(key, 'A'))
        {
            SelectAll();
            return true;
        }
        if (KeyInput.IsCtrlC(key)) return Copy();
        if (KeyInput.IsCtrl(key, 'X')) return Cut();
        if (KeyInput.IsCtrl(key, 'V')) return Paste(MiniTui.Clipboard.GetText() ?? "");
        if (KeyInput.IsCtrlZ(key))
        {
            Undo();
            return true;
        }
        if (KeyInput.IsCtrl(key, 'Y'))
        {
            Redo();
            return true;
        }

        return TypeChar(key);
    }

    private bool TypeChar(ConsoleKeyInfo key)
    {
        var c = key.KeyChar;
        if (c == '\0' || char.IsControl(c) || KeyInput.HasCommandModifier(key)) return false;

        if (char.IsHighSurrogate(c))
        {
            _pendingHighSurrogate = c;
            return true;
        }

        if (char.IsLowSurrogate(c))
        {
            var high = _pendingHighSurrogate;
            _pendingHighSurrogate = null;
            if (high is { } h) InsertText(new string([h, c]), EditKind.Type);
            return true;
        }

        _pendingHighSurrogate = null;
        InsertText(c.ToString(), EditKind.Type, char.IsWhiteSpace(c));
        return true;
    }

    // Move the cursor; Shift extends the selection from where the cursor was, anything else clears it
    private bool MoveTo(int index, bool extend)
    {
        if (extend) _anchor ??= _cursor;
        else _anchor = null;
        _cursor = index;
        if (_anchor == _cursor) _anchor = null;
        _pendingHighSurrogate = null;
        _history.Break();
        return true;
    }

    private void InsertText(string text, EditKind kind, bool typedSpace = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Replacing a selection is a group of its own, which the typing after it joins
        if (HasSelection) _history.Break();
        _history.Record(kind, Current, typedSpace);
        RemoveSelection();
        _text = _text.Insert(_cursor, text);
        _cursor = SnapToBoundary(_text, _cursor + text.Length);
    }

    private bool DeleteSelection()
    {
        if (!HasSelection) return false;
        _history.Record(EditKind.Other, Current);
        RemoveSelection();
        return true;
    }

    private void RemoveSelection()
    {
        if (!HasSelection)
        {
            _anchor = null;
            return;
        }
        var start = SelectionStart;
        _text = _text.Remove(start, SelectionEnd - start);
        _cursor = start;
        _anchor = null;
    }

    private void DeleteRange(int start, int end, EditKind kind)
    {
        if (end <= start) return;
        _history.Record(kind, Current);
        _text = _text.Remove(start, end - start);
        _cursor = SnapToBoundary(_text, start);
        _anchor = null;
    }

    // The start of the cluster at a display column; the end of the text past it
    private int IndexAtColumn(int column)
    {
        if (column <= 0) return 0;
        var x = 0;
        for (var i = 0; i < _text.Length;)
        {
            var next = NextBoundary(_text, i);
            var width = UnicodeWidth.Of(Sanitizer.SingleLine(_text.AsSpan(i, next - i)));
            if (x + width > column) return i;
            x += width;
            i = next;
        }
        return _text.Length;
    }

    private Snapshot Current() => new(_text, _cursor, _anchor);

    private void Restore(Snapshot snapshot)
    {
        (_text, _cursor, _anchor) = snapshot;
        _pendingHighSurrogate = null;
    }

    private readonly record struct Snapshot(string Text, int Cursor, int? Anchor);

    private int PreviousWord(int index)
    {
        while (index > 0 && char.IsWhiteSpace(_text[index - 1])) index = PreviousBoundary(_text, index);
        while (index > 0 && !char.IsWhiteSpace(_text[index - 1])) index = PreviousBoundary(_text, index);
        return index;
    }

    private int NextWord(int index)
    {
        while (index < _text.Length && char.IsWhiteSpace(_text[index])) index = NextBoundary(_text, index);
        while (index < _text.Length && !char.IsWhiteSpace(_text[index])) index = NextBoundary(_text, index);
        return index;
    }

    internal static int NextBoundary(string text, int index) =>
        index >= text.Length ? text.Length : index + StringInfo.GetNextTextElementLength(text, index);

    internal static int PreviousBoundary(string text, int index)
    {
        var previous = 0;
        for (var i = 0; i < index; i = NextBoundary(text, i)) previous = i;
        return Math.Min(previous, index);
    }

    /// <summary>The first cluster boundary at or after <paramref name="index"/>.</summary>
    internal static int SnapToBoundary(string text, int index)
    {
        var i = 0;
        while (i < index) i = NextBoundary(text, i);
        return i;
    }
}
