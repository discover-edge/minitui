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

    /// <summary>True when the rect has no area (width or height is zero or negative).</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Returns a new rect with margins subtracted from all sides.
    /// Sizes never go below zero; an over-shrunk rect collapses to its center.
    /// </summary>
    public Rect Shrink(int margin) => Inner(new Margin(margin));

    /// <summary>
    /// Returns a new rect with different margins for horizontal and vertical.
    /// Sizes never go below zero; an over-shrunk rect collapses to its center.
    /// </summary>
    public Rect Shrink(int horizontal, int vertical) => Inner(new Margin(horizontal, vertical));

    /// <summary>
    /// Returns the area inside <paramref name="margin"/>.
    /// Sizes never go below zero; an axis that doesn't fit collapses to its center.
    /// </summary>
    public Rect Inner(Margin margin)
    {
        var (x, width) = InnerAxis(X, Width, margin.Left, margin.Right);
        var (y, height) = InnerAxis(Y, Height, margin.Top, margin.Bottom);
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// The overlap of two rects. If they don't overlap, the result is empty.
    /// </summary>
    public Rect Intersect(Rect other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min((long)X + Width, (long)other.X + other.Width);
        var y2 = Math.Min((long)Y + Height, (long)other.Y + other.Height);
        return new Rect(x1, y1, (int)Math.Max(0, x2 - x1), (int)Math.Max(0, y2 - y1));
    }

    /// <summary>
    /// Move this rect inside <paramref name="bounds"/>, shrinking it if it's larger than the bounds.
    /// </summary>
    public Rect Clamp(Rect bounds)
    {
        var width = Math.Clamp(Width, 0, Math.Max(0, bounds.Width));
        var height = Math.Clamp(Height, 0, Math.Max(0, bounds.Height));
        var x = Math.Clamp(X, bounds.X, bounds.X + Math.Max(0, bounds.Width) - width);
        var y = Math.Clamp(Y, bounds.Y, bounds.Y + Math.Max(0, bounds.Height) - height);
        return new Rect(x, y, width, height);
    }

    /// <summary>Each row of this rect, top to bottom, as a rect of height 1.</summary>
    public IEnumerable<Rect> Rows()
    {
        for (var i = 0; i < Height; i++)
            yield return new Rect(X, Y + i, Math.Max(0, Width), 1);
    }

    /// <summary>Each column of this rect, left to right, as a rect of width 1.</summary>
    public IEnumerable<Rect> Columns()
    {
        for (var i = 0; i < Width; i++)
            yield return new Rect(X + i, Y, 1, Math.Max(0, Height));
    }

    /// <summary>
    /// Split horizontally at a given row offset from top, returns (top, bottom).
    /// The offset is clamped to the rect, so neither part has a negative height.
    /// </summary>
    public (Rect Top, Rect Bottom) SplitHorizontal(int topHeight)
    {
        var height = Math.Max(0, Height);
        topHeight = Math.Clamp(topHeight, 0, height);
        return (new Rect(X, Y, Width, topHeight), new Rect(X, Y + topHeight, Width, height - topHeight));
    }

    /// <summary>
    /// Split vertically at a given column offset from left, returns (left, right).
    /// The offset is clamped to the rect, so neither part has a negative width.
    /// </summary>
    public (Rect Left, Rect Right) SplitVertical(int leftWidth)
    {
        var width = Math.Max(0, Width);
        leftWidth = Math.Clamp(leftWidth, 0, width);
        return (new Rect(X, Y, leftWidth, Height), new Rect(X + leftWidth, Y, width - leftWidth, Height));
    }

    /// <summary>
    /// Check if a point is inside this rect.
    /// </summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>
    /// Check if a point is inside this rect.
    /// </summary>
    public bool Contains(Vec2 pos) => Contains(pos.X, pos.Y);

    private static (int Start, int Size) InnerAxis(int start, int size, int before, int after)
    {
        var inner = size - before - after;
        return inner >= 0 ? (start + before, inner) : (start + Math.Max(0, size) / 2, 0);
    }
}

/// <summary>
/// Spacing on each side of a rect.
/// </summary>
/// <param name="Left">Columns on the left.</param>
/// <param name="Top">Rows on the top.</param>
/// <param name="Right">Columns on the right.</param>
/// <param name="Bottom">Rows on the bottom.</param>
public readonly record struct Margin(int Left, int Top, int Right, int Bottom)
{
    /// <summary>The same margin on all four sides.</summary>
    public Margin(int all) : this(all, all, all, all) { }

    /// <summary>One margin for left and right, another for top and bottom.</summary>
    public Margin(int horizontal, int vertical) : this(horizontal, vertical, horizontal, vertical) { }
}
