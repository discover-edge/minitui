using System.Globalization;

namespace MiniTui;

/// <summary>A bar in a <see cref="BarChartBuilder"/> (1.x spec §8.5).</summary>
/// <param name="Label">The text under (or beside) the bar.</param>
/// <param name="Value">The bar's value. Negative and NaN values draw as an empty bar.</param>
public sealed record Bar(string Label, double Value)
{
    /// <summary>The bar's style, or null for the theme's gauge style.</summary>
    public Style? Style { get; init; }

    /// <summary>The value shown with the bar, or null for <see cref="Value"/> formatted; <c>""</c> hides it.</summary>
    public string? ValueText { get; init; }
}

/// <summary>Labelled bars, vertical (growing up) or horizontal (growing right), in eighths of a cell (1.x spec §8.5).</summary>
public sealed class BarChartBuilder
{
    private const string VerticalLevels = "▁▂▃▄▅▆▇█";
    private const string HorizontalLevels = "▏▎▍▌▋▊▉█";

    private IReadOnlyList<Bar> _bars = [];
    private Direction _direction = MiniTui.Direction.Vertical;
    private int _barWidth = 3;
    private int _barGap = 1;
    private double? _max;
    private string _valueFormat = "0.##";
    private BlockBuilder? _block;
    private Style? _style;

    /// <summary>The bars, left to right (or top to bottom).</summary>
    public BarChartBuilder Bars(IReadOnlyList<Bar> bars) { _bars = bars ?? []; return this; }

    /// <summary>The bars, left to right (or top to bottom).</summary>
    public BarChartBuilder Bars(params Bar[] bars) => Bars((IReadOnlyList<Bar>)bars);

    /// <summary>Vertical bars grow up; horizontal ones grow right. Default <see cref="Direction.Vertical"/>.</summary>
    public BarChartBuilder Direction(Direction direction) { _direction = direction; return this; }

    /// <summary>The width of a vertical bar in cells. Default 3.</summary>
    public BarChartBuilder BarWidth(int width) { _barWidth = Math.Max(1, width); return this; }

    /// <summary>The cells (or rows) between bars. Default 1.</summary>
    public BarChartBuilder BarGap(int gap) { _barGap = Math.Max(0, gap); return this; }

    /// <summary>The value of a full bar. By default, the largest value.</summary>
    public BarChartBuilder Max(double? max) { _max = max; return this; }

    /// <summary>The .NET numeric format of the values, in the invariant culture. Default <c>"0.##"</c>.</summary>
    public BarChartBuilder ValueFormat(string format) { _valueFormat = format ?? "0.##"; return this; }

    /// <summary>Draw a block around the chart.</summary>
    public BarChartBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's text style, for the labels and values.</summary>
    public BarChartBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the chart.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Draw(buf, area) ?? area;
        if (inner.Intersect(buf.Area).IsEmpty) return;

        var bars = _bars.Where(b => b is not null).ToList();
        if (bars.Count == 0) return;
        var max = _max ?? bars.Select(b => b.Value).Where(double.IsFinite).DefaultIfEmpty(0).Max();
        if (!(max > 0) || !double.IsFinite(max)) max = 1;

