using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace MiniTui;

/// <summary>
/// Off-screen buffer for rendering. This is your canvas.
/// </summary>
/// <remarks>
/// No drawing method throws for any coordinates or <see cref="Rect"/>: out-of-bounds writes are ignored.
/// Wide characters always occupy two cells, a head and a continuation, and are never split.
/// </remarks>
public class Buffer
{
    private readonly Cell[] _cells;

    /// <summary>
    /// Buffer width in cells.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Buffer height in cells.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// The full buffer area as a Rect.
    /// </summary>
    public Rect Area => new(0, 0, Width, Height);

    /// <summary>
    /// Where the visible terminal cursor should be shown this frame, or null to hide it.
    /// Set with <see cref="SetCursor"/>; reset by <see cref="Clear"/>.
    /// </summary>
    public (int X, int Y)? CursorRequest { get; private set; }

    /// <summary>
    /// Create a new buffer with the specified dimensions. Negative sizes are treated as zero.
    /// </summary>
    public Buffer(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _cells = new Cell[Width * Height];
        Clear();
    }

    /// <summary>
    /// Clear the buffer to empty cells and drop any cursor request.
    /// </summary>
    public void Clear()
    {
        Array.Fill(_cells, Cell.Empty);
        CursorRequest = null;
    }

    /// <summary>
    /// Fill the entire buffer with a cell.
    /// </summary>
    public void Fill(Cell cell) => FillRect(Area, cell);

    /// <summary>
    /// Set a cell at the given position. Ignores out-of-bounds coordinates.
    /// The symbol is sanitized (a zero-width symbol becomes a space), and wide characters overwrite the cell to their right.
    /// A wide character that would start in the last column is written as a space.
    /// </summary>
    public void Set(int x, int y, Cell cell)
    {
        if (!InBounds(x, y)) return;
        Put(x, y, Normalize(cell));
    }

    /// <summary>
    /// Get a cell at the given position. Returns empty cell for out-of-bounds.
    /// </summary>
    public Cell Get(int x, int y) => InBounds(x, y) ? _cells[y * Width + x] : Cell.Empty;

    /// <summary>
    /// Write a single line of text, patching each cell with <paramref name="style"/>.
    /// Control characters are sanitized; tabs and line breaks become spaces.
    /// Stops at <paramref name="maxWidth"/> columns or the buffer edge, and never writes half of a wide character.
    /// </summary>
    /// <returns>The number of columns advanced.</returns>
    public int SetString(int x, int y, ReadOnlySpan<char> text, Style style, int maxWidth = int.MaxValue) =>
        SetStringCore(x, y, text, style, maxWidth, out _);

    /// <summary>
    /// Write a line of styled spans, up to <paramref name="maxWidth"/> columns.
    /// Like Ratatui's <c>set_line</c>, this ignores <see cref="Line.Alignment"/>.
    /// </summary>
    /// <returns>The number of columns advanced.</returns>
    public int SetLine(int x, int y, Line line, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(line);
        var used = 0;
        foreach (var span in line.Spans)
        {
            if (used >= maxWidth) break;
            used += SetStringCore(x + used, y, span.Content, span.Style, maxWidth - used, out var complete);
            if (!complete) break;
        }
        return used;
    }

    /// <summary>
    /// Patch the style of every cell in <paramref name="area"/>, keeping the symbols.
    /// </summary>
    public void SetStyle(Rect area, Style style)
    {
        var clip = area.Intersect(Area);
        for (var y = clip.Y; y < clip.Bottom; y++)
        for (var x = clip.X; x < clip.Right; x++)
            _cells[y * Width + x] = style.ApplyTo(_cells[y * Width + x]);
    }

    /// <summary>
    /// Request that the visible terminal cursor be shown at this position for this frame.
    /// Out-of-bounds positions are ignored.
    /// </summary>
    public void SetCursor(int x, int y)
    {
        if (InBounds(x, y)) CursorRequest = (x, y);
    }

    /// <summary>
    /// The buffer's symbols, one string per row, with continuation cells omitted. Intended for tests.
    /// </summary>
    public string[] ToLines()
    {
        var lines = new string[Height];
        var sb = new StringBuilder(Width);
        for (var y = 0; y < Height; y++)
        {
            sb.Clear();
            for (var x = 0; x < Width; x++)
                sb.Append(_cells[y * Width + x].Symbol);
            lines[y] = sb.ToString();
        }
        return lines;
    }

    // ========== Drawing Primitives ==========

