namespace MiniTui;

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
