using System.Globalization;
using System.Text;

namespace MiniTui.Tests;

/// <summary>
/// Parses the bytes AnsiBackend emits (CUP, SGR, ED 2J, DEC private modes and UTF-8 text)
/// and reconstructs the screen as a cell grid plus mode flags (§10.1).
/// Throws on any sequence it doesn't understand, so the encoder can't emit unexpected output.
/// </summary>
internal sealed class VirtualTerminal
{
    private readonly Cell[] _main;
    private readonly Cell[] _alt;
    private Color _fg;
    private Color _bg;
    private Modifiers _modifiers;

    public VirtualTerminal(int width, int height)
    {
        Width = width;
        Height = height;
        _main = Blank();
        _alt = Blank();
    }

    public int Width { get; }
    public int Height { get; }
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }

    // Modes, with their power-on defaults
    public bool AltScreen { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public bool AutoWrap { get; private set; } = true;
    public bool SynchronizedOutput { get; private set; }
    public bool StyleIsDefault => _fg == Color.Reset && _bg == Color.Reset && _modifiers == Modifiers.None;

    /// <summary>Count of ESC bytes that started a sequence this terminal understood.</summary>
    public int SequenceCount { get; private set; }

    private Cell[] Screen => AltScreen ? _alt : _main;

    public Cell CellAt(int x, int y) => Screen[y * Width + x];

    public void Feed(ReadOnlySpan<byte> bytes) => Feed(Encoding.UTF8.GetString(bytes));

    public void Feed(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\x1b')
            {
                i = ParseEscape(text, i);
                continue;
            }
            if (char.IsControl(text[i]) && !char.IsSurrogate(text[i]))
                throw new InvalidOperationException($"Unexpected control character U+{(int)text[i]:X4} at {i}.");

            var length = StringInfo.GetNextTextElementLength(text, i);
            Print(text.Substring(i, length));
            i += length;
        }
    }

    private int ParseEscape(string text, int start)
    {
        if (start + 1 >= text.Length || text[start + 1] != '[')
            throw new InvalidOperationException($"Unsupported escape at {start}.");

        var i = start + 2;
        var isPrivate = i < text.Length && text[i] == '?';
        if (isPrivate) i++;
        var paramStart = i;
        while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == ';')) i++;
        if (i >= text.Length) throw new InvalidOperationException("Unterminated sequence.");

        var parameters = text[paramStart..i].Split(';').Select(p => p.Length == 0 ? 0 : int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        var final = text[i];
        SequenceCount++;

        switch (final, isPrivate)
        {
            case ('H', false):
                CursorY = Math.Clamp((parameters.ElementAtOrDefault(0) is var row and > 0 ? row : 1) - 1, 0, Height - 1);
                CursorX = Math.Clamp((parameters.ElementAtOrDefault(1) is var col and > 0 ? col : 1) - 1, 0, Width - 1);
                break;
            case ('J', false) when parameters is [2]:
                Array.Fill(Screen, Cell.Empty);
                break;
            case ('m', false):
                ApplySgr(parameters);
                break;
            case ('h' or 'l', true):
                foreach (var mode in parameters) SetMode(mode, final == 'h');
                break;
            default:
                throw new InvalidOperationException($"Unsupported sequence {text[start..(i + 1)].Replace("\x1b", "ESC")}.");
        }
        return i + 1;
    }

    private void SetMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1049:
                if (on && !AltScreen) Array.Fill(_alt, Cell.Empty);
                AltScreen = on;
                break;
            case 25: CursorVisible = on; break;
            case 7: AutoWrap = on; break;
            case 2026: SynchronizedOutput = on; break;
            default: throw new InvalidOperationException($"Unsupported mode ?{mode}.");
        }
    }

    private void ApplySgr(int[] p)
    {
        for (var i = 0; i < p.Length; i++)
        {
            switch (p[i])
            {
                case 0: _fg = _bg = Color.Reset; _modifiers = Modifiers.None; break;
                case 1: _modifiers |= Modifiers.Bold; break;
                case 2: _modifiers |= Modifiers.Dim; break;
                case 3: _modifiers |= Modifiers.Italic; break;
                case 4: _modifiers |= Modifiers.Underlined; break;
                case 7: _modifiers |= Modifiers.Reversed; break;
                case 9: _modifiers |= Modifiers.CrossedOut; break;
                case >= 30 and <= 37: _fg = Named(p[i] - 30); break;
                case >= 90 and <= 97: _fg = Named(p[i] - 90 + 8); break;
                case >= 40 and <= 47: _bg = Named(p[i] - 40); break;
                case >= 100 and <= 107: _bg = Named(p[i] - 100 + 8); break;
                case 39: _fg = Color.Reset; break;
                case 49: _bg = Color.Reset; break;
                case 38 or 48:
                    var color = p[i + 1] switch
                    {
                        5 => Color.Indexed((byte)p[i + 2]),
                        2 => Color.Rgb((byte)p[i + 2], (byte)p[i + 3], (byte)p[i + 4]),
                        _ => throw new InvalidOperationException("Bad extended color.")
                    };
                    if (p[i] == 38) _fg = color; else _bg = color;
                    i += p[i + 1] == 5 ? 2 : 4;
                    break;
                default: throw new InvalidOperationException($"Unsupported SGR {p[i]}.");
            }
        }
    }

    private static Color Named(int ansiIndex) => Color.Indexed((byte)ansiIndex).ToMode(ColorMode.Ansi16);

    private void Print(string grapheme)
    {
        var width = Math.Max(1, UnicodeWidth.OfGrapheme(grapheme));
        var cell = new Cell(grapheme, _fg, _bg, _modifiers);
        var screen = Screen;
        screen[CursorY * Width + CursorX] = cell;
        if (width == 2 && CursorX + 1 < Width)
            screen[CursorY * Width + CursorX + 1] = cell with { Symbol = "" };

        // Autowrap is off in TUI mode: the cursor stops at the right margin
        CursorX = Math.Min(CursorX + width, Width - 1);
    }

    private static Cell[] BlankFor(int size)
    {
        var cells = new Cell[size];
        Array.Fill(cells, Cell.Empty);
        return cells;
    }

    private Cell[] Blank() => BlankFor(Width * Height);
}
