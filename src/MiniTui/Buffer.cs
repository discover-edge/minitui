using System.Diagnostics.CodeAnalysis;

namespace MiniTui;

/// <summary>
/// A single terminal cell with character and colors.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Public API since 0.1; renaming would break consumers.")]
public record struct Cell(char Char, ConsoleColor Fg, ConsoleColor Bg)
{
    /// <summary>
    /// An empty cell (space with default colors).
    /// </summary>
    public static Cell Empty => new(' ', ConsoleColor.Gray, ConsoleColor.Black);

    /// <summary>
    /// Create a cell from a character with default colors.
    /// </summary>
    public static Cell From(char c) => new(c, ConsoleColor.Gray, ConsoleColor.Black);
}

/// <summary>
/// Off-screen buffer for rendering. This is your canvas.
/// </summary>
public class Buffer
{
    private readonly Cell[,] _cells;

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
    /// Create a new buffer with the specified dimensions.
    /// </summary>
    public Buffer(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new Cell[height, width];
        Clear();
    }

    /// <summary>
    /// Clear the buffer to empty cells.
    /// </summary>
    public void Clear() => Fill(Cell.Empty);

    /// <summary>
    /// Fill the entire buffer with a cell.
    /// </summary>
    public void Fill(Cell cell)
    {
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            _cells[y, x] = cell;
    }

    /// <summary>
    /// Set a cell at the given position. Ignores out-of-bounds coordinates.
    /// </summary>
    public void Set(int x, int y, Cell cell)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
            _cells[y, x] = cell;
    }

    /// <summary>
    /// Get a cell at the given position. Returns empty cell for out-of-bounds.
    /// </summary>
    public Cell Get(int x, int y)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
            return _cells[y, x];
        return Cell.Empty;
    }

    // ========== Drawing Primitives ==========

    /// <summary>
    /// Draw a string at the given position. Truncates if it exceeds buffer width.
    /// </summary>
    public void DrawText(int x, int y, string text, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor bg = ConsoleColor.Black)
    {
        for (var i = 0; i < text.Length; i++)
            Set(x + i, y, new Cell(text[i], fg, bg));
    }

    /// <summary>
    /// Draw text centered within a rect.
    /// </summary>
    public void DrawTextCentered(Rect area, string text, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor bg = ConsoleColor.Black)
    {
        var x = area.X + (area.Width - text.Length) / 2;
        var y = area.Y + area.Height / 2;
        DrawText(x, y, text, fg, bg);
    }

    /// <summary>
    /// Draw a horizontal line.
    /// </summary>
    public void DrawHLine(int x, int y, int length, char c = '─', ConsoleColor fg = ConsoleColor.Gray)
    {
        for (var i = 0; i < length; i++)
            Set(x + i, y, new Cell(c, fg, ConsoleColor.Black));
    }

    /// <summary>
    /// Draw a vertical line.
    /// </summary>
    public void DrawVLine(int x, int y, int length, char c = '│', ConsoleColor fg = ConsoleColor.Gray)
    {
        for (var i = 0; i < length; i++)
            Set(x, y + i, new Cell(c, fg, ConsoleColor.Black));
    }

    /// <summary>
    /// Fill a rectangular area with a cell.
    /// </summary>
    public void FillRect(Rect rect, Cell cell)
    {
        for (var y = rect.Y; y < rect.Bottom; y++)
        for (var x = rect.X; x < rect.Right; x++)
            Set(x, y, cell);
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

        // Corners
        Set(rect.X, rect.Y, new Cell(tl, fg, ConsoleColor.Black));
        Set(rect.Right - 1, rect.Y, new Cell(tr, fg, ConsoleColor.Black));
        Set(rect.X, rect.Bottom - 1, new Cell(bl, fg, ConsoleColor.Black));
        Set(rect.Right - 1, rect.Bottom - 1, new Cell(br, fg, ConsoleColor.Black));

        // Horizontal lines
        for (var x = rect.X + 1; x < rect.Right - 1; x++)
        {
            Set(x, rect.Y, new Cell(h, fg, ConsoleColor.Black));
            Set(x, rect.Bottom - 1, new Cell(h, fg, ConsoleColor.Black));
        }

        // Vertical lines
        for (var y = rect.Y + 1; y < rect.Bottom - 1; y++)
        {
            Set(rect.X, y, new Cell(v, fg, ConsoleColor.Black));
            Set(rect.Right - 1, y, new Cell(v, fg, ConsoleColor.Black));
        }
    }

    /// <summary>
    /// Draw a box with a title in the top border.
    /// </summary>
    public void DrawBoxWithTitle(Rect rect, string title, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor titleFg = ConsoleColor.White, BoxStyle style = BoxStyle.Single)
    {
        DrawBox(rect, fg, style);
        
        if (title.Length > 0 && rect.Width > 4)
        {
            var maxLen = rect.Width - 4;
            var displayTitle = title.Length > maxLen ? title[..maxLen] : title;
            var x = rect.X + 2;
            DrawText(x, rect.Y, displayTitle, titleFg);
        }
    }

    /// <summary>
    /// Draw a progress bar.
    /// </summary>
    public void DrawProgressBar(Rect rect, float progress, ConsoleColor fillFg = ConsoleColor.Green, ConsoleColor emptyFg = ConsoleColor.DarkGray)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        var fillWidth = (int)(rect.Width * progress);

        for (var x = 0; x < rect.Width; x++)
        {
            var c = x < fillWidth ? '█' : '░';
            var fg = x < fillWidth ? fillFg : emptyFg;
            Set(rect.X + x, rect.Y, new Cell(c, fg, ConsoleColor.Black));
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