        var theme = Theme.Current;
        var text = theme.Text.Patch(_style ?? default);
        if (_direction == MiniTui.Direction.Vertical) DrawVertical(buf, inner, bars, max, theme, text);
        else DrawHorizontal(buf, inner, bars, max, theme, text);
    }

    private void DrawVertical(Buffer buf, Rect inner, List<Bar> bars, double max, Theme theme, Style text)
    {
        var labelRow = inner.Height >= 2 ? 1 : 0;
        var height = inner.Height - labelRow;
        var bottom = inner.Y + height - 1;
        for (var i = 0; i < bars.Count; i++)
        {
            var x = inner.X + (long)i * (_barWidth + _barGap);
            if (x + _barWidth > inner.Right) break;
            var bar = bars[i];
            var style = theme.GaugeFilled.Patch(bar.Style ?? default);
            var eighths = Eighths(bar.Value, max, height);

            // Rows count up from the bottom; skip the ones below the buffer
            var firstRow = (int)Math.Clamp((long)bottom + 1 - buf.Height, 0, height);
            for (var row = firstRow; row < height && eighths - 8L * row > 0 && bottom - row >= 0; row++)
            {
                var glyph = Glyph(VerticalLevels, (int)Math.Min(eighths - 8L * row, 8), "▄");
                if (glyph is null) continue;
                for (var dx = 0; dx < _barWidth; dx++) buf.SetString((int)x + dx, bottom - row, glyph, style, 1);
            }

            var value = ValueText(bar);
            var valueWidth = UnicodeWidth.Of(value);
            if (valueWidth > 0 && valueWidth <= _barWidth && height > 0)
            {
                var full = eighths >= 8;
                WidgetText.DrawString(buf, (int)x, bottom, value, _barWidth, full ? style.Reversed() : text, Alignment.Center);
            }
            if (labelRow > 0)
                WidgetText.DrawString(buf, (int)x, inner.Bottom - 1, Sanitizer.SingleLine(bar.Label ?? ""), _barWidth, text, Alignment.Center);
        }
    }

    private void DrawHorizontal(Buffer buf, Rect inner, List<Bar> bars, double max, Theme theme, Style text)
    {
        var labels = bars.Select(b => Sanitizer.SingleLine(b.Label ?? "")).ToList();
        var values = bars.Select(ValueText).ToList();
        var labelWidth = Math.Min(labels.Max(l => UnicodeWidth.Of(l)), inner.Width / 3);
        var barX = inner.X + labelWidth + (labelWidth > 0 ? 1 : 0);

        // Room for the widest value after the longest bar, if that leaves any bar
        var valueWidth = values.Max(v => UnicodeWidth.Of(v));
        var space = inner.Right - barX;
        var showValues = valueWidth > 0 && space - valueWidth - 1 >= 1;
        var length = showValues ? space - valueWidth - 1 : space;
        if (length <= 0) return;

        for (var i = 0; i < bars.Count; i++)
        {
            var y = inner.Y + (long)i * (1 + _barGap);
            if (y >= inner.Bottom) break;
            var bar = bars[i];
            var style = theme.GaugeFilled.Patch(bar.Style ?? default);
            if (labelWidth > 0) WidgetText.DrawString(buf, inner.X, (int)y, labels[i], labelWidth, text);

            var eighths = Eighths(bar.Value, max, length);
            var cells = (int)((eighths + 7) / 8);
            for (var col = 0; col < cells && (long)barX + col < buf.Width; col++)
            {
                var glyph = Glyph(HorizontalLevels, (int)Math.Min(eighths - 8L * col, 8), "▌");
                if (glyph is not null) buf.SetString(barX + col, (int)y, glyph, style, 1);
            }
            if (showValues && values[i].Length > 0) WidgetText.DrawString(buf, barX + cells + 1, (int)y, values[i], valueWidth, text);
        }
    }

    // The bar's length in eighths of a cell, of cells cells in all
    private static long Eighths(double value, double max, int cells) =>
        value > 0 ? (long)Math.Round(Math.Min(value / max, 1) * cells * 8) : 0;

    // Basic glyphs round to the nearest of empty, the half glyph and █
    private static string? Glyph(string levels, int eighths, string half) => !Glyphs.Basic
        ? levels[eighths - 1].ToString()
        : eighths switch { <= 2 => null, <= 5 => half, _ => "█" };

    private string ValueText(Bar bar) => Sanitizer.SingleLine(bar.ValueText
        ?? (double.IsFinite(bar.Value) ? bar.Value.ToString(_valueFormat, CultureInfo.InvariantCulture) : ""));
}
