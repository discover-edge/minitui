namespace MiniTui;

/// <summary>
/// A bar chart of recent values in one row (or more) of <c>▁▂▃▄▅▆▇█</c>, newest at the right.
/// </summary>
public sealed class SparklineBuilder
{
    private const string Levels = "▁▂▃▄▅▆▇█";

    private IReadOnlyList<double> _data = [];
    private double? _max;
    private BlockBuilder? _block;
    private Style? _style;

    /// <summary>The values, oldest first. Negative and NaN values draw as empty.</summary>
    public SparklineBuilder Data(IReadOnlyList<double> data) { _data = data ?? []; return this; }

    /// <summary>The value of a full bar. By default, the largest value.</summary>
    public SparklineBuilder Max(double? max) { _max = max; return this; }

    /// <summary>Draw a block around the sparkline.</summary>
    public SparklineBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's gauge style.</summary>
    public SparklineBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the sparkline.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Draw(buf, area) ?? area;
        if (inner.IsEmpty || _data.Count == 0) return;

        var max = _max ?? _data.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
        if (!(max > 0)) max = 1;

        var style = Theme.Current.GaugeFilled.Patch(_style ?? default);
        var count = Math.Min(_data.Count, inner.Width);
        var first = _data.Count - count;
        var x0 = inner.Right - count;
        for (var i = 0; i < count; i++)
        {
            var value = _data[first + i];
            if (!(value > 0)) continue;
            var level = (long)Math.Round(Math.Min(value / max, 1) * inner.Height * 8);

            // Rows count up from the bottom; skip the ones below the buffer
            var firstRow = (int)Math.Max(0, (long)inner.Y + inner.Height - buf.Height);
            level -= 8L * firstRow;
            for (var row = firstRow; row < inner.Height && level > 0; row++, level -= 8)
            {
                var glyph = Levels[(int)Math.Min(level, 8) - 1];
                buf.SetString(x0 + i, inner.Bottom - 1 - row, glyph.ToString(), style, 1);
            }
        }
    }
}
