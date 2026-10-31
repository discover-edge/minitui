namespace MiniTui;

/// <summary>How a canvas turns dots into cells (1.x spec §8.3).</summary>
public enum Marker
{
    /// <summary>Braille, 2×4 dots per cell. Basic glyphs (conhost) draw <see cref="HalfBlock"/> instead.</summary>
    Braille,

    /// <summary><c>▀ ▄ █</c>, 1×2 dots per cell.</summary>
    HalfBlock,

    /// <summary><c>█</c>, one dot per cell.</summary>
    Block,

    /// <summary><c>•</c>, one dot per cell.</summary>
    Dot,
}

/// <summary>
/// A drawing surface in app coordinates: x grows to the right and y grows upward (1.x spec §8.3). Shapes are
/// passed to <see cref="Paint"/>, which runs on every draw.
/// </summary>
public sealed class CanvasBuilder
{
    private (double Min, double Max) _x = (0, 1);
    private (double Min, double Max) _y = (0, 1);
    private Marker _marker = MiniTui.Marker.Braille;
    private Action<CanvasPainter>? _paint;
    private BlockBuilder? _block;
    private Style? _style;

    /// <summary>The x values at the left and right edges. Default 0 to 1.</summary>
    public CanvasBuilder XBounds(double min, double max) { _x = (min, max); return this; }

    /// <summary>The y values at the bottom and top edges. Default 0 to 1.</summary>
    public CanvasBuilder YBounds(double min, double max) { _y = (min, max); return this; }

    /// <summary>How dots are drawn. Default <see cref="MiniTui.Marker.Braille"/>.</summary>
    public CanvasBuilder Marker(Marker marker) { _marker = marker; return this; }

    /// <summary>Draws the shapes. The painter is only valid during the call.</summary>
    public CanvasBuilder Paint(Action<CanvasPainter> paint) { _paint = paint; return this; }

    /// <summary>Draw a block around the canvas.</summary>
    public CanvasBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's text style, for the background and for dots without a style.</summary>
    public CanvasBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the canvas.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Draw(buf, area) ?? area;
        var visible = inner.Intersect(buf.Area);
        if (visible.IsEmpty) return;

        var style = Theme.Current.Text.Patch(_style ?? default);
        buf.SetStyle(visible, style);
        if (_paint is null) return;

        var painter = new CanvasPainter(inner, visible, _marker, _x, _y);
        _paint(painter);
        painter.Render(buf, style);
    }
}

