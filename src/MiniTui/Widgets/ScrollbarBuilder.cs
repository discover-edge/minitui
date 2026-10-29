namespace MiniTui;

/// <summary>
/// A scrollbar, drawn along the right edge (vertical) or bottom edge (horizontal) of its area.
/// Hidden when the content fits in the viewport.
/// </summary>
public sealed class ScrollbarBuilder
{
    private bool _horizontal;
    private int _contentLength;
    private int _position;
    private int _viewportLength;

    /// <summary>Draw along the right edge. This is the default.</summary>
    public ScrollbarBuilder Vertical() { _horizontal = false; return this; }

    /// <summary>Draw along the bottom edge.</summary>
    public ScrollbarBuilder Horizontal() { _horizontal = true; return this; }

    /// <summary>The total number of rows (or columns) of content.</summary>
    public ScrollbarBuilder ContentLength(int length) { _contentLength = length; return this; }

    /// <summary>The first visible row (or column).</summary>
    public ScrollbarBuilder Position(int offset) { _position = offset; return this; }

    /// <summary>How many rows (or columns) are visible.</summary>
    public ScrollbarBuilder ViewportLength(int length) { _viewportLength = length; return this; }

    /// <summary>Draw the scrollbar.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        if (area.IsEmpty || _contentLength <= _viewportLength || _viewportLength < 0) return;

        var track = _horizontal ? area.Width : area.Height;
        var (thumbStart, thumbLength) = Thumb(track, _contentLength, _viewportLength, _position);

        var theme = Theme.Current;
        var line = _horizontal
            ? new Rect(area.X, area.Y + area.Height - 1, area.Width, 1)
            : new Rect(area.X + area.Width - 1, area.Y, 1, area.Height);
        var clip = line.Intersect(buf.Area);
        for (var y = clip.Y; y < clip.Bottom; y++)
        for (var x = clip.X; x < clip.Right; x++)
        {
            var i = _horizontal ? x - area.X : y - area.Y;
            var onThumb = i >= thumbStart && i < thumbStart + thumbLength;
            buf.SetString(x, y, onThumb ? "█" : "░", onThumb ? theme.ScrollbarThumb : theme.ScrollbarTrack, 1);
        }
    }

    /// <summary>Where the thumb sits on a track of <paramref name="track"/> cells.</summary>
    internal static (int Start, int Length) Thumb(int track, int content, int viewport, int position)
    {
        if (track <= 0 || content <= 0) return (0, 0);
        var length = (int)Math.Clamp((long)track * viewport / content, 1, track);
        var maxPosition = Math.Max(1, content - viewport);
        var start = (int)((long)(track - length) * Math.Clamp(position, 0, maxPosition) / maxPosition);
        return (start, length);
    }
}
