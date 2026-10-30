using System.Buffers;
using System.Text;

namespace MiniTui;

/// <summary>What <see cref="AnsiInputDecoder"/> produces (1.x spec §2.3). Later phases add paste and mouse.</summary>
internal interface IInputSink
{
    void Key(ConsoleKeyInfo key);
}

/// <summary>
/// Decodes terminal input bytes into keys (1.x spec §2.4). Keeps state across reads, so a sequence split
/// between two reads is reassembled instead of decoding as Esc plus characters. Pure: no I/O and no clock.
/// </summary>
internal sealed class AnsiInputDecoder
{
    private const byte Esc = 0x1B;
    private const int MaxCsiLength = 64;
    private const int MaxStringLength = 4096;

    // A held prefix is at most an unterminated OSC/DCS string, so this never grows
    private readonly byte[] _pending = new byte[MaxStringLength * 2];
    private readonly TimeSpan _escapeTimeout;
    private int _pendingCount;
    private TimeSpan _lastByte;

    public AnsiInputDecoder(TimeSpan escapeTimeout) => _escapeTimeout = escapeTimeout;

    /// <summary>When held bytes must be resolved, or null if none are held.</summary>
    public TimeSpan? Deadline => _pendingCount > 0 ? _lastByte + _escapeTimeout : null;

    /// <summary>Decode <paramref name="bytes"/>, which arrived at <paramref name="now"/>.</summary>
    public void Feed(ReadOnlySpan<byte> bytes, TimeSpan now, IInputSink sink)
    {
        if (bytes.IsEmpty) return;
        _lastByte = now;

        while (!bytes.IsEmpty)
        {
            if (_pendingCount == 0)
            {
                // Common case: decode straight from the caller's span and hold only the incomplete tail
                var consumed = DecodeAll(bytes, final: false, sink);
                Hold(bytes[consumed..]);
                break;
            }

            var chunk = bytes[..Math.Min(bytes.Length, _pending.Length - _pendingCount)];
            chunk.CopyTo(_pending.AsSpan(_pendingCount));
            _pendingCount += chunk.Length;
            bytes = bytes[chunk.Length..];

            var used = DecodeAll(_pending.AsSpan(0, _pendingCount), final: false, sink);
            Shift(used);
        }

        if (_escapeTimeout <= TimeSpan.Zero) Resolve(sink);
    }

    /// <summary>Resolve held bytes as typed if <paramref name="now"/> is at or past <see cref="Deadline"/>.</summary>
    public void Expire(TimeSpan now, IInputSink sink)
    {
        if (Deadline is { } deadline && now >= deadline) Resolve(sink);
    }

    private void Resolve(IInputSink sink)
    {
        DecodeAll(_pending.AsSpan(0, _pendingCount), final: true, sink);
        _pendingCount = 0;
    }

    private void Hold(ReadOnlySpan<byte> tail)
    {
        tail.CopyTo(_pending);
        _pendingCount = tail.Length;
    }

    private void Shift(int used)
    {
        _pending.AsSpan(used, _pendingCount - used).CopyTo(_pending);
        _pendingCount -= used;
    }

    /// <summary>Decode as much as possible. When <paramref name="final"/> is set, everything is consumed.</summary>
    /// <returns>The number of bytes consumed.</returns>
    private static int DecodeAll(ReadOnlySpan<byte> s, bool final, IInputSink sink)
    {
        var offset = 0;
        while (offset < s.Length)
        {
            var used = Decode(s[offset..], final, sink);
            if (used == 0) break;
            offset += used;
        }
        return offset;
    }

    /// <summary>Decode one key or sequence from the start of <paramref name="s"/>.</summary>
    /// <returns>Bytes consumed, or 0 if more input is needed.</returns>
    private static int Decode(ReadOnlySpan<byte> s, bool final, IInputSink sink)
    {
        if (s[0] != Esc) return DecodePlain(s, final, alt: false, sink);

        if (s.Length == 1)
        {
            if (!final) return 0;
            sink.Key(Named(ConsoleKey.Escape, '\x1b', 0));
            return 1;
        }

        // On timeout, ESC plus one byte is that byte with Alt (ESC [ is Alt+[). ESC ESC is two Escapes.
        if (final && s.Length == 2 && s[1] != Esc) return 1 + DecodePlain(s[1..], final, alt: true, sink);

        switch (s[1])
        {
            case (byte)'[':
            case (byte)'O':
                return DecodeSequenceOrEscape(s, 0, final, alt: false, sink);
            case Esc:
                // ESC ESC [ / ESC ESC O: the sequence with Alt added
                if (s.Length >= 3 && s[2] is (byte)'[' or (byte)'O')
                    return DecodeSequenceOrEscape(s, 1, final, alt: true, sink);
                if (s.Length == 2 && !final) return 0;
                sink.Key(Named(ConsoleKey.Escape, '\x1b', 0));
                return 1;
            case (byte)']':
            case (byte)'P':
                return DecodeString(s, final, sink);
            default:
                var used = DecodePlain(s[1..], final, alt: true, sink);
                return used == 0 ? 0 : used + 1;
        }
    }

