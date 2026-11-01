using System.Runtime.InteropServices;
using System.Text;

namespace MiniTui;

/// <summary>
/// A Win32 <c>INPUT_RECORD</c> (20 bytes): the event type, then a 16-byte union of which only the key and mouse
/// fields MiniTui reads are declared.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
internal struct InputRecord
{
    public const ushort KeyEvent = 0x1;
    public const ushort MouseEvent = 0x2;
    public const ushort WindowBufferSizeEvent = 0x4;

    [FieldOffset(0)] public ushort EventType;

    // MOUSE_EVENT_RECORD
    [FieldOffset(4)] public short MouseX;
    [FieldOffset(6)] public short MouseY;
    [FieldOffset(8)] public uint ButtonState;
    [FieldOffset(12)] public uint MouseControlKeyState;
    [FieldOffset(16)] public uint EventFlags;

    // KEY_EVENT_RECORD
    [FieldOffset(4)] public int KeyDown;
    [FieldOffset(8)] public ushort RepeatCount;
    [FieldOffset(10)] public ushort VirtualKeyCode;
    [FieldOffset(12)] public ushort VirtualScanCode;
    [FieldOffset(14)] public ushort UnicodeChar;
    [FieldOffset(16)] public uint KeyControlKeyState;

    public static InputRecord Key(char c, bool down = true, ushort repeat = 1, ushort virtualKey = 0, uint controlKeyState = 0) => new()
    {
        EventType = KeyEvent,
        KeyDown = down ? 1 : 0,
        RepeatCount = repeat,
        VirtualKeyCode = virtualKey,
        UnicodeChar = c,
        KeyControlKeyState = controlKeyState,
    };

    public static InputRecord Mouse(int x, int y, uint buttons, uint flags = 0, uint controlKeyState = 0) => new()
    {
        EventType = MouseEvent,
        MouseX = (short)x,
        MouseY = (short)y,
        ButtonState = buttons,
        EventFlags = flags,
        MouseControlKeyState = controlKeyState,
    };

    public static InputRecord Resize() => new() { EventType = WindowBufferSizeEvent };
}

/// <summary>
/// Turns console input records read in VT input mode into the bytes <see cref="AnsiInputDecoder"/> decodes
/// (1.x spec §4.4), and mouse records into <see cref="MouseEvent"/>s (§4.6). In VT input mode the console has
/// already encoded every key as text or an escape sequence, one UTF-16 character per key-down record. Pure, so
/// it's tested on every platform.
/// </summary>
/// <param name="windowOrigin">The window's top-left cell in buffer coordinates; read once per batch with mouse records.</param>
internal sealed class WindowsInputTranslator(Func<Vec2> windowOrigin)
{
    private const uint LeftButton = 0x1;
    private const uint RightButton = 0x2;
    private const uint MiddleButton = 0x4;
    private const uint MouseMoved = 0x1;
    private const uint MouseWheeled = 0x4;
    private const uint MouseHWheeled = 0x8;
    private const uint ShiftPressed = 0x10;
    private const uint LeftAltPressed = 0x2;
    private const uint RightAltPressed = 0x1;

    private const ushort VkSpace = 0x20;
    private const ushort Vk2 = 0x32;
    private const uint LeftCtrlPressed = 0x8;
    private const uint RightCtrlPressed = 0x4;

    private byte[] _bytes = new byte[1024];
    private int _count;
    private char _highSurrogate;
    private uint _buttons;

    /// <summary>
    /// Translate <paramref name="records"/>, which arrived at <paramref name="now"/>: key characters go through
    /// <paramref name="decoder"/>, mouse events straight to <paramref name="sink"/>, in order. Key-ups and
    /// key-downs without a character (a bare Shift, Ctrl or Alt) are skipped; a surrogate pair split across
    /// records is joined.
    /// </summary>
    public void Translate(ReadOnlySpan<InputRecord> records, AnsiInputDecoder decoder, TimeSpan now, IInputSink sink, out bool resized)
    {
        _count = 0;
        resized = false;
        Vec2? origin = null;
        foreach (ref readonly var record in records)
        {
            switch (record.EventType)
            {
                case InputRecord.KeyEvent when record.KeyDown != 0:
                    if (CharacterOf(record) is not { } c) break;
                    for (var i = 0; i < Math.Max((int)record.RepeatCount, 1); i++) Append(c);
                    break;
                case InputRecord.MouseEvent:
                    Flush(decoder, now, sink);
                    origin ??= windowOrigin();
                    Mouse(record, origin.Value, sink);
                    break;
                case InputRecord.WindowBufferSizeEvent:
                    resized = true;
                    break;
            }
        }
        Flush(decoder, now, sink);
    }

