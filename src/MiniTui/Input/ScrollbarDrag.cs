namespace MiniTui;

/// <summary>
/// Dragging a vertical scrollbar's thumb and paging on its track (1.x spec §14.4), for the states whose widgets
/// draw one. The widget records the bar while drawing; the state passes its mouse events here first.
/// </summary>
internal sealed class ScrollbarDrag
{
    private Rect _bar;
    private int _content;
    private int _viewport;
    private int? _grab;

    /// <summary>True from a press on the thumb until the release.</summary>
    public bool IsDragging => _grab is not null;

    /// <summary>
    /// Called by widgets while drawing: where the bar was drawn, or an empty rect when it wasn't. A drag in
    /// progress continues across draws; while the bar isn't drawn, drags do nothing.
    /// </summary>
    public void Track(Rect bar, int content, int viewport)
    {
        var shown = viewport > 0 && content > viewport && !bar.IsEmpty;
        _bar = shown ? bar : default;
        _content = content;
        _viewport = viewport;
    }

    /// <summary>
    /// A left press on the thumb starts a drag and one on the track pages towards it; drags move the thumb under
    /// the pointer; the release ends a drag. <paramref name="offset"/> is updated.
    /// </summary>
    /// <returns>True if the event was for the scrollbar.</returns>
    public bool Handle(MouseEvent mouse, ref int offset)
    {
        switch (mouse.Action)
        {
            case MouseAction.Press:
                _grab = null;
                if (mouse.Button != MouseButton.Left || _bar.IsEmpty || !_bar.Contains(mouse.X, mouse.Y)) return false;
                var (start, length) = ScrollbarBuilder.Thumb(_bar.Height, _content, _viewport, offset);
                var at = mouse.Y - _bar.Y;
                if (at < start) offset = Clamp(offset - _viewport);
                else if (at >= start + length) offset = Clamp(offset + _viewport);
                else _grab = at - start;
                return true;
            case MouseAction.Drag when _grab is { } grab && mouse.Button == MouseButton.Left && !_bar.IsEmpty:
                offset = OffsetAt(mouse.Y - _bar.Y - grab);
                return true;
            case MouseAction.Release when _grab is not null:
                _grab = null;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The offset whose thumb starts nearest <paramref name="thumbStart"/>.</summary>
    private int OffsetAt(int thumbStart)
    {
        var (_, length) = ScrollbarBuilder.Thumb(_bar.Height, _content, _viewport, 0);
        var free = _bar.Height - length;
        if (free <= 0) return 0;
        var max = Math.Max(0, _content - _viewport);
        return (int)Math.Round((double)Math.Clamp(thumbStart, 0, free) * max / free, MidpointRounding.AwayFromZero);
    }

    private int Clamp(int offset) => Math.Clamp(offset, 0, Math.Max(0, _content - _viewport));
}
