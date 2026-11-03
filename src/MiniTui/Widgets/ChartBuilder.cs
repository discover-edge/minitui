using System.Globalization;

namespace MiniTui;

/// <summary>How a <see cref="Dataset"/> is drawn.</summary>
public enum GraphType
{
    /// <summary>Consecutive points joined by lines.</summary>
    Line,

    /// <summary>The points only.</summary>
    Scatter,

    /// <summary>A line with the dots under it filled down to the bottom of the plot (1.x spec §19.6).</summary>
    Area,
}

/// <summary>A series of (x, y) values for a <see cref="ChartBuilder"/> (1.x spec §8.2).</summary>
/// <param name="Name">The name in the legend. An empty name leaves the dataset out of it.</param>
/// <param name="Data">The points, in drawing order. NaN and infinite values aren't drawn and break a line.</param>
public sealed record Dataset(string Name, IReadOnlyList<(double X, double Y)> Data)
{
    /// <summary>Lines or points. Default <see cref="GraphType.Line"/>.</summary>
    public GraphType Type { get; init; } = GraphType.Line;

    /// <summary>How dots are drawn. Default <see cref="Marker.Braille"/>.</summary>
    public Marker Marker { get; init; } = Marker.Braille;

    /// <summary>The dataset's style, or null for <see cref="Theme.ChartSeries"/> by the dataset's position.</summary>
    public Style? Style { get; init; }
}

/// <summary>One axis of a <see cref="ChartBuilder"/> (1.x spec §8.4).</summary>
public sealed record Axis
{
    /// <summary>The axis title, or null for none.</summary>
    public string? Title { get; init; }

    /// <summary>The value at the start of the axis, or null for the smallest value in the data.</summary>
    public double? Min { get; init; }

    /// <summary>The value at the end of the axis, or null for the largest value in the data.</summary>
    public double? Max { get; init; }

    /// <summary>
    /// Labels spread evenly along the axis, or null for <see cref="LabelCount"/> of them from <see cref="Min"/> to
    /// <see cref="Max"/>.
    /// </summary>
    public IReadOnlyList<string>? Labels { get; init; }

    /// <summary>The .NET numeric format of the default labels, in the invariant culture. Default <c>"0.##"</c>.</summary>
    public string LabelFormat { get; init; } = "0.##";

    /// <summary>How many labels to spread along the axis when <see cref="Labels"/> is null. Default 3, at least 2.</summary>
    public int LabelCount { get; init; } = 3;

    /// <summary>Formats the default labels' values, instead of <see cref="LabelFormat"/> (1.x spec §19.4).</summary>
    public Func<double, string>? Formatter { get; init; }

    /// <summary>Draw a tick on the axis line at each label: <c>┬</c> on the X axis, <c>┤</c> on the Y axis (1.x spec §19.5).</summary>
    public bool Ticks { get; init; }

    /// <summary>Draw a dotted line across the plot at each label, behind the data (1.x spec §19.5).</summary>
    public bool Grid { get; init; }

    /// <summary>
    /// An axis of times: values are seconds since the Unix epoch (<see cref="ChartTime"/>), labelled in local time
    /// with a <see cref="DateTime"/> <paramref name="format"/> in the invariant culture (1.x spec §19.4).
    /// </summary>
    public static Axis Time(string format = "HH:mm:ss") => new()
    {
        Formatter = v => double.IsFinite(v) ? ChartTime.FromValue(v).ToLocalTime().ToString(format, CultureInfo.InvariantCulture) : "",
    };

    /// <summary>
    /// An axis of durations: values are seconds, labelled with a <see cref="TimeSpan"/> <paramref name="format"/>,
    /// with <c>-</c> before negative ones (1.x spec §19.4).
    /// </summary>
    public static Axis Duration(string format = @"m\:ss") => new()
    {
        Formatter = v => !double.IsFinite(v) || Math.Abs(v) > TimeSpan.MaxValue.TotalSeconds ? ""
            : (v < 0 && Math.Round(v) != 0 ? "-" : "") + TimeSpan.FromSeconds(Math.Round(Math.Abs(v))).ToString(format, CultureInfo.InvariantCulture),
    };
}

/// <summary>Times as chart values: seconds since the Unix epoch, and durations as seconds (1.x spec §19.4).</summary>
public static class ChartTime
{
    /// <summary>A time as seconds since the Unix epoch.</summary>
    public static double ToValue(DateTimeOffset time) => (time - DateTimeOffset.UnixEpoch).TotalSeconds;

    /// <summary>The time a value made by <see cref="ToValue(DateTimeOffset)"/> stands for, in UTC.</summary>
    public static DateTimeOffset FromValue(double value) => DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(value);

