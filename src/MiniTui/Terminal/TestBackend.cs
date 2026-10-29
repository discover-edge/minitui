using System.Text;

namespace MiniTui;

/// <summary>
/// An in-memory terminal for testing apps. It applies each frame's changed cells to a grid,
/// so <see cref="Lines"/> and <see cref="CellAt"/> show what a real terminal would display.
/// </summary>
public sealed class TestBackend : ITerminalBackend
{
    private Cell[] _grid;

    /// <summary>Create a test terminal of the given size.</summary>
    public TestBackend(int width = 80, int height = 24)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _grid = NewGrid(Width, Height);
    }

    /// <summary>Width in cells.</summary>
    public int Width { get; private set; }

    /// <summary>Height in cells.</summary>
    public int Height { get; private set; }

    /// <summary>True between <see cref="Enter"/> and <see cref="Leave"/>.</summary>
    public bool IsEntered { get; private set; }

    /// <summary>The number of times <see cref="Enter"/> actually entered (repeated calls don't count).</summary>
    public int EnterCount { get; private set; }

    /// <summary>The number of times <see cref="Leave"/> actually left.</summary>
    public int LeaveCount { get; private set; }

    /// <summary>The number of <see cref="Draw"/> calls.</summary>
    public int DrawCount { get; private set; }

    /// <summary>The number of <see cref="Draw"/> calls with <c>fullRedraw</c> set.</summary>
    public int FullRedrawCount { get; private set; }

    /// <summary>The number of cells in the most recent <see cref="Draw"/>.</summary>
    public int LastUpdateCount { get; private set; }

    /// <summary>Where the cursor is shown, or null when it's hidden.</summary>
    public (int X, int Y)? CursorPosition { get; private set; }

    /// <summary>True when the cursor is shown.</summary>
    public bool CursorVisible => CursorPosition is not null;

    /// <summary>The displayed symbols, one string per row, with continuation cells omitted.</summary>
    public string[] Lines
    {
        get
        {
            var lines = new string[Height];
            var sb = new StringBuilder(Width);
            for (var y = 0; y < Height; y++)
            {
                sb.Clear();
                for (var x = 0; x < Width; x++) sb.Append(_grid[y * Width + x].Symbol);
                lines[y] = sb.ToString();
            }
            return lines;
        }
    }

    /// <summary>The displayed cell at a position, or <see cref="Cell.Empty"/> when out of bounds.</summary>
    public Cell CellAt(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? _grid[y * Width + x] : Cell.Empty;

    /// <summary>Change the size. The grid is cleared, like a real terminal's next full redraw.</summary>
    public void Resize(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _grid = NewGrid(Width, Height);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetSize() => (Width, Height);

    /// <inheritdoc />
    public void Enter()
    {
        if (IsEntered) return;
        IsEntered = true;
        EnterCount++;
        Array.Fill(_grid, Cell.Empty);
    }

    /// <inheritdoc />
    public void Leave()
    {
        if (!IsEntered) return;
        IsEntered = false;
        LeaveCount++;
        CursorPosition = null;
    }

    /// <inheritdoc />
    public void Draw(IReadOnlyList<CellUpdate> updates, (int X, int Y)? cursor, bool fullRedraw)
    {
        ArgumentNullException.ThrowIfNull(updates);
        DrawCount++;
        LastUpdateCount = updates.Count;
        if (fullRedraw)
        {
            FullRedrawCount++;
            Array.Fill(_grid, Cell.Empty);
        }

        for (var i = 0; i < updates.Count; i++)
        {
            var (x, y, cell) = updates[i];
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) continue;
            _grid[y * Width + x] = cell;
            if (cell.Width == 2 && x + 1 < Width)
                _grid[y * Width + x + 1] = cell with { Symbol = "" };
        }
        CursorPosition = cursor;
    }

    /// <inheritdoc />
    public void Dispose() => Leave();

    private static Cell[] NewGrid(int width, int height)
    {
        var grid = new Cell[width * height];
        Array.Fill(grid, Cell.Empty);
        return grid;
    }
}
