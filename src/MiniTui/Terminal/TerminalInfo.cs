using System.Globalization;
using System.Text;

namespace MiniTui;

/// <summary>
/// What the terminal said about itself in reply to the startup queries (1.x spec §12.3). Unknown values are null.
/// </summary>
public sealed record TerminalInfo
{
    /// <summary>
    /// True once the terminal answered (its Primary Device Attributes reply arrived). False before that, with
    /// queries turned off, on backends that aren't a VT terminal, and when it never answered.
    /// </summary>
    public bool Answered { get; init; }

    /// <summary>The default background color (the OSC 11 reply), as an RGB color.</summary>
    public Color? Background { get; init; }

    /// <summary>The default foreground color (the OSC 10 reply), as an RGB color.</summary>
    public Color? Foreground { get; init; }

    /// <summary>Whether the background is dark: from <see cref="Background"/>, or else <c>COLORFGBG</c>.</summary>
    public bool? IsDark { get; init; }

    /// <summary>The keyboard protocol in use (1.x spec §12.5). Legacy until one is detected and turned on.</summary>
    public KeyboardProtocol Keyboard { get; init; }

    /// <summary>The terminal's name and version from its XTVERSION reply.</summary>
    public string? Name { get; init; }
}

/// <summary>How the terminal encodes keys (1.x spec §12.5).</summary>
public enum KeyboardProtocol
{
    /// <summary>The classic encoding: keys such as Ctrl+Enter arrive as plain Enter.</summary>
    Legacy,

    /// <summary>The kitty keyboard protocol, with the "disambiguate escape codes" flag.</summary>
    Kitty,

    /// <summary>xterm's modifyOtherKeys, level 2.</summary>
    ModifyOtherKeys,

    /// <summary>Windows Terminal's win32-input-mode (<c>?9001</c>).</summary>
    Win32InputMode,
}

/// <summary>A reply to one of the startup queries, decoded by <see cref="AnsiInputDecoder"/>.</summary>
internal abstract record TerminalReply
{
    /// <summary><c>CSI ? flags u</c>: the kitty keyboard protocol is supported.</summary>
    public sealed record KittyFlags(int Flags) : TerminalReply;

    /// <summary><c>CSI &gt; 4 ; value m</c>: the XTQMODKEYS reply, so modifyOtherKeys is supported.</summary>
    public sealed record ModifyOtherKeys(int Value) : TerminalReply;

    /// <summary><c>OSC 10/11 ; rgb:…</c>: the default foreground (10) or background (11).</summary>
    public sealed record DefaultColor(int Which, Color Color) : TerminalReply;

    /// <summary><c>DCS &gt; | text ST</c>: the XTVERSION reply.</summary>
    public sealed record Version(string Text) : TerminalReply;

    /// <summary><c>CSI ? … c</c>: Primary Device Attributes, the last reply to arrive.</summary>
    public sealed record DeviceAttributes : TerminalReply;
}

/// <summary>The startup queries and the parsing of their replies (1.x spec §12.3, §12.4).</summary>
internal static class TerminalQueries
{
    /// <summary>Kitty flags, XTQMODKEYS, OSC 10, OSC 11, XTVERSION, then DA1, which every terminal answers.</summary>
    public static ReadOnlySpan<byte> Bytes => "\e[?u\e[?4m\e]10;?\e\\\e]11;?\e\\\e[>0q\e[c"u8;

    /// <summary>How long the first frame waits for the background with <see cref="App.WithTheme(Theme, Theme)"/>.</summary>
    public static readonly TimeSpan ThemeWait = TimeSpan.FromMilliseconds(150);

    /// <summary>Apply a reply to what's known so far.</summary>
    public static TerminalInfo Apply(TerminalInfo info, TerminalReply reply) => reply switch
    {
        TerminalReply.DeviceAttributes => info with { Answered = true },
        TerminalReply.DefaultColor { Which: 11, Color: var bg } => info with { Background = bg, IsDark = IsDark(bg) },
        TerminalReply.DefaultColor { Which: 10, Color: var fg } => info with { Foreground = fg },
        TerminalReply.Version v => info with { Name = v.Text },
        _ => info,
    };