    private void Flush(AnsiInputDecoder decoder, TimeSpan now, IInputSink sink)
    {
        if (_count == 0) return;
        decoder.Feed(_bytes.AsSpan(0, _count), now, sink);
        _count = 0;
    }

    // A press or release is a button bit that changed since the last record; a move with a button held is a drag
    private void Mouse(in InputRecord record, Vec2 origin, IInputSink sink)
    {
        var x = record.MouseX - origin.X;
        var y = record.MouseY - origin.Y;
        var modifiers = MouseModifiers(record.MouseControlKeyState);
        var wheel = (short)(record.ButtonState >> 16);
        if ((record.EventFlags & MouseWheeled) != 0)
        {
            sink.Mouse(new MouseEvent(wheel > 0 ? MouseAction.ScrollUp : MouseAction.ScrollDown, MouseButton.None, x, y, modifiers));
            return;
        }
        if ((record.EventFlags & MouseHWheeled) != 0)
        {
            sink.Mouse(new MouseEvent(wheel > 0 ? MouseAction.ScrollRight : MouseAction.ScrollLeft, MouseButton.None, x, y, modifiers));
            return;
        }
        var buttons = record.ButtonState & (LeftButton | RightButton | MiddleButton);
        if ((record.EventFlags & MouseMoved) != 0)
        {
            // 1.x spec §10.7: the lowest held button, and a drag past the window's edge stays at the edge
            var held = (buttons & LeftButton) != 0 ? MouseButton.Left
                : (buttons & MiddleButton) != 0 ? MouseButton.Middle
                : (buttons & RightButton) != 0 ? MouseButton.Right
                : MouseButton.None;
            if (held != MouseButton.None) sink.Mouse(new MouseEvent(MouseAction.Drag, held, Math.Max(0, x), Math.Max(0, y), modifiers));
            return;
        }

        var changed = buttons ^ _buttons;
        _buttons = buttons;
        if (x < 0 || y < 0) return;
        foreach (var (bit, button) in (ReadOnlySpan<(uint, MouseButton)>)[(LeftButton, MouseButton.Left), (MiddleButton, MouseButton.Middle), (RightButton, MouseButton.Right)])
        {
            if ((changed & bit) == 0) continue;
            var action = (buttons & bit) != 0 ? MouseAction.Press : MouseAction.Release;
            sink.Mouse(new MouseEvent(action, button, x, y, modifiers));
        }
    }

    private static ConsoleModifiers MouseModifiers(uint state)
    {
        ConsoleModifiers modifiers = 0;
        if ((state & ShiftPressed) != 0) modifiers |= ConsoleModifiers.Shift;
        if ((state & (LeftAltPressed | RightAltPressed)) != 0) modifiers |= ConsoleModifiers.Alt;
        if ((state & (LeftCtrlPressed | RightCtrlPressed)) != 0) modifiers |= ConsoleModifiers.Control;
        return modifiers;
    }

    // Ctrl+Space (and Ctrl+@) is NUL, which the record can't tell from "no character" except by its key
    private static char? CharacterOf(in InputRecord record)
    {
        if (record.UnicodeChar != 0) return (char)record.UnicodeChar;
        var ctrl = (record.KeyControlKeyState & (LeftCtrlPressed | RightCtrlPressed)) != 0;
        return ctrl && record.VirtualKeyCode is VkSpace or Vk2 ? '\0' : null;
    }

    private void Append(char c)
    {
        if (_highSurrogate != 0)
        {
            var high = _highSurrogate;
            _highSurrogate = '\0';
            if (char.IsLowSurrogate(c))
            {
                AppendRune(new Rune(high, c));
                return;
            }
            AppendRune(Rune.ReplacementChar);
        }

        if (char.IsHighSurrogate(c)) _highSurrogate = c;
        else AppendRune(char.IsLowSurrogate(c) ? Rune.ReplacementChar : new Rune(c));
    }

    private void AppendRune(Rune rune)
    {
        if (_count + 4 > _bytes.Length) Array.Resize(ref _bytes, _bytes.Length * 2);
        _count += rune.EncodeToUtf8(_bytes.AsSpan(_count));
    }
}
