using System.Buffers;
using System.Globalization;
using System.Text;

namespace MiniTui;

/// <summary>
/// Encodes frames as ANSI escape sequences into one reusable byte buffer (§5.2).
/// Tracks the terminal's cursor position and current style so it emits only what changes.
/// </summary>
internal sealed class AnsiEncoder : IDisposable
{
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
    private int _length;

    // Cursor position as the terminal sees it; -1 when unknown.
    private int _cursorX = -1;
    private int _cursorY = -1;

    // Current SGR state as the terminal sees it, already downsampled.
    private Color _fg;
    private Color _bg;
    private Modifiers _modifiers;
    private bool _styleKnown;

    public AnsiEncoder(ColorMode mode) => Mode = mode;

    public ColorMode Mode { get; }

    /// <summary>Screen width, used to know when writing a cell leaves the cursor at the right margin.</summary>
    public int ScreenWidth { get; set; } = int.MaxValue;

    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

    public void Clear() => _length = 0;

    /// <summary>Forget the tracked cursor position and style, e.g. after switching screens.</summary>
    public void Forget()
    {
        _cursorX = _cursorY = -1;
        _styleKnown = false;
    }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    public void BeginFrame(bool fullRedraw)
    {
        Append("\e[?2026h"u8);
        if (fullRedraw) Append("\e[2J"u8);
    }

    public void WriteCell(int x, int y, Cell cell)
    {
        if (x != _cursorX || y != _cursorY) MoveTo(x, y);

        var fg = cell.Fg.ToMode(Mode);
        var bg = cell.Bg.ToMode(Mode);
        if (!_styleKnown || fg != _fg || bg != _bg || cell.Modifiers != _modifiers)
            WriteSgr(fg, bg, cell.Modifiers);

        var symbol = cell.Symbol;
        Ensure(Encoding.UTF8.GetMaxByteCount(symbol.Length));
        _length += Encoding.UTF8.GetBytes(symbol, _buffer.AsSpan(_length));

        // With autowrap off, writing in the last column leaves the cursor there, so its position is unknown.
        var next = x + Math.Max(1, cell.Width);
        if (next >= ScreenWidth) _cursorX = _cursorY = -1;
        else _cursorX = next;
    }

    public void EndFrame((int X, int Y)? cursor)
    {
        Append("\e[0m"u8);
        _fg = _bg = Color.Reset;
        _modifiers = Modifiers.None;
        _styleKnown = true;

        if (cursor is var (x, y))
        {
            MoveTo(x, y);
            Append("\e[?25h"u8);
        }
        else
        {
            Append("\e[?25l"u8);
        }
        Append("\e[?2026l"u8);
    }

    private void MoveTo(int x, int y)
    {
        Append("\e["u8);
        AppendNumber(y + 1);
        Append(";"u8);
        AppendNumber(x + 1);
        Append("H"u8);
        _cursorX = x;
        _cursorY = y;
    }

    private void WriteSgr(Color fg, Color bg, Modifiers modifiers)
    {
        Append("\e[0"u8);
        if ((modifiers & Modifiers.Bold) != 0) Append(";1"u8);
        if ((modifiers & Modifiers.Dim) != 0) Append(";2"u8);
        if ((modifiers & Modifiers.Italic) != 0) Append(";3"u8);
        if ((modifiers & Modifiers.Underlined) != 0) Append(";4"u8);
        if ((modifiers & Modifiers.Reversed) != 0) Append(";7"u8);
        if ((modifiers & Modifiers.CrossedOut) != 0) Append(";9"u8);
        if (Mode != ColorMode.None)
        {
            AppendColor(fg, background: false);
            AppendColor(bg, background: true);
        }
        Append("m"u8);

        _fg = fg;
        _bg = bg;
        _modifiers = modifiers;
        _styleKnown = true;
    }

    private void AppendColor(Color color, bool background)
    {
        Append(";"u8);
        switch (color.Kind)
        {
            case ColorKind.Named:
                var index = color.Index;
                AppendNumber((index < 8 ? 30 + index : 90 + index - 8) + (background ? 10 : 0));
                break;
            case ColorKind.Indexed:
                Append(background ? "48;5;"u8 : "38;5;"u8);
                AppendNumber(color.Index);
                break;
            case ColorKind.Rgb:
                var (r, g, b) = color.RgbValue;
                Append(background ? "48;2;"u8 : "38;2;"u8);
                AppendNumber(r);
                Append(";"u8);
                AppendNumber(g);
                Append(";"u8);
                AppendNumber(b);
                break;
            default:
                Append(background ? "49"u8 : "39"u8);
                break;
        }
    }

    private void AppendNumber(int value)
    {
        Ensure(11);
        value.TryFormat(_buffer.AsSpan(_length), out var written, default, CultureInfo.InvariantCulture);
        _length += written;
    }

    private void Ensure(int extra)
    {
        if (_length + extra <= _buffer.Length) return;
        var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + extra));
        _buffer.AsSpan(0, _length).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = bigger;
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
        _length = 0;
    }
}