/// <summary>Passed to <see cref="CanvasBuilder.Paint"/>. Shapes are drawn in call order (1.x spec §8.3).</summary>
public sealed class CanvasPainter
{
    private static readonly int[,] BrailleBits = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };
    private static readonly string[] BrailleGlyphs = [.. Enumerable.Range(0, 256).Select(i => ((char)(0x2800 + i)).ToString())];

    private readonly Marker _marker;
    private readonly double _xMin, _xMax, _yMin, _yMax;
    private readonly bool _valid;
    private readonly int _cellDotsX, _cellDotsY;
    private readonly long _dotWidth, _dotHeight;
    private readonly Rect _area;
    private readonly Rect _visible;
    private readonly long _windowLeft, _windowTop, _windowRight, _windowBottom;
    private readonly int[] _masks;
    private readonly Style[] _styles;
    private readonly List<(int X, int Y, Line Text)> _labels = [];

    /// <param name="area">The canvas in cells; the coordinates map onto all of it.</param>
    /// <param name="visible">The part of <paramref name="area"/> inside the buffer, where dots are kept.</param>
    /// <param name="marker">How dots are drawn; braille becomes half blocks with basic glyphs.</param>
    /// <param name="x">The x values at the left and right edges.</param>
    /// <param name="y">The y values at the bottom and top edges.</param>
    internal CanvasPainter(Rect area, Rect visible, Marker marker, (double Min, double Max) x, (double Min, double Max) y)
    {
        _marker = marker == Marker.Braille && Glyphs.Basic ? Marker.HalfBlock : marker;
        (_cellDotsX, _cellDotsY) = _marker switch
        {
            Marker.Braille => (2, 4),
            Marker.HalfBlock => (1, 2),
            _ => (1, 1),
        };
        (_xMin, _xMax, _yMin, _yMax) = (x.Min, x.Max, y.Min, y.Max);
        _valid = double.IsFinite(_xMin) && double.IsFinite(_xMax) && double.IsFinite(_yMin) && double.IsFinite(_yMax)
            && _xMax > _xMin && _yMax > _yMin && !visible.IsEmpty;

        _area = area;
        _visible = visible;
        _dotWidth = (long)Math.Max(0, area.Width) * _cellDotsX;
        _dotHeight = (long)Math.Max(0, area.Height) * _cellDotsY;
        _windowLeft = ((long)visible.X - area.X) * _cellDotsX;
        _windowTop = ((long)visible.Y - area.Y) * _cellDotsY;
        _windowRight = _windowLeft + (long)visible.Width * _cellDotsX - 1;
        _windowBottom = _windowTop + (long)visible.Height * _cellDotsY - 1;
        _masks = new int[Math.Max(0, visible.Width * visible.Height)];
        _styles = new Style[_masks.Length];
    }

    /// <summary>The width of the dot grid: the canvas width times the marker's dots per cell.</summary>
    public int DotWidth => (int)Math.Min(_dotWidth, int.MaxValue);

    /// <summary>The height of the dot grid: the canvas height times the marker's dots per cell.</summary>
    public int DotHeight => (int)Math.Min(_dotHeight, int.MaxValue);

    /// <summary>A dot. Points outside the bounds, NaN and infinite values aren't drawn.</summary>
    public void Point(double x, double y, Style style = default)
    {
        if (!InBounds(x, y)) return;
        var (dx, dy) = ToDot(x, y);
        SetDot((long)Math.Round(dx), (long)Math.Round(dy), style);
    }

    /// <summary>Dots, as for <see cref="Point"/>.</summary>
    public void Points(IEnumerable<(double X, double Y)> points, Style style = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        foreach (var (x, y) in points) Point(x, y, style);
    }

    /// <summary>A line between two points. The part outside the bounds isn't drawn.</summary>
    public void Line(double x1, double y1, double x2, double y2, Style style = default)
    {
        if (!_valid || !double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2)) return;
        if (!Clip(ref x1, ref y1, ref x2, ref y2, _xMin, _xMax, _yMin, _yMax)) return;

        var (ax, ay) = ToDot(x1, y1);
        var (bx, by) = ToDot(x2, y2);
        if (!Clip(ref ax, ref ay, ref bx, ref by, _windowLeft, _windowRight, _windowTop, _windowBottom)) return;
        Bresenham((long)Math.Round(ax), (long)Math.Round(ay), (long)Math.Round(bx), (long)Math.Round(by), style);
    }

    /// <summary>A rectangle outline. (<paramref name="x"/>, <paramref name="y"/>) is its bottom-left corner.</summary>
    public void Rectangle(double x, double y, double width, double height, Style style = default)
    {
        Line(x, y, x + width, y, style);
        Line(x + width, y, x + width, y + height, style);
        Line(x + width, y + height, x, y + height, style);
        Line(x, y + height, x, y, style);
    }

    /// <summary>
    /// A circle outline, the radius in coordinate units on both axes, so it looks round only when the bounds
    /// match the canvas's aspect.
    /// </summary>
    public void Circle(double x, double y, double radius, Style style = default)
    {
        if (!_valid || !double.IsFinite(radius) || radius < 0) return;
        if (radius == 0)
        {
            Point(x, y, style);
            return;
        }

        // Enough segments that each one is about a dot long
        var dots = Math.Max(radius / (_xMax - _xMin) * _dotWidth, radius / (_yMax - _yMin) * _dotHeight);
        var segments = (int)Math.Clamp(Math.Ceiling(2 * Math.PI * dots), 8, 1024);
        var (px, py) = (x + radius, y);
        for (var i = 1; i <= segments; i++)
        {
            var angle = 2 * Math.PI * i / segments;
            var (nx, ny) = (x + radius * Math.Cos(angle), y + radius * Math.Sin(angle));
            Line(px, py, nx, ny, style);
            (px, py) = (nx, ny);
        }
    }

    /// <summary>Text with its left end at the cell holding (<paramref name="x"/>, <paramref name="y"/>), drawn above the dots.</summary>
    public void Print(double x, double y, Line text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!InBounds(x, y)) return;
        var (dx, dy) = ToDot(x, y);
        var cellX = _area.X + (long)Math.Round(dx) / _cellDotsX;
        var cellY = _area.Y + (long)Math.Round(dy) / _cellDotsY;
        if (_visible.Contains((int)cellX, (int)cellY)) _labels.Add(((int)cellX, (int)cellY, text));
    }

    /// <summary>Draw the dots, then the text, with each style patched over <paramref name="style"/>.</summary>
    internal void Render(Buffer buf, Style style)
    {
        for (var row = 0; row < _visible.Height; row++)
        for (var col = 0; col < _visible.Width; col++)
        {
            var i = row * _visible.Width + col;
            if (_masks[i] != 0) buf.SetString(_visible.X + col, _visible.Y + row, Glyph(_masks[i]), style.Patch(_styles[i]), 1);
        }

        foreach (var (x, y, text) in _labels)
        {
            var spans = text.Spans.Select(s => new Span(Sanitizer.SingleLine(s.Content ?? ""), style.Patch(s.Style))).ToList();
            buf.SetLine(x, y, new Line(spans), _visible.Right - x);
        }
    }

    private bool InBounds(double x, double y) =>
        _valid && x >= _xMin && x <= _xMax && y >= _yMin && y <= _yMax;

    // Continuous dot coordinates: (xMin, yMax) is the top-left dot, (xMax, yMin) the bottom-right one
    private (double X, double Y) ToDot(double x, double y) =>
        ((x - _xMin) / (_xMax - _xMin) * (_dotWidth - 1), (_yMax - y) / (_yMax - _yMin) * (_dotHeight - 1));

    private void SetDot(long dx, long dy, Style style)
    {
        if (dx < _windowLeft || dx > _windowRight || dy < _windowTop || dy > _windowBottom) return;
        var col = (int)((dx - _windowLeft) / _cellDotsX);
        var row = (int)((dy - _windowTop) / _cellDotsY);
        var i = row * _visible.Width + col;
        var subX = (int)((dx - _windowLeft) % _cellDotsX);
        var subY = (int)((dy - _windowTop) % _cellDotsY);
        _masks[i] |= _marker switch
        {
            Marker.Braille => BrailleBits[subX, subY],
            Marker.HalfBlock => subY == 0 ? 1 : 2,
            _ => 1,
        };
        _styles[i] = style;
    }

    private void Bresenham(long x0, long y0, long x1, long y1, Style style)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            SetDot(x0, y0, style);
            if (x0 == x1 && y0 == y1) return;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private string Glyph(int mask) => _marker switch
    {
        Marker.Braille => BrailleGlyphs[mask],
        Marker.HalfBlock => mask switch { 1 => "▀", 2 => "▄", _ => "█" },
        Marker.Block => "█",
        _ => "•",
    };

    /// <summary>Liang–Barsky: cut the segment to the box; false if none of it is inside.</summary>
    private static bool Clip(ref double x1, ref double y1, ref double x2, ref double y2,
        double left, double right, double top, double bottom)
    {
        double t0 = 0, t1 = 1;
        var dx = x2 - x1;
        var dy = y2 - y1;
        if (!Edge(-dx, x1 - left) || !Edge(dx, right - x1) || !Edge(-dy, y1 - top) || !Edge(dy, bottom - y1)) return false;

        (x1, y1, x2, y2) = (x1 + t0 * dx, y1 + t0 * dy, x1 + t1 * dx, y1 + t1 * dy);
        return true;

        bool Edge(double p, double q)
        {
            if (p == 0) return q >= 0;
            var t = q / p;
            if (p < 0)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }
    }
}
