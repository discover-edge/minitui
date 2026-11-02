namespace MiniTui;

/// <summary>
/// The key encodings of the keyboard protocols (1.x spec §12.5): kitty's <c>CSI code ; modifiers u</c>, xterm
/// modifyOtherKeys' <c>CSI 27 ; modifiers ; code ~</c>, and Windows Terminal's win32-input-mode
/// <c>CSI Vk ; Sc ; Uc ; Kd ; Cs ; Rc _</c>. A key the legacy encoding can express decodes to the same
/// <see cref="ConsoleKeyInfo"/> as it does there. Pure, so it's tested on every platform.
/// </summary>
internal static class KeyProtocols
{
    private const int MaxRepeat = 32;

    // win32-input-mode control key state
    private const int RightAlt = 0x01;
    private const int LeftAlt = 0x02;
    private const int RightCtrl = 0x04;
    private const int LeftCtrl = 0x08;
    private const int Shift = 0x10;

    /// <summary>
    /// Decode a CSI sequence in one of the protocols' forms. Returns false if it isn't one, so the legacy forms
    /// are tried; true if it was, whether or not it produced a key.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> parameters, byte finalByte, bool alt, IInputSink sink)
    {
        Span<int> values = stackalloc int[6];
        switch (finalByte)
        {
            case (byte)'u':
            {
                // code[:shifted[:base]] ; modifiers[:event] ; text — only the first sub-parameter of each is read
                var count = Parse(parameters, values);
                if (count is < 1 or > 3 || values[0] < 0) return true;
                var m = count >= 2 && values[1] > 0 ? values[1] : 1;
                if (KittyModifiers(m) is not { } modifiers) return true;
                if (FromCode(values[0], modifiers | (alt ? ConsoleModifiers.Alt : 0)) is { } key) sink.Key(key);
                return true;
            }
            case (byte)'~':
            {
                // modifyOtherKeys: 27 ; modifiers ; code
                if (Parse(parameters, values) != 3 || values[0] != 27) return false;
                var modifiers = XtermModifiers(values[1]) | (alt ? ConsoleModifiers.Alt : 0);
                if (FromCode(values[2], modifiers) is { } key) sink.Key(key);
                return true;
            }
            case (byte)'_':
            {
                values.Fill(-1);
                var count = Parse(parameters, values);
                if (count is < 1 or > 6) return true;
                sink.Reply(new TerminalReply.Win32InputMode());
                Win32(values, alt, sink);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// A key from a Unicode code point (letters lowercase, or uppercase from modifyOtherKeys) or one of kitty's
    /// functional key codes, with its modifiers. Null for keys MiniTui has no <see cref="ConsoleKeyInfo"/> for.
    /// </summary>
    internal static ConsoleKeyInfo? FromCode(int code, ConsoleModifiers modifiers)
    {
        var ctrl = (modifiers & ConsoleModifiers.Control) != 0;
        switch (code)
        {
            case 27: return Named(ConsoleKey.Escape, '\x1b', modifiers);
            case 13: return Named(ConsoleKey.Enter, '\r', modifiers);
            case 9: return Named(ConsoleKey.Tab, '\t', modifiers);
            case 127 or 8: return Named(ConsoleKey.Backspace, '\b', modifiers);
            case 32: return Named(ConsoleKey.Spacebar, ctrl ? '\0' : ' ', modifiers);
        }

        if (code is >= 'A' and <= 'Z')
        {
            code += 'a' - 'A';
            modifiers |= ConsoleModifiers.Shift;
        }
        if (code is >= 'a' and <= 'z')
        {
            // As the legacy encoding: Ctrl+letter is the control character, Shift+letter the capital
            var c = ctrl ? (char)(code - 'a' + 1)
                : (modifiers & ConsoleModifiers.Shift) != 0 ? (char)(code - 'a' + 'A')
                : (char)code;
            return Named(ConsoleKey.A + (code - 'a'), c, modifiers);
        }
        if (code is >= '0' and <= '9') return Named(ConsoleKey.D0 + (code - '0'), (char)code, modifiers);

        // Ctrl+\ and Ctrl+] are control characters in the legacy encoding too
        if (ctrl && code == '\\') return Named(0, '\x1c', modifiers);
        if (ctrl && code == ']') return Named(0, '\x1d', modifiers);

        if (code is >= 57344 and <= 63743) return Functional(code, modifiers);
        if (code is < 0x20 or (>= 0x7F and < 0xA0) or > 0xFFFF || char.IsSurrogate((char)code)) return null;
        return Named(0, (char)code, modifiers);
    }

    // Kitty's private-use codes: keypad keys and F13–F24. Modifier-only, lock and media keys give null.
    private static ConsoleKeyInfo? Functional(int code, ConsoleModifiers modifiers) => code switch
    {
        >= 57399 and <= 57408 => Named(ConsoleKey.NumPad0 + (code - 57399), (char)('0' + code - 57399), modifiers),
        57409 => Named(ConsoleKey.Decimal, '.', modifiers),
        57410 => Named(ConsoleKey.Divide, '/', modifiers),
        57411 => Named(ConsoleKey.Multiply, '*', modifiers),
        57412 => Named(ConsoleKey.Subtract, '-', modifiers),
        57413 => Named(ConsoleKey.Add, '+', modifiers),
        57414 => Named(ConsoleKey.Enter, '\r', modifiers),
        57415 => Named(0, '=', modifiers),
        57416 => Named(ConsoleKey.Separator, ',', modifiers),
        57417 => Named(ConsoleKey.LeftArrow, '\0', modifiers),
        57418 => Named(ConsoleKey.RightArrow, '\0', modifiers),
        57419 => Named(ConsoleKey.UpArrow, '\0', modifiers),
        57420 => Named(ConsoleKey.DownArrow, '\0', modifiers),
        57421 => Named(ConsoleKey.PageUp, '\0', modifiers),
        57422 => Named(ConsoleKey.PageDown, '\0', modifiers),
        57423 => Named(ConsoleKey.Home, '\0', modifiers),
        57424 => Named(ConsoleKey.End, '\0', modifiers),
        57425 => Named(ConsoleKey.Insert, '\0', modifiers),
        57426 => Named(ConsoleKey.Delete, '\0', modifiers),
        >= 57376 and <= 57387 => Named(ConsoleKey.F13 + (code - 57376), '\0', modifiers),
        _ => null,
    };

    /// <summary>
    /// Kitty's modifier parameter: the bits of m − 1 are Shift 1, Alt 2, Ctrl 4, Super 8, Hyper 16, Meta 32 (as
    /// Alt), Caps Lock 64 and Num Lock 128 (ignored). Null with Super or Hyper: the OS or terminal normally takes
    /// those, and there's no <see cref="ConsoleModifiers"/> for them.
    /// </summary>
    internal static ConsoleModifiers? KittyModifiers(int m)
    {
        var bits = Math.Max(0, m - 1);
        if ((bits & (8 | 16)) != 0) return null;
        ConsoleModifiers modifiers = 0;
        if ((bits & 1) != 0) modifiers |= ConsoleModifiers.Shift;
        if ((bits & (2 | 32)) != 0) modifiers |= ConsoleModifiers.Alt;
        if ((bits & 4) != 0) modifiers |= ConsoleModifiers.Control;
        return modifiers;
    }

    // xterm's: Shift 1, Alt 2, Ctrl 4, Meta 8 (as Alt), as the legacy forms
    private static ConsoleModifiers XtermModifiers(int m)
    {
        var bits = Math.Max(0, m - 1);
        ConsoleModifiers modifiers = 0;
        if ((bits & 1) != 0) modifiers |= ConsoleModifiers.Shift;
        if ((bits & (2 | 8)) != 0) modifiers |= ConsoleModifiers.Alt;
        if ((bits & 4) != 0) modifiers |= ConsoleModifiers.Control;
        return modifiers;
    }

    /// <summary>
    /// A win32-input-mode key event: Vk ; Sc ; Uc ; Kd ; Cs ; Rc, any of them omitted (-1 here). Key downs only;
    /// modifier-only keys are dropped.
    /// </summary>
    private static void Win32(ReadOnlySpan<int> v, bool alt, IInputSink sink)
    {
        var vk = Math.Max(0, v[0]);
        var uc = Math.Max(0, v[2]);
        var down = v[3] == 1;
        var state = Math.Max(0, v[4]);
        var repeat = Math.Clamp(v[5] < 0 ? 1 : v[5], 1, MaxRepeat);
        if (!down || vk is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or 0x90 or 0x91 or >= 0xA0 and <= 0xA5) return;

        ConsoleModifiers modifiers = alt ? ConsoleModifiers.Alt : 0;
        if ((state & Shift) != 0) modifiers |= ConsoleModifiers.Shift;
        if ((state & (LeftAlt | RightAlt)) != 0) modifiers |= ConsoleModifiers.Alt;
        if ((state & (LeftCtrl | RightCtrl)) != 0) modifiers |= ConsoleModifiers.Control;

        // AltGr (Right Alt with Left Ctrl) types a character, as the Phase 10 translator treats it
        var printable = uc >= 0x20 && uc != 0x7F;
        if (printable && (state & RightAlt) != 0 && (state & LeftCtrl) != 0)
            modifiers &= ~(ConsoleModifiers.Alt | ConsoleModifiers.Control);

        var key = Win32Key(vk, (char)uc, printable, modifiers);
        if (key is not { } k) return;
        for (var i = 0; i < repeat; i++) sink.Key(k);
    }

    private static ConsoleKeyInfo? Win32Key(int vk, char uc, bool printable, ConsoleModifiers modifiers)
    {
        var command = (modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0;

        // Typed text decodes as the same character typed in the legacy encoding
        if (printable && !command)
            return uc < 0x80 ? AnsiInputDecoder.Ascii((byte)uc, 0) : Named(0, uc, 0);

        return vk switch
        {
            0x0D => Named(ConsoleKey.Enter, '\r', modifiers),
            0x09 => Named(ConsoleKey.Tab, '\t', modifiers),
            0x08 => Named(ConsoleKey.Backspace, '\b', modifiers),
            0x1B => Named(ConsoleKey.Escape, '\x1b', modifiers),
            0x20 => FromCode(32, modifiers),
            >= 0x41 and <= 0x5A => FromCode('a' + (vk - 0x41), modifiers),
            >= 0x30 and <= 0x39 => FromCode(vk, modifiers),
            _ when printable && uc is var c => Named((ConsoleKey)vk, c, modifiers),
            _ => Named((ConsoleKey)vk, '\0', modifiers),
        };
    }

    private static ConsoleKeyInfo Named(ConsoleKey key, char c, ConsoleModifiers modifiers) =>
        AnsiInputDecoder.Named(key, c, modifiers);

    /// <summary>Parse ";"-separated numbers, reading the first ":"-separated part of each. Empty parts stay as given.</summary>
    /// <returns>How many parameters there were, or -1 if one isn't a number.</returns>
    private static int Parse(ReadOnlySpan<byte> p, Span<int> values)
    {
        if (p.IsEmpty) return 0;
        var count = 0;
        while (true)
        {
            var end = p.IndexOf((byte)';');
            var part = end < 0 ? p : p[..end];
            var colon = part.IndexOf((byte)':');
            if (colon >= 0) part = part[..colon];
            if (count >= values.Length) return -1;
            if (!part.IsEmpty)
            {
                var n = 0;
                foreach (var d in part)
                {
                    if (d is < (byte)'0' or > (byte)'9' || n > 1_000_000) return -1;
                    n = n * 10 + (d - '0');
                }
                values[count] = n;
            }
            count++;
            if (end < 0) return count;
            p = p[(end + 1)..];
        }
    }
}

/// <summary>The bytes that turn each keyboard protocol on and off (1.x spec §12.5).</summary>
internal static class KeyboardModes
{
    public static ReadOnlySpan<byte> On(KeyboardProtocol protocol) => protocol switch
    {
        KeyboardProtocol.Kitty => "\e[>1u"u8,
        KeyboardProtocol.ModifyOtherKeys => "\e[>4;2m"u8,
        KeyboardProtocol.Win32InputMode => "\e[?9001h"u8,
        _ => default,
    };

    public static ReadOnlySpan<byte> Off(KeyboardProtocol protocol) => protocol switch
    {
        KeyboardProtocol.Kitty => "\e[<u"u8,
        KeyboardProtocol.ModifyOtherKeys => "\e[>4;0m"u8,
        KeyboardProtocol.Win32InputMode => "\e[?9001l"u8,
        _ => default,
    };
}