    /// <summary>A duration as seconds.</summary>
    public static double ToValue(TimeSpan duration) => duration.TotalSeconds;
}

/// <summary>Where a <see cref="ChartBuilder"/> draws its legend.</summary>
public enum LegendPosition
{
    /// <summary>The top-right corner of the plot.</summary>
    TopRight,

    /// <summary>The top-left corner of the plot.</summary>
    TopLeft,

    /// <summary>The bottom-right corner of the plot.</summary>
    BottomRight,

    /// <summary>The bottom-left corner of the plot.</summary>
    BottomLeft,

    /// <summary>No legend.</summary>
    None,
}

/// <summary>A line or scatter plot of one or more datasets, with axes, labels and a legend (1.x spec §8.4).</summary>
public sealed class ChartBuilder
{
    private IReadOnlyList<Dataset> _datasets = [];
    private Axis _xAxis = new();
    private Axis _yAxis = new();
    private LegendPosition _legend = LegendPosition.TopRight;
    private BlockBuilder? _block;
    private Style? _style;

    /// <summary>The datasets, drawn in order.</summary>
    public ChartBuilder Datasets(IReadOnlyList<Dataset> datasets) { _datasets = datasets ?? []; return this; }

    /// <summary>The datasets, drawn in order.</summary>
    public ChartBuilder Datasets(params Dataset[] datasets) => Datasets((IReadOnlyList<Dataset>)datasets);

    /// <summary>The horizontal axis.</summary>
    public ChartBuilder XAxis(Axis axis) { _xAxis = axis ?? new(); return this; }

    /// <summary>The vertical axis.</summary>
    public ChartBuilder YAxis(Axis axis) { _yAxis = axis ?? new(); return this; }

    /// <summary>Where the legend goes. Default <see cref="LegendPosition.TopRight"/>.</summary>
    public ChartBuilder Legend(LegendPosition position) { _legend = position; return this; }

    /// <summary>Draw a block around the chart.</summary>
    public ChartBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's text style.</summary>
    public ChartBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the chart.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Draw(buf, area) ?? area;
        if (inner.Intersect(buf.Area).IsEmpty) return;

        var theme = Theme.Current;
        var style = theme.Text.Patch(_style ?? default);
        var datasets = _datasets.Where(d => d is not null).ToList();
        var xBounds = Bounds(_xAxis, datasets.SelectMany(d => d.Data ?? []).Select(p => p.X));
        var yBounds = Bounds(_yAxis, datasets.SelectMany(d => d.Data ?? []).Select(p => p.Y));
        var xLabels = Labels(_xAxis, xBounds);
        var yLabels = Labels(_yAxis, yBounds);
        var xTitle = Title(_xAxis);
        var yTitle = Title(_yAxis);

        // Titles go first, then labels, then the axis lines, until the plot fits
        Rect plot = default;
        var (titles, labels, axes) = (true, true, true);
        foreach (var (t, l, a) in new[] { (true, true, true), (false, true, true), (false, false, true), (false, false, false) })
        {
            plot = PlotArea(inner, t && yTitle.Length > 0, l, a, l ? yLabels.Max(s => (int?)UnicodeWidth.Of(s)) ?? 0 : 0, l && xLabels.Count > 0);
            (titles, labels, axes) = (t, l, a);
            if (!plot.IsEmpty) break;
        }
        if (plot.IsEmpty) return;

        buf.SetStyle(inner.Intersect(buf.Area), style);
        if (axes) DrawAxes(buf, plot, theme.Border);
        if (axes) DrawTicks(buf, plot, xLabels.Count, yLabels.Count, theme.Border);
        DrawGrid(buf, plot, xLabels.Count, yLabels.Count, theme.Border.Patch(theme.TextMuted));
        if (labels)
        {
            DrawYLabels(buf, plot, inner.X, yLabels, style.Patch(theme.TextMuted));
            DrawXLabels(buf, plot, axes ? plot.Bottom + 1 : plot.Bottom, inner.Right, xLabels, style.Patch(theme.TextMuted));
        }

        var visible = plot.Intersect(buf.Area);
        for (var i = 0; i < datasets.Count; i++)
        {
            var dataset = datasets[i];
            var painter = new CanvasPainter(plot, visible, dataset.Marker, xBounds, yBounds);
            var dataStyle = dataset.Style ?? SeriesStyle(theme, i);
            var data = dataset.Data ?? [];
            if (dataset.Type == GraphType.Scatter || data.Count == 1) painter.Points(data, dataStyle);
            else
            {
                for (var p = 1; p < data.Count; p++)
                {
                    if (dataset.Type == GraphType.Area) painter.FillBelow(data[p - 1].X, data[p - 1].Y, data[p].X, data[p].Y, dataStyle);
                    painter.Line(data[p - 1].X, data[p - 1].Y, data[p].X, data[p].Y, dataStyle);
                }
            }
            painter.Render(buf, style);
        }

