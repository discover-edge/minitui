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

/// <summary>Several bars under one label, side by side or stacked into one bar (1.x spec §19.3).</summary>
/// <param name="Label">The text under (or beside) the group.</param>
/// <param name="Bars">The group's bars. Bar i of every group is series i, named in the legend by the first group's.</param>
public sealed record BarGroup(string Label, IReadOnlyList<Bar> Bars);

/// <summary>
/// Labelled bars, vertical (growing up) or horizontal (growing right), in eighths of a cell (1.x spec §8.5), or groups
/// of them side by side or stacked, with a legend of their series (§19.3).
/// </summary>
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
    private IReadOnlyList<BarGroup>? _groups;
    private bool _stacked;
    private int _groupGap = 2;
    private LegendPosition _legend = LegendPosition.TopRight;

    /// <summary>The bars, left to right (or top to bottom).</summary>
    public BarChartBuilder Bars(IReadOnlyList<Bar> bars) { _bars = bars ?? []; _groups = null; return this; }

    /// <summary>The bars, left to right (or top to bottom).</summary>
    public BarChartBuilder Bars(params Bar[] bars) => Bars((IReadOnlyList<Bar>)bars);

    /// <summary>
    /// Draw groups of bars instead (1.x spec §19.3): side by side with <see cref="GroupGap"/> between groups, or one
    /// stacked bar per group with <see cref="Stacked"/>. A bar without a style takes <see cref="Theme.ChartSeries"/> by
    /// its index in the group.
    /// </summary>
    public BarChartBuilder Groups(IReadOnlyList<BarGroup> groups) { _groups = groups ?? []; return this; }

    /// <inheritdoc cref="Groups(IReadOnlyList{BarGroup})"/>
    public BarChartBuilder Groups(params BarGroup[] groups) => Groups((IReadOnlyList<BarGroup>)groups);

    /// <summary>Stack each group's bars into one bar, bottom up (left to right), instead of side by side. Off by default.</summary>
    public BarChartBuilder Stacked(bool stacked = true) { _stacked = stacked; return this; }

    /// <summary>The cells (or rows) between groups drawn side by side. Default 2.</summary>
    public BarChartBuilder GroupGap(int gap) { _groupGap = Math.Max(0, gap); return this; }

    /// <summary>Where the legend of the groups' series goes. Default <see cref="LegendPosition.TopRight"/>.</summary>
    public BarChartBuilder Legend(LegendPosition position) { _legend = position; return this; }

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
    public void Draw(CellBuffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Draw(buf, area) ?? area;
        if (inner.Intersect(buf.Area).IsEmpty) return;

        var theme = Theme.Current;
        var text = theme.Text.Patch(_style ?? default);
        var columns = Columns(theme);
        if (columns.Count == 0) return;
        var max = _max ?? columns.Max(c => c.Total);
        if (!(max > 0) || !double.IsFinite(max)) max = 1;

        if (_direction == MiniTui.Direction.Vertical) DrawVertical(buf, inner, columns, max, text);
        else DrawHorizontal(buf, inner, columns, max, text);

        if (_groups is { } groups && groups.FirstOrDefault(g => g is not null)?.Bars is { } first)
        {
            var entries = first.Select((b, i) => (b?.Label ?? "", Glyphs.Basic ? "#" : "█", SeriesStyle(theme, i, b)));
            ChartLegend.Draw(buf, inner, _legend, entries, theme, text);
        }
    }

    // One drawn bar: its segments (one for a plain bar), the label under it, its value text, and the gap after it
    private sealed record Column(List<(double Value, Style Style)> Segments, string Label, string Value, int GapAfter)
    {
        public double Total => Segments.Sum(s => s.Value);

        /// <summary>For the first bar of a group drawn side by side: how many bars the group has (its label spans them).</summary>
        public int GroupSize { get; init; }

        /// <summary>A group stacked into one bar: its value text is its total, in its top cell.</summary>
        public bool IsStack { get; init; }
    }

    // Plain bars; each group's bars side by side (labelled once, under the group); or each group as one stacked bar
    private List<Column> Columns(Theme theme)
    {
        static double Positive(double v) => v > 0 && double.IsFinite(v) ? v : 0;
        if (_groups is null)
        {
            return [.. _bars.Where(b => b is not null).Select(b =>
                new Column([(Positive(b.Value), theme.GaugeFilled.Patch(b.Style ?? default))], Sanitizer.SingleLine(b.Label ?? ""), ValueText(b), _barGap))];
        }

        var columns = new List<Column>();
        foreach (var group in _groups.Where(g => g?.Bars is not null))
        {
            var bars = group.Bars.Select((b, i) => (Bar: b, Index: i)).Where(e => e.Bar is not null).ToList();
            var label = Sanitizer.SingleLine(group.Label ?? "");
            if (_stacked)
            {
                var segments = bars.Select(e => (Positive(e.Bar.Value), SeriesStyle(theme, e.Index, e.Bar))).ToList();
                var total = segments.Sum(x => x.Item1);
                columns.Add(new Column(segments, label, total.ToString(_valueFormat, CultureInfo.InvariantCulture), _barGap) { IsStack = true });
                continue;
            }
            for (var j = 0; j < bars.Count; j++)
            {
                var (bar, index) = bars[j];
                // The group's label goes with its first bar, spread over the whole group when drawn
                columns.Add(new Column([(Positive(bar.Value), SeriesStyle(theme, index, bar))], j == 0 ? label : "", ValueText(bar),
                    j == bars.Count - 1 ? _groupGap : _barGap) { GroupSize = j == 0 ? bars.Count : 0 });
            }
        }
        return columns;
    }

    private static Style SeriesStyle(Theme theme, int index, Bar? bar) =>
        theme.Text.Patch(ChartBuilder.SeriesStyle(theme, index)).Patch(bar?.Style ?? default);

    // A bar's segments as cumulative ends in eighths: whole cells between segments, eighths only at the top (§19.3)
    private static List<(long End, Style Style)> Ends(Column column, double max, int cells)
    {
        var drawn = column.Segments.Where(s => s.Value > 0).ToList();
        var ends = new List<(long, Style)>();
        if (drawn.Count == 0) return ends;
        var top = Eighths(drawn.Sum(s => s.Value), max, cells);
        double sum = 0;
        for (var i = 0; i < drawn.Count; i++)
        {
            sum += drawn[i].Value;
            var end = i == drawn.Count - 1 ? top : Math.Min((long)Math.Round(Math.Min(sum / max, 1) * cells) * 8, top / 8 * 8);
            ends.Add((end, drawn[i].Style));
        }
        return ends;
    }

    private void DrawVertical(CellBuffer buf, Rect inner, List<Column> columns, double max, Style text)
    {
        var labelRow = inner.Height >= 2 ? 1 : 0;
        var height = inner.Height - labelRow;
        var bottom = inner.Y + height - 1;
        var x = (long)inner.X;
        var above = new List<(long From, long To)>();
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var groupWidth = column.GroupSize > 1 ? GroupWidth(columns, i) : _barWidth;
            if (x + groupWidth > inner.Right) break;
            var ends = Ends(column, max, height);
            var top = ends.Count > 0 ? ends[^1].End : 0;

            // Rows count up from the bottom; skip the ones below the buffer
            var firstRow = (int)Math.Clamp((long)bottom + 1 - buf.Height, 0, height);
            for (var row = firstRow; row < height && top - 8L * row > 0 && bottom - row >= 0; row++)
            {
                var glyph = Glyph(VerticalLevels, (int)Math.Min(top - 8L * row, 8), "▄");
                if (glyph is null) continue;
                var style = ends.First(e => e.End > 8L * row).Style;
                for (var dx = 0; dx < _barWidth; dx++) buf.SetString((int)x + dx, bottom - row, glyph, style, 1);
            }

            var valueWidth = UnicodeWidth.Of(column.Value);
            if (valueWidth > 0 && valueWidth <= _barWidth && height > 0)
            {
                // 1.x spec §23.2: inside a bar of a whole cell or more; above a shorter one, so its glyph stays whole;
                // in the bottom row of an empty one. The last two take the bar's color.
                var color = text.Patch(new Style((ends.Count > 0 ? ends[^1].Style : column.Segments[0].Style).Fg));
                if (top >= 8)
                {
                    // A stack's total goes in its top whole cell
                    var row = column.IsStack ? (int)(top / 8) - 1 : 0;
                    WidgetText.DrawString(buf, (int)x, bottom - row, column.Value, _barWidth, ends.First(e => e.End > 8L * row).Style.Reversed(), Alignment.Center);
                }
                else if (top == 0)
                {
                    WidgetText.DrawString(buf, (int)x, bottom, column.Value, _barWidth, color, Alignment.Center);
                }
                else if (height >= 2)
                {
                    // Short bars' neighbours share the row above: a value that would touch one already there is left out
                    var from = x + (_barWidth - valueWidth) / 2;
                    var to = from + valueWidth;
                    if (!above.Any(v => from <= v.To && v.From <= to))
                    {
                        WidgetText.DrawString(buf, (int)x, bottom - 1, column.Value, _barWidth, color, Alignment.Center);
                        above.Add((from, to));
                    }
                }
            }
            if (labelRow > 0 && (column.GroupSize > 0 || _groups is null || _stacked))
                WidgetText.DrawString(buf, (int)x, inner.Bottom - 1, column.Label, groupWidth, text, Alignment.Center);
            x += _barWidth + column.GapAfter;
        }
    }

    // The width of the group starting at column i, from its bars and the gaps inside it
    private int GroupWidth(List<Column> columns, int i)
    {
        var width = 0;
        for (var j = i; j < i + columns[i].GroupSize && j < columns.Count; j++) width += _barWidth + (j < i + columns[i].GroupSize - 1 ? columns[j].GapAfter : 0);
        return width;
    }

    private static void DrawHorizontal(CellBuffer buf, Rect inner, List<Column> columns, double max, Style text)
    {
        var labelWidth = Math.Min(columns.Max(c => UnicodeWidth.Of(c.Label)), inner.Width / 3);
        var barX = inner.X + labelWidth + (labelWidth > 0 ? 1 : 0);

        // Room for the widest value after the longest bar, if that leaves any bar
        var valueWidth = columns.Max(c => UnicodeWidth.Of(c.Value));
        var space = inner.Right - barX;
        var showValues = valueWidth > 0 && space - valueWidth - 1 >= 1;
        var length = showValues ? space - valueWidth - 1 : space;
        if (length <= 0) return;

        var y = (long)inner.Y;
        foreach (var column in columns)
        {
            if (y >= inner.Bottom) break;
            if (labelWidth > 0) WidgetText.DrawString(buf, inner.X, (int)y, column.Label, labelWidth, text);

            var ends = Ends(column, max, length);
            var top = ends.Count > 0 ? ends[^1].End : 0;
            var cells = (int)((top + 7) / 8);
            for (var col = 0; col < cells && (long)barX + col < buf.Width; col++)
            {
                var glyph = Glyph(HorizontalLevels, (int)Math.Min(top - 8L * col, 8), "▌");
                if (glyph is not null) buf.SetString(barX + col, (int)y, glyph, ends.First(e => e.End > 8L * col).Style, 1);
            }
            if (showValues && column.Value.Length > 0) WidgetText.DrawString(buf, barX + cells + 1, (int)y, column.Value, valueWidth, text);
            y += 1 + column.GapAfter;
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
