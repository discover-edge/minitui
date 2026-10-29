namespace MiniTui;

/// <summary>
/// The kind of a <see cref="Color"/>.
/// </summary>
public enum ColorKind
{
    /// <summary>The terminal's default color.</summary>
    Reset,
    /// <summary>One of the 16 standard ANSI colors.</summary>
    Named,
    /// <summary>An index into the xterm 256-color palette.</summary>
    Indexed,
    /// <summary>A 24-bit RGB color.</summary>
    Rgb
}

/// <summary>
/// A terminal color: the terminal default, one of the 16 named colors, a 256-color palette index, or RGB.
/// <c>default(Color)</c> is <see cref="Reset"/>.
/// </summary>
public readonly record struct Color
{
    // Named: _a is the ANSI index (0-15). Indexed: _a is the palette index. Rgb: _a, _g, _b are r, g, b.
    private readonly byte _a;
    private readonly byte _g;
    private readonly byte _b;

    /// <summary>The kind of color.</summary>
    public ColorKind Kind { get; }

    private Color(ColorKind kind, byte a, byte g = 0, byte b = 0)
    {
        Kind = kind;
        _a = a;
        _g = g;
        _b = b;
    }

    /// <summary>The terminal's default color.</summary>
    public static Color Reset => default;

    /// <summary>Black (SGR 30).</summary>
    public static Color Black => new(ColorKind.Named, 0);
    /// <summary>Dark red (SGR 31).</summary>
    public static Color DarkRed => new(ColorKind.Named, 1);
    /// <summary>Dark green (SGR 32).</summary>
    public static Color DarkGreen => new(ColorKind.Named, 2);
    /// <summary>Dark yellow (SGR 33).</summary>
    public static Color DarkYellow => new(ColorKind.Named, 3);
    /// <summary>Dark blue (SGR 34).</summary>
    public static Color DarkBlue => new(ColorKind.Named, 4);
    /// <summary>Dark magenta (SGR 35).</summary>
    public static Color DarkMagenta => new(ColorKind.Named, 5);
    /// <summary>Dark cyan (SGR 36).</summary>
    public static Color DarkCyan => new(ColorKind.Named, 6);
    /// <summary>Gray (SGR 37).</summary>
    public static Color Gray => new(ColorKind.Named, 7);
    /// <summary>Dark gray (SGR 90).</summary>
    public static Color DarkGray => new(ColorKind.Named, 8);
    /// <summary>Red (SGR 91).</summary>
    public static Color Red => new(ColorKind.Named, 9);
    /// <summary>Green (SGR 92).</summary>
    public static Color Green => new(ColorKind.Named, 10);
    /// <summary>Yellow (SGR 93).</summary>
    public static Color Yellow => new(ColorKind.Named, 11);
    /// <summary>Blue (SGR 94).</summary>
    public static Color Blue => new(ColorKind.Named, 12);
    /// <summary>Magenta (SGR 95).</summary>
    public static Color Magenta => new(ColorKind.Named, 13);
    /// <summary>Cyan (SGR 96).</summary>
    public static Color Cyan => new(ColorKind.Named, 14);
    /// <summary>White (SGR 97).</summary>
    public static Color White => new(ColorKind.Named, 15);

    /// <summary>A color from the xterm 256-color palette.</summary>
    public static Color Indexed(byte index) => new(ColorKind.Indexed, index);

    /// <summary>A 24-bit RGB color.</summary>
    public static Color Rgb(byte r, byte g, byte b) => new(ColorKind.Rgb, r, g, b);

    /// <summary>
    /// Parse an RGB color from <c>"#RRGGBB"</c> or <c>"RRGGBB"</c>.
    /// </summary>
    /// <exception cref="FormatException">The string isn't in one of those formats.</exception>
    public static Color FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var digits = hex.AsSpan();
        if (digits.StartsWith('#')) digits = digits[1..];
        if (digits.Length != 6)
            throw new FormatException($"'{hex}' is not a color in the form #RRGGBB.");

        var bytes = Convert.FromHexString(digits);
        return Rgb(bytes[0], bytes[1], bytes[2]);
    }

    /// <summary>
    /// Convert a <see cref="ConsoleColor"/> to the named color with the same name.
    /// Values outside the <see cref="ConsoleColor"/> range give <see cref="Reset"/>.
    /// </summary>
    public static implicit operator Color(ConsoleColor c) =>
        (uint)c < 16 ? new(ColorKind.Named, ConsoleToAnsi[(int)c]) : Reset;

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        ColorKind.Named => ((ConsoleColor)AnsiToConsole[_a]).ToString(),
        ColorKind.Indexed => $"Indexed({_a})",
        ColorKind.Rgb => $"#{_a:X2}{_g:X2}{_b:X2}",
        _ => "Reset"
    };

    // ===== Internal accessors used by the encoder and backends =====

    /// <summary>ANSI index (0-15) of a named color, or the palette index of an indexed color.</summary>
    internal byte Index => _a;

    internal (byte R, byte G, byte B) RgbValue => (_a, _g, _b);

    /// <summary>The <see cref="ConsoleColor"/> for a named color.</summary>
    internal ConsoleColor ToConsoleColor() => (ConsoleColor)AnsiToConsole[_a & 15];

    /// <summary>
    /// Downsample this color for display in <paramref name="mode"/>. Never changes stored colors.
    /// </summary>
    internal Color ToMode(ColorMode mode) => mode switch
    {
        ColorMode.None => Reset,
        ColorMode.Ansi256 when Kind == ColorKind.Rgb => Indexed(NearestIndexed(_a, _g, _b)),
        ColorMode.Ansi16 when Kind == ColorKind.Rgb => IndexedToNamed(NearestIndexed(_a, _g, _b)),
        ColorMode.Ansi16 when Kind == ColorKind.Indexed => IndexedToNamed(_a),
        _ => this
    };

    // ANSI index order: black, red, green, yellow, blue, magenta, cyan, white, then the bright variants.
    private static ReadOnlySpan<byte> ConsoleToAnsi => [0, 4, 2, 6, 1, 5, 3, 7, 8, 12, 10, 14, 9, 13, 11, 15];
    private static ReadOnlySpan<byte> AnsiToConsole => [0, 4, 2, 6, 1, 5, 3, 7, 8, 12, 10, 14, 9, 13, 11, 15];

    // The 16 standard xterm colors, in ANSI index order.
    private static ReadOnlySpan<byte> Xterm16 =>
    [
        0x00, 0x00, 0x00,  0xCD, 0x00, 0x00,  0x00, 0xCD, 0x00,  0xCD, 0xCD, 0x00,
        0x00, 0x00, 0xEE,  0xCD, 0x00, 0xCD,  0x00, 0xCD, 0xCD,  0xE5, 0xE5, 0xE5,
        0x7F, 0x7F, 0x7F,  0xFF, 0x00, 0x00,  0x00, 0xFF, 0x00,  0xFF, 0xFF, 0x00,
        0x5C, 0x5C, 0xFF,  0xFF, 0x00, 0xFF,  0x00, 0xFF, 0xFF,  0xFF, 0xFF, 0xFF,
    ];

    private static ReadOnlySpan<byte> CubeLevels => [0, 95, 135, 175, 215, 255];

    /// <summary>The RGB value of an xterm 256-color palette index.</summary>
    internal static (byte R, byte G, byte B) IndexedToRgb(byte index)
    {
        if (index < 16)
            return (Xterm16[index * 3], Xterm16[index * 3 + 1], Xterm16[index * 3 + 2]);
        if (index < 232)
        {
            var i = index - 16;
            return (CubeLevels[i / 36], CubeLevels[i / 6 % 6], CubeLevels[i % 6]);
        }
        var gray = (byte)(8 + 10 * (index - 232));
        return (gray, gray, gray);
    }

    /// <summary>Nearest 6×6×6 cube (16–231) or grayscale (232–255) index, by squared RGB distance.</summary>
    internal static byte NearestIndexed(byte r, byte g, byte b)
    {
        var cr = NearestLevel(r);
        var cg = NearestLevel(g);
        var cb = NearestLevel(b);
        var best = (byte)(16 + 36 * cr + 6 * cg + cb);
        var bestDistance = Distance(r, g, b, CubeLevels[cr], CubeLevels[cg], CubeLevels[cb]);

        for (var i = 0; i < 24; i++)
        {
            var v = 8 + 10 * i;
            var d = Distance(r, g, b, v, v, v);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = (byte)(232 + i);
            }
        }
        return best;
    }

    private static Color IndexedToNamed(byte index)
    {
        if (index < 16) return new(ColorKind.Named, index);

        var (r, g, b) = IndexedToRgb(index);
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < 16; i++)
        {
            var d = Distance(r, g, b, Xterm16[i * 3], Xterm16[i * 3 + 1], Xterm16[i * 3 + 2]);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return new(ColorKind.Named, (byte)best);
    }

    private static int NearestLevel(byte v)
    {
        var best = 0;
        for (var i = 1; i < CubeLevels.Length; i++)
            if (Math.Abs(CubeLevels[i] - v) < Math.Abs(CubeLevels[best] - v))
                best = i;
        return best;
    }

    private static int Distance(int r1, int g1, int b1, int r2, int g2, int b2) =>
        (r1 - r2) * (r1 - r2) + (g1 - g2) * (g1 - g2) + (b1 - b2) * (b1 - b2);
}