        if (titles)
        {
            if (yTitle.Length > 0) WidgetText.DrawString(buf, plot.X - 1, plot.Y - 1, yTitle, inner.Right - (plot.X - 1), style);
            if (xTitle.Length > 0) WidgetText.DrawString(buf, plot.X, plot.Bottom - 1, xTitle, plot.Width, style, Alignment.Right);
        }
        DrawLegend(buf, plot, datasets, theme, style);
    }

    // The plot inside the axes, labels and titles
    private static Rect PlotArea(Rect inner, bool yTitle, bool labels, bool axes, int yLabelWidth, bool xLabels)
    {
        var top = yTitle ? 1 : 0;
        var bottom = (axes ? 1 : 0) + (labels && xLabels ? 1 : 0);
        var left = yLabelWidth + (axes ? 1 : 0);
        return new Rect(inner.X + left, inner.Y + top, Math.Max(0, inner.Width - left), Math.Max(0, inner.Height - top - bottom));
    }

    private static void DrawAxes(Buffer buf, Rect plot, Style style)
    {
        var axis = new Rect(plot.X - 1, plot.Y, 1, plot.Height).Intersect(buf.Area);
        for (var y = axis.Y; y < axis.Bottom; y++) buf.SetString(plot.X - 1, y, "│", style, 1);
        buf.SetString(plot.X - 1, plot.Bottom, "└", style, 1);
        var clip = new Rect(plot.X, plot.Bottom, plot.Width, 1).Intersect(buf.Area);
        for (var x = clip.X; x < clip.Right; x++) buf.SetString(x, plot.Bottom, "─", style, 1);
    }

    // 1.x spec §19.5: ┬ on the X axis and ┤ on the Y axis at each label position
    private void DrawTicks(Buffer buf, Rect plot, int xCount, int yCount, Style style)
    {
        if (_xAxis.Ticks)
        {
            for (var i = 0; i < xCount; i++) buf.SetString(plot.X + Spread(i, xCount, plot.Width), plot.Bottom, "┬", style, 1);
        }
        if (_yAxis.Ticks)
        {
            for (var i = 0; i < yCount; i++) buf.SetString(plot.X - 1, plot.Bottom - 1 - Spread(i, yCount, plot.Height), "┤", style, 1);
        }
    }

    // Dotted lines across the plot at each label position, but not the first, which is beside the other axis
    private void DrawGrid(Buffer buf, Rect plot, int xCount, int yCount, Style style)
    {
        var columns = _xAxis.Grid ? Enumerable.Range(1, Math.Max(0, xCount - 1)).Select(i => plot.X + Spread(i, xCount, plot.Width)).ToHashSet() : [];
        var rows = _yAxis.Grid ? Enumerable.Range(1, Math.Max(0, yCount - 1)).Select(i => plot.Bottom - 1 - Spread(i, yCount, plot.Height)).ToHashSet() : [];
        if (columns.Count == 0 && rows.Count == 0) return;
        var visible = plot.Intersect(buf.Area);
        for (var y = visible.Y; y < visible.Bottom; y++)
        for (var x = visible.X; x < visible.Right; x++)
        {
            var (column, row) = (columns.Contains(x), rows.Contains(y));
            if (column || row) buf.SetString(x, y, column && row ? "┼" : column ? "┊" : "┈", style, 1);
        }
    }

    // Right-aligned left of the axis, the first on the bottom row and the last on the top one
    private static void DrawYLabels(Buffer buf, Rect plot, int x, List<string> labels, Style style)
    {
        var width = plot.X - 1 - x;
        var lastRow = int.MaxValue;
        for (var i = 0; i < labels.Count; i++)
        {
            var row = plot.Bottom - 1 - Spread(i, labels.Count, plot.Height);
            if (row >= lastRow) continue;
            WidgetText.DrawString(buf, x, row, labels[i], width, style, Alignment.Right);
            lastRow = row;
        }
    }

    // The first left-aligned at the plot's left, the last right-aligned at its right, the others centered
    private static void DrawXLabels(Buffer buf, Rect plot, int y, int right, List<string> labels, Style style)
    {
        var lastEnd = int.MinValue;
        for (var i = 0; i < labels.Count; i++)
        {
            var width = UnicodeWidth.Of(labels[i]);
            var anchor = plot.X + Spread(i, labels.Count, plot.Width);
            var start = labels.Count == 1 || i == 0 ? anchor
                : i == labels.Count - 1 ? anchor - width + 1
                : anchor - width / 2;
            start = Math.Max(start, plot.X);
            if (start <= lastEnd) continue;
            var drawn = WidgetText.DrawString(buf, start, y, labels[i], right - start, style);
            lastEnd = start + drawn;
        }
    }

    // A bordered box in the chosen corner, omitted when it would cover more than half the plot's width or height
    private void DrawLegend(Buffer buf, Rect plot, List<Dataset> datasets, Theme theme, Style style)
    {
        var entries = datasets.Select((d, i) => (Name: d.Name, Sample: Sample(d.Marker), Style: style.Patch(d.Style ?? SeriesStyle(theme, i)))).ToList();
        ChartLegend.Draw(buf, plot, _legend, entries, theme, style);
    }

    private static string Sample(Marker marker) => marker switch
    {
        Marker.Braille when !Glyphs.Basic => "⣿",
        Marker.Dot => "•",
        _ => "█",
    };

    internal static Style SeriesStyle(Theme theme, int index) =>
        theme.ChartSeries is { Count: > 0 } series ? series[index % series.Count] : default;

    // Position i of count spread over length cells: 0 for the first, length - 1 for the last
    private static int Spread(int i, int count, int length) =>
        count <= 1 ? 0 : (int)Math.Round((double)i * (length - 1) / (count - 1));

    private static (double Min, double Max) Bounds(Axis axis, IEnumerable<double> values)
    {
        double? low = null, high = null;
        foreach (var v in values)
        {
            if (!double.IsFinite(v)) continue;
            low = Math.Min(low ?? v, v);
            high = Math.Max(high ?? v, v);
        }

        var min = axis.Min ?? low;
        var max = axis.Max ?? high;
        switch (min, max)
        {
            case (null, null): return (0, 1);
            case ({ } a, null): return (a, a + 1);
            case (null, { } b): return (b - 1, b);
        }
        if (min == max && axis.Min is null && axis.Max is null) return (min.Value - 1, max.Value + 1);
        if (max <= min && axis.Max is null) return (min.Value, min.Value + 1);
        if (max <= min && axis.Min is null) return (max.Value - 1, max.Value);
        return (min.Value, max.Value);
    }

    private static List<string> Labels(Axis axis, (double Min, double Max) bounds)
    {
        if (axis.Labels is { } labels) return [.. labels.Select(l => Sanitizer.SingleLine(l ?? ""))];
        string Format(double v) => Sanitizer.SingleLine(axis.Formatter?.Invoke(v) ?? v.ToString(axis.LabelFormat, CultureInfo.InvariantCulture));
        var count = Math.Max(2, axis.LabelCount);
        return [.. Enumerable.Range(0, count).Select(i => Format(bounds.Min + (bounds.Max - bounds.Min) * i / (count - 1)))];
    }

    private static string Title(Axis axis) => Sanitizer.SingleLine(axis.Title ?? "");
}

