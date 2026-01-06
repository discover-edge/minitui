namespace MiniTui;

/// <summary>
/// 2D integer vector for positions and sizes.
/// </summary>
public readonly record struct Vec2(int X, int Y)
{
    public static Vec2 Zero => new(0, 0);
    public static Vec2 One => new(1, 1);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>
/// Rectangle defined by position and size.
/// </summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public Vec2 Position => new(X, Y);
    public Vec2 Size => new(Width, Height);
    public Vec2 Center => new(X + Width / 2, Y + Height / 2);

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
    public bool Contains(Vec2 pos) => Contains(pos.X, pos.Y);
}
