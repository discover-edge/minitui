using System.Globalization;

namespace MiniTui;

/// <summary>
/// A progress bar with eighth-cell precision and a centered label.
/// </summary>
public sealed class GaugeBuilder
{
    private const string Eighths = " ▏▎▍▌▋▊▉";

    private double _ratio;
    private Line? _label;
    private bool _hasLabel = true;
    private BlockBuilder? _block;
    private Style? _filledStyle;
    private Style? _emptyStyle;

    /// <summary>How full the gauge is, from 0 to 1. Values outside are clamped; NaN is 0.</summary>
    public GaugeBuilder Ratio(double ratio) { _ratio = double.IsNaN(ratio) ? 0 : Math.Clamp(ratio, 0, 1); return this; }

    /// <summary>The label drawn in the middle. Null shows the percentage (the default); an empty line shows none.</summary>
    public GaugeBuilder Label(Line? label) { _label = label; _hasLabel = label is null || label.Width > 0; return this; }

    /// <summary>Draw a block around the gauge.</summary>
    public GaugeBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's filled style.</summary>
    public GaugeBuilder FilledStyle(Style style) { _filledStyle = style; return this; }

    /// <summary>A style patched over the theme's empty style.</summary>
    public GaugeBuilder EmptyStyle(Style style) { _emptyStyle = style; return this; }

    /// <summary>Draw the gauge.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Draw(buf, area) ?? area;
        if (inner.IsEmpty) return;

        var theme = Theme.Current;
        var filled = theme.GaugeFilled.Patch(_filledStyle ?? default);
        var empty = theme.GaugeEmpty.Patch(_emptyStyle ?? default);

        var eighths = (long)(_ratio * inner.Width * 8);
        var full = (int)(eighths / 8);
        var partial = (int)(eighths % 8);

        var clip = inner.Intersect(buf.Area);
        for (var y = clip.Y; y < clip.Bottom; y++)
        for (var x = clip.X; x < clip.Right; x++)
        {
            var i = x - inner.X;
            if (i < full) buf.SetString(x, y, "█", filled, 1);
            else if (i == full && partial > 0) buf.SetString(x, y, Eighths[partial].ToString(), filled, 1);
            else buf.SetString(x, y, "░", empty, 1);
        }

        if (!_hasLabel) return;
        var label = _label ?? new Line(string.Create(CultureInfo.InvariantCulture, $"{Math.Round(_ratio * 100)}%"));
        DrawLabel(buf, inner, label, full + (partial >= 4 ? 1 : 0), filled);
    }

    // Over the filled part the label is reversed, so it reads as text on the bar color
    private static void DrawLabel(Buffer buf, Rect inner, Line label, int filledCells, Style filled)
    {
        var y = inner.Y + inner.Height / 2;
        var width = Math.Min(label.Width, inner.Width);
        var x = inner.X + (inner.Width - width) / 2;
        WidgetText.DrawLine(buf, x, y, label, width, new Style(Color.Reset));

        var onBar = Math.Clamp(inner.X + filledCells - x, 0, width);
        if (onBar > 0) buf.SetStyle(new Rect(x, y, onBar, 1), filled.Reversed());
    }
}
