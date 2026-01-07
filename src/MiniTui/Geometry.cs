namespace MiniTui;

/// <summary>
/// 2D integer vector for positions and sizes.
/// </summary>
public readonly record struct Vec2(int X, int Y)
{
    /// <summary>Zero vector (0, 0).</summary>
    public static Vec2 Zero => new(0, 0);

    /// <summary>Unit vector (1, 1).</summary>
    public static Vec2 One => new(1, 1);

    /// <summary>Add two vectors.</summary>
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Subtract two vectors.</summary>
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>
/// Rectangle defined by position and size.
/// </summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>Right edge X coordinate (X + Width).</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge Y coordinate (Y + Height).</summary>
    public int Bottom => Y + Height;

    /// <summary>Top-left position as Vec2.</summary>
    public Vec2 Position => new(X, Y);

    /// <summary>Size as Vec2.</summary>
    public Vec2 Size => new(Width, Height);

    /// <summary>Center point of the rectangle.</summary>
    public Vec2 Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>Create a rect from position and size vectors.</summary>
    public Rect(Vec2 pos, Vec2 size) : this(pos.X, pos.Y, size.X, size.Y) { }

    /// <summary>
    /// Returns a new rect with margins subtracted from all sides.
    /// </summary>
    public Rect Shrink(int margin) => new(X + margin, Y + margin, Width - margin * 2, Height - margin * 2);

    /// <summary>
    /// Returns a new rect with different margins for horizontal and vertical.
    /// </summary>
    public Rect Shrink(int horizontal, int vertical) => 
        new(X + horizontal, Y + vertical, Width - horizontal * 2, Height - vertical * 2);

    /// <summary>
    /// Split horizontally at a given row offset from top, returns (top, bottom).
    /// </summary>
    public (Rect Top, Rect Bottom) SplitHorizontal(int topHeight) => (
        new Rect(X, Y, Width, topHeight),
        new Rect(X, Y + topHeight, Width, Height - topHeight)
    );

    /// <summary>
    /// Split vertically at a given column offset from left, returns (left, right).
    /// </summary>
    public (Rect Left, Rect Right) SplitVertical(int leftWidth) => (
        new Rect(X, Y, leftWidth, Height),
        new Rect(X + leftWidth, Y, Width - leftWidth, Height)
    );

    /// <summary>
    /// Check if a point is inside this rect.
    /// </summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>
    /// Check if a point is inside this rect.
    /// </summary>
    public bool Contains(Vec2 pos) => Contains(pos.X, pos.Y);
}

/// <summary>
/// Fluent builder for creating layouts with named regions.
/// </summary>
public class Layout
{
    private readonly Rect _area;
    private readonly bool _vertical;
    private readonly List<(string Name, int? Size, bool Fill)> _regions = [];
    private Dictionary<string, Rect>? _built;

    private Layout(Rect area, bool vertical)
    {
        _area = area;
        _vertical = vertical;
    }

    /// <summary>
    /// Create a vertical layout (rows stacked top to bottom).
    /// </summary>
    public static Layout Vertical(Rect area) => new(area, vertical: true);

    /// <summary>
    /// Create a horizontal layout (columns side by side).
    /// </summary>
    public static Layout Horizontal(Rect area) => new(area, vertical: false);

    /// <summary>
    /// Add a region with a fixed size.
    /// </summary>
    public Layout Add(string name, int size)
    {
        _regions.Add((name, size, false));
        return this;
    }

    /// <summary>
    /// Add a region that fills remaining space.
    /// </summary>
    public Layout AddFill(string name)
    {
        _regions.Add((name, null, true));
        return this;
    }

    /// <summary>
    /// Build the layout and return the regions dictionary.
    /// </summary>
    public Layout Build()
    {
        _built = new Dictionary<string, Rect>();

        var totalSpace = _vertical ? _area.Height : _area.Width;
        var fixedUsed = _regions.Where(r => !r.Fill).Sum(r => r.Size ?? 0);
        var fillCount = _regions.Count(r => r.Fill);
        var fillSize = fillCount > 0 ? (totalSpace - fixedUsed) / fillCount : 0;

        var offset = 0;
        foreach (var (name, size, fill) in _regions)
        {
            var regionSize = fill ? fillSize : size ?? 0;

            if (_vertical)
            {
                _built[name] = new Rect(_area.X, _area.Y + offset, _area.Width, regionSize);
            }
            else
            {
                _built[name] = new Rect(_area.X + offset, _area.Y, regionSize, _area.Height);
            }

            offset += regionSize;
        }

        return this;
    }

    /// <summary>
    /// Get a named region. Must call Build() first.
    /// </summary>
    public Rect this[string name] => _built?[name] ?? throw new InvalidOperationException("Call Build() first");

    /// <summary>
    /// Try to get a named region.
    /// </summary>
    public bool TryGet(string name, out Rect rect)
    {
        if (_built != null && _built.TryGetValue(name, out rect))
            return true;
        rect = default;
        return false;
    }
}