/// <summary>The legend box shared by <see cref="ChartBuilder"/> and <see cref="BarChartBuilder"/> (1.x spec §8.4, §19.3).</summary>
internal static class ChartLegend
{
    /// <summary>
    /// Draw one row per named entry (its sample in its style, then its name) in a bordered box in the corner of
    /// <paramref name="area"/>, unless it would cover more than half its width or height.
    /// </summary>
    public static void Draw(Buffer buf, Rect area, LegendPosition position, IEnumerable<(string Name, string Sample, Style Style)> entries, Theme theme, Style style)
    {
        if (position == LegendPosition.None) return;
        var named = entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => e with { Name = Sanitizer.SingleLine(e.Name) }).ToList();
        if (named.Count == 0) return;

        var width = named.Max(e => UnicodeWidth.Of(e.Name)) + 4;
        var height = named.Count + 2;
        if (width > area.Width / 2.0 || height > area.Height / 2.0) return;

        var x = position is LegendPosition.TopLeft or LegendPosition.BottomLeft ? area.X : area.Right - width;
        var y = position is LegendPosition.TopLeft or LegendPosition.TopRight ? area.Y : area.Bottom - height;
        var box = new Rect(x, y, width, height);
        WidgetText.Clear(buf, box);
        buf.SetStyle(box.Intersect(buf.Area), style);
        var inner = Widget.Block().BorderStyle(theme.Border).Draw(buf, box);
        for (var i = 0; i < named.Count; i++)
        {
            buf.SetString(inner.X, inner.Y + i, named[i].Sample, named[i].Style, 1);
            WidgetText.DrawString(buf, inner.X + 2, inner.Y + i, named[i].Name, inner.Width - 2, style);
        }
    }
}