    /// <summary>
    /// Decode a CSI or SS3 sequence whose ESC is at <paramref name="start"/>. If it's incomplete at a timeout,
    /// emit Escape and let the rest decode as plain input.
    /// </summary>
    private static int DecodeSequenceOrEscape(ReadOnlySpan<byte> s, int start, bool final, bool alt, IInputSink sink)
    {
        var body = s[start..];
        var used = body[1] == (byte)'[' ? DecodeCsi(body, alt, sink) : DecodeSs3(body, alt, sink);
        if (used > 0) return start + used;
        if (!final) return 0;
        sink.Key(Named(ConsoleKey.Escape, '\x1b', 0));
        return 1;
    }

    private static int DecodeSs3(ReadOnlySpan<byte> s, bool alt, IInputSink sink)
    {
        if (s.Length < 3) return 0;
        if (FinalKey(s[2]) is { } key) sink.Key(Named(key, '\0', alt ? ConsoleModifiers.Alt : 0));
        return 3;
    }

    private static int DecodeCsi(ReadOnlySpan<byte> s, bool alt, IInputSink sink)
    {
        var i = 2;
        var paramStart = i;
        while (i < s.Length && s[i] is >= 0x30 and <= 0x3F) i++;
        var paramEnd = i;
        while (i < s.Length && s[i] is >= 0x20 and <= 0x2F) i++;

        if (i >= s.Length)
        {
            // Too long to be a key: drop it and resume after it
            return i >= MaxCsiLength ? i : 0;
        }
        if (s[i] is < 0x40 or > 0x7E) return i; // malformed: drop what we have, resume at this byte

        var finalByte = s[i];
        var length = i + 1;
        var parameters = s[paramStart..paramEnd];
        var hasIntermediates = i > paramEnd;

        // Linux console F1–F5: CSI [ A–E
        if (finalByte == (byte)'[' && parameters.IsEmpty && !hasIntermediates)
        {
            if (s.Length <= length) return 0;
            if (s[length] is >= (byte)'A' and <= (byte)'E')
                sink.Key(Named(ConsoleKey.F1 + (s[length] - 'A'), '\0', alt ? ConsoleModifiers.Alt : 0));
            return length + 1;
        }

        // Private (mouse, replies) and intermediate forms aren't keys
        if (hasIntermediates || (!parameters.IsEmpty && parameters[0] is (byte)'<' or (byte)'=' or (byte)'>' or (byte)'?'))
            return length;

        if (!TryParseParameters(parameters, out var first, out var modifierParam)) return length;
        var modifiers = ModifiersFrom(modifierParam) | (alt ? ConsoleModifiers.Alt : 0);

        ConsoleKey? key = finalByte switch
        {
            (byte)'~' => TildeKey(first),
            (byte)'Z' => ConsoleKey.Tab,
            _ => FinalKey(finalByte),
        };
        if (key is not { } k) return length;

        if (finalByte == (byte)'Z') modifiers |= ConsoleModifiers.Shift;
        sink.Key(Named(k, k == ConsoleKey.Tab ? '\t' : '\0', modifiers));
        return length;
    }

    /// <summary>OSC (ESC ]) and DCS (ESC P) strings, ended by BEL or ESC \. Dropped.</summary>
    private static int DecodeString(ReadOnlySpan<byte> s, bool final, IInputSink sink)
    {
        for (var i = 2; i < s.Length; i++)
        {
            if (s[i] == 0x07) return i + 1;
            if (s[i] == Esc && i + 1 < s.Length && s[i + 1] == (byte)'\\') return i + 2;
            if (i >= MaxStringLength) return i;
        }
        if (!final) return 0;

        // Never terminated: it was Alt+] or Alt+P after all
        return 1 + DecodePlain(s[1..], final, alt: true, sink);
    }

    private static int DecodePlain(ReadOnlySpan<byte> s, bool final, bool alt, IInputSink sink)
    {
        var b = s[0];
        var altMod = alt ? ConsoleModifiers.Alt : 0;
        if (b < 0x80)
        {
            sink.Key(Ascii(b, altMod));
            return 1;
        }

        var status = Rune.DecodeFromUtf8(s, out var rune, out var consumed);
        if (status == OperationStatus.NeedMoreData && !final) return 0;
        if (status != OperationStatus.Done) rune = Rune.ReplacementChar;

        Span<char> chars = stackalloc char[2];
        var count = rune.EncodeToUtf16(chars);
        for (var i = 0; i < count; i++)
            sink.Key(new ConsoleKeyInfo(chars[i], 0, false, alt, false));
        return Math.Max(consumed, 1);
    }

