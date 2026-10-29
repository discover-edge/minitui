using System.Globalization;

namespace MiniTui;

/// <summary>
/// The text and cursor of a single-line text field (§8.4). The cursor is a UTF-16 index that is always on a
/// grapheme cluster boundary, so editing never splits a character.
/// </summary>
public class TextFieldState
{
    private string _text = "";
    private int _cursor;
    private char? _pendingHighSurrogate;

    /// <summary>Create a state with optional initial text. The cursor starts at the end.</summary>
    public TextFieldState(string text = "") => Text = text;

    /// <summary>The text. Setting it moves the cursor to the end.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            _cursor = _text.Length;
            _pendingHighSurrogate = null;
        }
    }

    /// <summary>The cursor as a UTF-16 index into <see cref="Text"/>. Set values snap to the next cluster boundary.</summary>
    public int Cursor
    {
        get => _cursor;
        set => _cursor = SnapToBoundary(_text, Math.Clamp(value, 0, _text.Length));
    }

    /// <summary>The first visible display column. Written by the widget while drawing.</summary>
    public int ScrollOffset { get; internal set; }

    /// <summary>Clear the text.</summary>
    public void Clear() => Text = "";

    /// <summary>Insert text at the cursor and move the cursor past it.</summary>
    public void Insert(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _text = _text.Insert(_cursor, text);
        _cursor = SnapToBoundary(_text, _cursor + text.Length);
    }

    /// <summary>
    /// Handle an editing key: text, ← → Home End, Ctrl+← →, Backspace, Delete,
    /// Ctrl+Backspace / Ctrl+W (delete word), Ctrl+U (delete to start) and Ctrl+K (delete to end).
    /// </summary>
    /// <returns>True if the key was an editing key.</returns>
    public virtual bool HandleKey(ConsoleKeyInfo key)
    {
        var ctrl = KeyInput.HasControl(key);
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _cursor = ctrl ? PreviousWord(_cursor) : PreviousBoundary(_text, _cursor);
                return true;
            case ConsoleKey.RightArrow:
                _cursor = ctrl ? NextWord(_cursor) : NextBoundary(_text, _cursor);
                return true;
            case ConsoleKey.Home:
                _cursor = 0;
                return true;
            case ConsoleKey.End:
                _cursor = _text.Length;
                return true;
            case ConsoleKey.Backspace:
                // Unix terminals send Ctrl+Backspace as ^H (Backspace + Control); Windows reports the modifier directly
                if (ctrl) DeleteRange(PreviousWord(_cursor), _cursor);
                else DeleteRange(PreviousBoundary(_text, _cursor), _cursor);
                return true;
            case ConsoleKey.Delete:
                DeleteRange(_cursor, NextBoundary(_text, _cursor));
                return true;
        }

        if (KeyInput.IsCtrl(key, 'W'))
        {
            DeleteRange(PreviousWord(_cursor), _cursor);
            return true;
        }
        if (KeyInput.IsCtrl(key, 'U'))
        {
            DeleteRange(0, _cursor);
            return true;
        }
        if (KeyInput.IsCtrl(key, 'K'))
        {
            DeleteRange(_cursor, _text.Length);
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
            if (high is { } h) Insert(new string([h, c]));
            return true;
        }

        _pendingHighSurrogate = null;
        Insert(c.ToString());
        return true;
    }

    private void DeleteRange(int start, int end)
    {
        if (end <= start) return;
        _text = _text.Remove(start, end - start);
        _cursor = SnapToBoundary(_text, start);
    }

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
