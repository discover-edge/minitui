namespace MiniTui;

/// <summary>
/// Fluent builder for creating layouts with named regions (§7).
/// </summary>
/// <example>
/// <code>
/// var layout = Layout.Vertical(screen)
///     .Add("header", Constraint.Length(3))
///     .Add("body", Constraint.Fill())
///     .Add("status", Constraint.Length(1))
///     .Build();
/// var body = layout["body"];
/// </code>
/// </example>
public class Layout
{
    private readonly Rect _area;
    private readonly Direction _direction;
    private readonly List<(string Name, Constraint Constraint)> _regions = [];
    private int _spacing;
    private Margin _margin;
    private Flex _flex;
    private Dictionary<string, Rect>? _built;

    private Layout(Rect area, Direction direction)
    {
        _area = area;
        _direction = direction;
    }

    /// <summary>
    /// Create a vertical layout (rows stacked top to bottom).
    /// </summary>
    public static Layout Vertical(Rect area) => new(area, Direction.Vertical);

    /// <summary>
    /// Create a horizontal layout (columns side by side).
    /// </summary>
    public static Layout Horizontal(Rect area) => new(area, Direction.Horizontal);

    /// <summary>
    /// Add a named region.
    /// </summary>
    /// <exception cref="ArgumentException">A region with this name was already added.</exception>
    public Layout Add(string name, Constraint constraint)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(constraint);
        if (_regions.Exists(r => r.Name == name))
            throw new ArgumentException($"The layout already has a region named '{name}'.", nameof(name));
        _regions.Add((name, constraint));
        _built = null;
        return this;
    }

    /// <summary>
    /// Add a region with a fixed size.
    /// </summary>
    [Obsolete("Use Add(name, Constraint.Length(size)) instead.")]
    public Layout Add(string name, int size) => Add(name, Constraint.Length(Math.Max(0, size)));

    /// <summary>
    /// Add a region that fills remaining space.
    /// </summary>
    [Obsolete("Use Add(name, Constraint.Fill()) instead.")]
    public Layout AddFill(string name) => Add(name, Constraint.Fill());

    /// <summary>
    /// Leave <paramref name="cells"/> empty cells between regions. Reduced if it doesn't fit.
    /// </summary>
    public Layout Spacing(int cells)
    {
        _spacing = Math.Max(0, cells);
        _built = null;
        return this;
    }

    /// <summary>
    /// Lay the regions out inside <paramref name="margin"/>.
    /// </summary>
    public Layout Margin(Margin margin)
    {
        _margin = margin;
        _built = null;
        return this;
    }

    /// <summary>
    /// Where space goes when no region can grow into it. The default is <see cref="MiniTui.Flex.Start"/>.
    /// </summary>
    public Layout Flex(Flex flex)
    {
        _flex = flex;
        _built = null;
        return this;
    }

    /// <summary>
    /// Compute the regions. Calling it again without changes does nothing.
    /// </summary>
    public Layout Build()
    {
        if (_built is not null) return this;

        var constraints = _regions.ConvertAll(r => r.Constraint);
        var rects = Compute(_area.Inner(_margin), _direction, constraints, _spacing, _flex);
        _built = new Dictionary<string, Rect>(_regions.Count);
        for (var i = 0; i < _regions.Count; i++) _built[_regions[i].Name] = rects[i];
        return this;
    }

    /// <summary>
    /// Get a named region. Builds the layout if needed.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No region has this name.</exception>
    public Rect this[string name]
    {
        get
        {
            if (TryGet(name, out var rect)) return rect;
            throw new KeyNotFoundException($"The layout has no region named '{name}'.");
        }
    }

    /// <summary>
    /// Try to get a named region. Builds the layout if needed.
    /// </summary>
    public bool TryGet(string name, out Rect rect)
    {
        Build();
        return _built!.TryGetValue(name, out rect);
    }

    /// <summary>
    /// Split <paramref name="area"/> into one rect per constraint, in order.
    /// </summary>
    public static Rect[] Split(Rect area, Direction direction, params Constraint[] constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        return Compute(area, direction, constraints, spacing: 0, MiniTui.Flex.Start);
    }

    private static Rect[] Compute(Rect area, Direction direction, IReadOnlyList<Constraint> constraints, int spacing, Flex flex)
    {
        var vertical = direction == Direction.Vertical;
        var slots = LayoutSolver.Solve(vertical ? area.Height : area.Width, constraints, spacing, flex);
        var rects = new Rect[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var (offset, size) = slots[i];
            rects[i] = vertical
                ? new Rect(area.X, area.Y + offset, area.Width, size)
                : new Rect(area.X + offset, area.Y, size, area.Height);
        }
        return rects;
    }
}