    /// <summary>
    /// Draw a string at the given position. Truncates if it exceeds buffer width.
    /// A null <paramref name="bg"/> keeps the existing background.
    /// </summary>
    public void DrawText(int x, int y, string text, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor? bg = null) =>
        SetString(x, y, text, new Style(fg, bg is { } b ? b : null));

    /// <summary>
    /// Draw text centered within a rect.
    /// A null <paramref name="bg"/> keeps the existing background.
    /// </summary>
    public void DrawTextCentered(Rect area, string text, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor? bg = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var x = area.X + (area.Width - UnicodeWidth.Of(text)) / 2;
        var y = area.Y + area.Height / 2;
        DrawText(x, y, text, fg, bg);
    }

    /// <summary>
    /// Draw a horizontal line.
    /// </summary>
    public void DrawHLine(int x, int y, int length, char c = '─', ConsoleColor fg = ConsoleColor.Gray)
    {
        var style = new Style(fg);
        var (from, to) = Clip(x, length, Width);
        for (var i = from; i < to; i++)
            PatchChar(i, y, c, style);
    }

    /// <summary>
    /// Draw a vertical line.
    /// </summary>
    public void DrawVLine(int x, int y, int length, char c = '│', ConsoleColor fg = ConsoleColor.Gray)
    {
        var style = new Style(fg);
        var (from, to) = Clip(y, length, Height);
        for (var i = from; i < to; i++)
            PatchChar(x, i, c, style);
    }

    /// <summary>
    /// Fill a rectangular area with a cell.
    /// </summary>
    public void FillRect(Rect rect, Cell cell)
    {
        cell = Normalize(cell);
        var step = Math.Max(1, cell.Width);
        var clip = rect.Intersect(Area);
        for (var y = clip.Y; y < clip.Bottom; y++)
        for (var x = clip.X; x < clip.Right; x += step)
            Put(x, y, x + step > clip.Right ? cell with { Symbol = " " } : cell);
    }

    /// <summary>
    /// Draw a box border using Unicode box-drawing characters.
    /// </summary>
    public void DrawBox(Rect rect, ConsoleColor fg = ConsoleColor.Gray, BoxStyle style = BoxStyle.Single)
    {
        var chars = style switch
        {
            BoxStyle.Double => ('╔', '╗', '╚', '╝', '═', '║'),
            BoxStyle.Rounded => ('╭', '╮', '╰', '╯', '─', '│'),
            BoxStyle.Heavy => ('┏', '┓', '┗', '┛', '━', '┃'),
            _ => ('┌', '┐', '└', '┘', '─', '│')
        };

        var (tl, tr, bl, br, h, v) = chars;
        var s = new Style(fg);

        // Corners
        PatchChar(rect.X, rect.Y, tl, s);
        PatchChar(rect.Right - 1, rect.Y, tr, s);
        PatchChar(rect.X, rect.Bottom - 1, bl, s);
        PatchChar(rect.Right - 1, rect.Bottom - 1, br, s);

        // Horizontal lines
        var (x1, x2) = Clip(rect.X + 1L, rect.Width - 2L, Width);
        for (var x = x1; x < x2; x++)
        {
            PatchChar(x, rect.Y, h, s);
            PatchChar(x, rect.Bottom - 1, h, s);
        }

        // Vertical lines
        var (y1, y2) = Clip(rect.Y + 1L, rect.Height - 2L, Height);
        for (var y = y1; y < y2; y++)
        {
            PatchChar(rect.X, y, v, s);
            PatchChar(rect.Right - 1, y, v, s);
        }
    }

    /// <summary>
    /// Draw a box with a title in the top border.
    /// </summary>
    public void DrawBoxWithTitle(Rect rect, string title, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor titleFg = ConsoleColor.White, BoxStyle style = BoxStyle.Single)
    {
        ArgumentNullException.ThrowIfNull(title);
        DrawBox(rect, fg, style);

        if (title.Length > 0 && rect.Width > 4)
            SetString(rect.X + 2, rect.Y, title, new Style(titleFg), rect.Width - 4);
    }

    /// <summary>
    /// Draw a progress bar.
    /// </summary>
    public void DrawProgressBar(Rect rect, float progress, ConsoleColor fillFg = ConsoleColor.Green, ConsoleColor emptyFg = ConsoleColor.DarkGray)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        var fillWidth = (int)(rect.Width * progress);
        var fill = new Style(fillFg);
        var empty = new Style(emptyFg);