    private static ConsoleKeyInfo Ascii(byte b, ConsoleModifiers alt)
    {
        var c = (char)b;
        return b switch
        {
            0x0D or 0x0A => Named(ConsoleKey.Enter, '\r', alt),
            0x09 => Named(ConsoleKey.Tab, '\t', alt),
            0x7F => Named(ConsoleKey.Backspace, '\b', alt),
            0x08 => Named(ConsoleKey.Backspace, '\b', alt | ConsoleModifiers.Control),
            0x1B => Named(ConsoleKey.Escape, '\x1b', alt),
            0x00 => Named(ConsoleKey.Spacebar, '\0', alt | ConsoleModifiers.Control),
            >= 0x01 and <= 0x1A => Named(ConsoleKey.A + (b - 1), c, alt | ConsoleModifiers.Control),
            >= 0x1C and <= 0x1F => Named(0, c, alt | ConsoleModifiers.Control),
            (byte)' ' => Named(ConsoleKey.Spacebar, ' ', alt),
            >= (byte)'a' and <= (byte)'z' => Named(ConsoleKey.A + (b - 'a'), c, alt),
            >= (byte)'A' and <= (byte)'Z' => Named(ConsoleKey.A + (b - 'A'), c, alt | ConsoleModifiers.Shift),
            >= (byte)'0' and <= (byte)'9' => Named(ConsoleKey.D0 + (b - '0'), c, alt),
            _ => Named(0, c, alt),
        };
    }

    private static ConsoleKeyInfo Named(ConsoleKey key, char c, ConsoleModifiers modifiers) =>
        new(c, key,
            (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0);

    private static ConsoleKey? FinalKey(byte b) => b switch
    {
        (byte)'A' => ConsoleKey.UpArrow,
        (byte)'B' => ConsoleKey.DownArrow,
        (byte)'C' => ConsoleKey.RightArrow,
        (byte)'D' => ConsoleKey.LeftArrow,
        (byte)'H' => ConsoleKey.Home,
        (byte)'F' => ConsoleKey.End,
        (byte)'P' => ConsoleKey.F1,
        (byte)'Q' => ConsoleKey.F2,
        (byte)'R' => ConsoleKey.F3,
        (byte)'S' => ConsoleKey.F4,
        _ => null,
    };

    private static ConsoleKey? TildeKey(int code) => code switch
    {
        1 or 7 => ConsoleKey.Home,
        2 => ConsoleKey.Insert,
        3 => ConsoleKey.Delete,
        4 or 8 => ConsoleKey.End,
        5 => ConsoleKey.PageUp,
        6 => ConsoleKey.PageDown,
        >= 11 and <= 15 => ConsoleKey.F1 + (code - 11),
        >= 17 and <= 21 => ConsoleKey.F6 + (code - 17),
        23 => ConsoleKey.F11,
        24 => ConsoleKey.F12,
        _ => null, // includes the paste markers 200 and 201
    };

    /// <summary>The modifier parameter m: bits of m − 1 are Shift 1, Alt 2, Control 4, Meta 8 (as Alt).</summary>
    private static ConsoleModifiers ModifiersFrom(int m)
    {
        if (m <= 1) return 0;
        var bits = m - 1;
        ConsoleModifiers modifiers = 0;
        if ((bits & 1) != 0) modifiers |= ConsoleModifiers.Shift;
        if ((bits & (2 | 8)) != 0) modifiers |= ConsoleModifiers.Alt;
        if ((bits & 4) != 0) modifiers |= ConsoleModifiers.Control;
        return modifiers;
    }

    /// <summary>Parse "", "n" or "n;m". Anything else isn't a key.</summary>
    private static bool TryParseParameters(ReadOnlySpan<byte> p, out int first, out int modifier)
    {
        first = 0;
        modifier = 1;
        var separator = p.IndexOf((byte)';');
        var head = separator < 0 ? p : p[..separator];
        if (!TryParseInt(head, out first)) return false;
        if (separator < 0) return true;
        var tail = p[(separator + 1)..];
        return tail.IndexOf((byte)';') < 0 && TryParseInt(tail, out modifier);
    }

    private static bool TryParseInt(ReadOnlySpan<byte> digits, out int value)
    {
        value = 0;
        foreach (var d in digits)
        {
            if (d is < (byte)'0' or > (byte)'9' || value > 10_000) return false;
            value = value * 10 + (d - '0');
        }
        return true;
    }
}
