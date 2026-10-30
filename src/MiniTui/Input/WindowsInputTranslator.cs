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

    public static InputRecord Resize() => new() { EventType = WindowBufferSizeEvent };
}

/// <summary>
/// Turns console input records read in VT input mode into the bytes <see cref="AnsiInputDecoder"/> decodes
/// (1.x spec §4.4). In that mode the console has already encoded every key as text or an escape sequence, one
/// UTF-16 character per key-down record. Pure, so it's tested on every platform.
/// </summary>
internal sealed class WindowsInputTranslator
{
    private const ushort VkSpace = 0x20;
    private const ushort Vk2 = 0x32;
    private const uint LeftCtrlPressed = 0x8;
    private const uint RightCtrlPressed = 0x4;

    private byte[] _bytes = new byte[1024];
    private int _count;
    private char _highSurrogate;

    /// <summary>
    /// Translate <paramref name="records"/>. Key-ups and key-downs without a character (a bare Shift, Ctrl or Alt)
    /// are skipped; a surrogate pair split across records is joined.
    /// </summary>
    /// <returns>The UTF-8 bytes, valid until the next call.</returns>
    public ReadOnlySpan<byte> Translate(ReadOnlySpan<InputRecord> records, out bool resized)
    {
        _count = 0;
        resized = false;
        foreach (ref readonly var record in records)
        {
            switch (record.EventType)
            {
                case InputRecord.KeyEvent when record.KeyDown != 0:
                    if (CharacterOf(record) is not { } c) break;
                    for (var i = 0; i < Math.Max((int)record.RepeatCount, 1); i++) Append(c);
                    break;
                case InputRecord.WindowBufferSizeEvent:
                    resized = true;
                    break;
            }
        }
        return _bytes.AsSpan(0, _count);
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