        var (from, to) = Clip(rect.X, rect.Width, Width);
        for (var x = from; x < to; x++)
        {
            var filled = x - (long)rect.X < fillWidth;
            PatchChar(x, rect.Y, filled ? '█' : '░', filled ? fill : empty);
        }
    }

    // ========== Invariant-preserving writes ==========

    private bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    // The part of [start, start + length) inside [0, size), without overflow.
    private static (int From, int To) Clip(long start, long length, int size)
    {
        var from = (int)Math.Clamp(start, 0, size);
        var to = (int)Math.Clamp(start + Math.Max(0, length), from, size);
        return (from, to);
    }

    private static Cell Normalize(Cell cell)
    {
        var symbol = cell.Symbol;
        if (symbol.Length == 1 && symbol[0] is >= ' ' and <= '~') return cell;
        var clean = Sanitizer.Symbol(symbol);

        // A lone zero-width symbol (combining mark, ZWJ, ...) would merge with the previous cell on screen
        if (UnicodeWidth.StartsWithZeroWidth(clean)) clean = " ";
        return ReferenceEquals(clean, symbol) ? cell : cell with { Symbol = clean };
    }

    private int SetStringCore(int x, int y, ReadOnlySpan<char> text, Style style, int maxWidth, out bool complete)
    {
        complete = false;
        if ((uint)y >= (uint)Height || maxWidth <= 0) return 0;

        var limit = Math.Min((long)x + maxWidth, Width);
        long col = x;

        if (Sanitizer.IsPrintableAscii(text))
        {
            foreach (var c in text)
            {
                if (col >= limit) return (int)(col - x);
                if (col >= 0) PatchSymbol((int)col, y, Symbols.OfAscii(c), 1, style);
                col++;
            }
            complete = true;
            return (int)(col - x);
        }

        var clean = Sanitizer.SingleLine(text);
        var span = clean.AsSpan();
        while (!span.IsEmpty)
        {
            var length = StringInfo.GetNextTextElementLength(span);
            var grapheme = span[..length];
            span = span[length..];

            var w = UnicodeWidth.OfGrapheme(grapheme);
            if (w == 0 || UnicodeWidth.StartsWithZeroWidth(grapheme)) continue;
            if (col + w > limit) return (int)(col - x);
            if (col >= 0)
                PatchSymbol((int)col, y, length == 1 ? Symbols.Of(grapheme[0]) : grapheme.ToString(), w, style);
            col += w;
        }
        complete = true;
        return (int)(col - x);
    }

    private void PatchChar(int x, int y, char c, Style style)
    {
        if (!InBounds(x, y)) return;
        var symbol = Symbols.Of(c);
        var w = UnicodeWidth.OfGrapheme(symbol);
        if (w == 2 && x == Width - 1) symbol = " ";
        PatchSymbol(x, y, symbol, Math.Clamp(w, 1, 2), style);
    }

    // Caller guarantees (x, y) is in bounds and a wide symbol fits.
    private void PatchSymbol(int x, int y, string symbol, int width, Style style)
    {
        var cell = style.ApplyTo(_cells[y * Width + x]) with { Symbol = symbol };
        Put(x, y, cell, width);
    }

    private void Put(int x, int y, Cell cell) => Put(x, y, cell, cell.Width);

    // Writes an already-sanitized cell, keeping the wide-character invariants (§4.5 B2–B4).
    private void Put(int x, int y, Cell cell, int width)
    {
        if (width == 2 && x == Width - 1)
        {
            cell = cell with { Symbol = " " };
            width = 1;
        }

        var row = y * Width;
        ClearOverlap(row, x);
        if (width == 2)
        {
            ClearOverlap(row, x + 1);
            _cells[row + x] = cell;
            _cells[row + x + 1] = cell with { Symbol = "" };
        }
        else
        {
            _cells[row + x] = cell;
        }
    }

    // Before overwriting x: a continuation loses its head, and a wide head loses its continuation.
    private void ClearOverlap(int row, int x)
    {
        var old = _cells[row + x];
        if (old.IsContinuation)
        {
            if (x > 0) _cells[row + x - 1] = _cells[row + x - 1] with { Symbol = " " };
        }
        else if (x + 1 < Width && _cells[row + x + 1].IsContinuation)
        {
            _cells[row + x + 1] = _cells[row + x + 1] with { Symbol = " " };
        }
    }
}

/// <summary>
/// Box drawing styles using Unicode box-drawing characters.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Public API since 0.1; renaming would break consumers.")]
public enum BoxStyle
{
    /// <summary>Single line border (┌─┐)</summary>
    Single,
    /// <summary>Double line border (╔═╗)</summary>
    Double,
    /// <summary>Rounded corners (╭─╮)</summary>
    Rounded,
    /// <summary>Heavy/bold border (┏━┓)</summary>
    Heavy
}