    /// <summary>Dark when the relative luminance (sRGB, Rec. 709 weights) is below 0.5.</summary>
    public static bool IsDark(Color color)
    {
        var (r, g, b) = color.RgbValue;
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b) < 0.5;
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary><c>COLORFGBG</c> ("fg;bg", or "fg;other;bg"): a bg of 0–6 or 8 is dark, 7 or 9–15 light.</summary>
    public static bool? IsDarkFromColorFgBg(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var last = value[(value.LastIndexOf(';') + 1)..];
        if (!int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var bg) || bg > 15) return null;
        return bg is <= 6 or 8;
    }

    /// <summary>Parse an OSC string's body (between <c>ESC ]</c> and its terminator).</summary>
    public static TerminalReply? ParseOsc(ReadOnlySpan<byte> body)
    {
        // "10;rgb:RRRR/GGGG/BBBB" or "11;rgb:…"
        if (body.Length < 3 || body[0] != (byte)'1' || body[1] is not ((byte)'0' or (byte)'1') || body[2] != (byte)';')
            return null;
        var which = body[1] == (byte)'0' ? 10 : 11;
        var spec = body[3..];
        if (!spec.StartsWith("rgb:"u8)) return null;
        var channels = spec[4..];
        Span<byte> rgb = stackalloc byte[3];
        for (var i = 0; i < 3; i++)
        {
            var slash = channels.IndexOf((byte)'/');
            var part = i < 2 ? (slash < 0 ? default : channels[..slash]) : channels;
            if (!TryParseChannel(part, out rgb[i])) return null;
            channels = i < 2 ? channels[(slash + 1)..] : default;
        }
        return new TerminalReply.DefaultColor(which, Color.Rgb(rgb[0], rgb[1], rgb[2]));
    }

    /// <summary>Parse a DCS string's body (between <c>ESC P</c> and its terminator): <c>&gt;|text</c>.</summary>
    public static TerminalReply? ParseDcs(ReadOnlySpan<byte> body)
    {
        if (!body.StartsWith(">|"u8)) return null;
        var text = Sanitizer.SingleLine(Encoding.UTF8.GetString(body[2..])).Trim();
        return text.Length == 0 ? null : new TerminalReply.Version(text);
    }

    /// <summary>Parse a private CSI reply: its parameters (starting with the private marker) and final byte.</summary>
    public static TerminalReply? ParseCsi(ReadOnlySpan<byte> parameters, byte finalByte)
    {
        if (parameters.IsEmpty) return null;
        var rest = parameters[1..];
        switch ((char)parameters[0], (char)finalByte)
        {
            case ('?', 'u'):
                return TryParseInt(rest, out var flags) ? new TerminalReply.KittyFlags(flags) : null;
            case ('?', 'c'):
                return new TerminalReply.DeviceAttributes();
            case ('>', 'm'):
                // "4;value" (or "4" alone, the default)
                if (!rest.StartsWith("4"u8) || (rest.Length > 1 && rest[1] != (byte)';')) return null;
                var value = 0;
                return rest.Length <= 2 || TryParseInt(rest[2..], out value) ? new TerminalReply.ModifyOtherKeys(value) : null;
            default:
                return null;
        }
    }

    // 1–4 hex digits, scaled to 8 bits: "f" → 0xff, "ff" → 0xff, "ffff" → 0xff, "8000" → 0x80
    private static bool TryParseChannel(ReadOnlySpan<byte> hex, out byte value)
    {
        value = 0;
        if (hex.Length is < 1 or > 4) return false;
        var n = 0;
        foreach (var c in hex)
        {
            var d = c switch
            {
                >= (byte)'0' and <= (byte)'9' => c - '0',
                >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
                _ => -1,
            };
            if (d < 0) return false;
            n = n * 16 + d;
        }
        var max = (1 << (4 * hex.Length)) - 1;
        value = (byte)Math.Round(n * 255.0 / max);
        return true;
    }

    private static bool TryParseInt(ReadOnlySpan<byte> digits, out int value)
    {
        value = 0;
        if (digits.IsEmpty) return false;
        foreach (var d in digits)
        {
            if (d is < (byte)'0' or > (byte)'9' || value > 1_000_000) return false;
            value = value * 10 + (d - '0');
        }
        return true;
    }
}
