namespace MiniTui;

/// <summary>Which pane of a split: <see cref="SplitState.FixedPane"/>.</summary>
public enum SplitPane
{
    /// <summary>The left or top pane.</summary>
    First,

    /// <summary>The right or bottom pane.</summary>
    Second,
}

/// <summary>
/// The divider position of a split (1.x spec §14.3), drawn with <see cref="Widget.Split"/>. The panes are sized by
/// <see cref="Ratio"/>, so they keep their proportions when the terminal is resized, or with one pane at a fixed
/// size in cells (<see cref="FixedSize"/>, a sidebar), the other taking the rest. The user drags the divider, or
/// moves it with Alt+Shift and the arrows.
/// </summary>
public sealed class SplitState
{
    private double _ratio;
    private int? _fixedSize;
    private Rect _area;
    private Rect _divider;
    private int _grab;

    /// <summary>A split sized by <paramref name="ratio"/>.</summary>
    /// <param name="direction">
    /// How the panes are laid out: <see cref="Direction.Horizontal"/> side by side (a │ divider),
    /// <see cref="Direction.Vertical"/> one above the other (a ─ divider).
    /// </param>
    /// <param name="ratio">The first pane's share of the space, from 0 to 1.</param>
    public SplitState(Direction direction, double ratio = 0.5)
    {
        Direction = direction;
        Ratio = ratio;
    }

    /// <summary>A split with <paramref name="pane"/> fixed at <paramref name="cells"/>.</summary>
    public static SplitState Fixed(Direction direction, int cells, SplitPane pane = SplitPane.First) =>
        new(direction) { FixedSize = cells, FixedPane = pane };

    /// <summary>How the panes are laid out.</summary>
    public Direction Direction { get; }

    /// <summary>
    /// The first pane's share of the space (the area minus the divider), from 0 to 1, when <see cref="FixedSize"/>
    /// is null. Clamped to 0–1 when set.
    /// </summary>
    public double Ratio
    {
        get => _ratio;
        set => _ratio = double.IsNaN(value) ? 0.5 : Math.Clamp(value, 0, 1);
    }

    /// <summary>The <see cref="FixedPane"/>'s size in cells, or null to size by <see cref="Ratio"/>. Clamped to 0 or more when set.</summary>
    public int? FixedSize
    {
        get => _fixedSize;
        set => _fixedSize = value is { } v ? Math.Max(0, v) : null;
    }

    /// <summary>Which pane <see cref="FixedSize"/> applies to. Default <see cref="SplitPane.First"/>.</summary>
    public SplitPane FixedPane { get; set; }

    /// <summary>
    /// The smallest size, in cells, the user can drag the first pane to. Default 3. If the area is too small for
    /// both minimums, they're ignored.
    /// </summary>
    public int MinFirst { get; set; } = 3;

    /// <summary>The smallest size, in cells, the user can drag the second pane to. Default 3.</summary>
    public int MinSecond { get; set; } = 3;

    /// <summary>True from a left press on the divider until the release.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Raised on the UI thread when the user moves the divider (not when code sets <see cref="Ratio"/> or <see cref="FixedSize"/>).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Move the divider by <paramref name="cells"/> (negative: left or up), within the minimums, using the area from
    /// the last draw. Does nothing before the first draw.
    /// </summary>
    public void MoveBy(int cells)
    {
        if (Space(_area) <= 0) return;
        MoveTo(FirstSize(_area) + cells);
    }

    /// <summary>
    /// Alt+Shift+← → (side by side) or Alt+Shift+↑ ↓ (one above the other) move the divider a cell.
    /// </summary>
    /// <returns>True if the key was one of these.</returns>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (key.Modifiers != (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) return false;
        var step = (Direction, key.Key) switch
        {
            (Direction.Horizontal, ConsoleKey.LeftArrow) or (Direction.Vertical, ConsoleKey.UpArrow) => -1,
            (Direction.Horizontal, ConsoleKey.RightArrow) or (Direction.Vertical, ConsoleKey.DownArrow) => 1,
            _ => 0,
        };
        if (step == 0) return false;
        MoveBy(step);
        return true;
    }

    /// <summary>
    /// A left press on the divider from the last draw starts a drag; drags move the divider under the pointer,
    /// wherever it is; the release, or a press anywhere else, ends it.
    /// </summary>
    /// <returns>True if the event was for the divider.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        switch (mouse.Action)
        {
            case MouseAction.Press:
                IsDragging = mouse.Button == MouseButton.Left && !_divider.IsEmpty && _divider.Contains(mouse.X, mouse.Y);
                if (IsDragging) _grab = Along(mouse) - Along(_divider);
                return IsDragging;
            case MouseAction.Drag when IsDragging && mouse.Button == MouseButton.Left:
                MoveTo(Along(mouse) - _grab - Along(_area));
                return true;
            case MouseAction.Release when IsDragging:
                IsDragging = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Called by the widget: the panes and the divider for <paramref name="area"/>, remembered for the mouse and
    /// <see cref="MoveBy"/>. The divider is empty when the area has no room for one.
    /// </summary>
    internal (Rect First, Rect Second, Rect Divider) Layout(Rect area)
    {
        _area = area;
        if (Space(area) <= 0)
        {
            _divider = default;
            return (area, Direction == Direction.Horizontal ? area with { X = area.Right, Width = 0 } : area with { Y = area.Bottom, Height = 0 }, default);
        }
        var first = FirstSize(area);
        if (Direction == Direction.Horizontal)
        {
            _divider = new Rect(area.X + first, area.Y, 1, area.Height);
            return (area with { Width = first }, new Rect(area.X + first + 1, area.Y, area.Width - first - 1, area.Height), _divider);
        }
        _divider = new Rect(area.X, area.Y + first, area.Width, 1);
        return (area with { Height = first }, new Rect(area.X, area.Y + first + 1, area.Width, area.Height - first - 1), _divider);
    }

    private int Space(Rect area) => (Direction == Direction.Horizontal ? area.Width : area.Height) - 1;

    private int Along(MouseEvent mouse) => Direction == Direction.Horizontal ? mouse.X : mouse.Y;

    private int Along(Rect rect) => Direction == Direction.Horizontal ? rect.X : rect.Y;

    // 1.x spec §14.3: by ratio or fixed, clamped to the minimums (or, without room for both, only to the space)
    private int FirstSize(Rect area)
    {
        var space = Space(area);
        int first;
        if (_fixedSize is { } size)
            first = FixedPane == SplitPane.First ? size : space - size;
        else
            first = (int)Math.Round(_ratio * space, MidpointRounding.AwayFromZero);
        return ClampFirst(first, space);
    }

    private int ClampFirst(int first, int space)
    {
        var minFirst = Math.Max(0, MinFirst);
        var minSecond = Math.Max(0, MinSecond);
        if (minFirst + minSecond > space) return Math.Clamp(first, 0, space);
        return Math.Clamp(first, minFirst, space - minSecond);
    }

    // Store the new position the way the split is sized, and raise Changed if the panes changed
    private void MoveTo(int first)
    {
        var space = Space(_area);
        if (space <= 0) return;
        var before = FirstSize(_area);
        first = ClampFirst(first, space);
        if (first == before) return;
        if (_fixedSize is not null) _fixedSize = FixedPane == SplitPane.First ? first : space - first;
        else _ratio = (double)first / space;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
