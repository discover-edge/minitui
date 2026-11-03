using System.Globalization;
using System.Security;
using System.Text;
using MiniTui;

namespace SiteMedia;

/// <summary>A frame as an SVG in a window frame: background runs as rectangles, text runs stretched to their cells.</summary>
internal static class Svg
{
    private const double CellWidth = 8.4;
    private const double CellHeight = 18;
    private const double FontSize = 14;
    private const double Pad = 14;
    private const double TitleBar = 30;
    private const string Fonts = "ui-monospace, 'Cascadia Mono', 'SF Mono', Menlo, Consolas, 'DejaVu Sans Mono', monospace";

    public static string Draw(Func<int, int, Cell> cellAt, int width, int height, string title)
    {
        var (defaultFg, defaultBg) = Recorder.DefaultColors();
        var w = width * CellWidth + 2 * Pad;
        var h = height * CellHeight + 2 * Pad + TitleBar;
        var svg = new StringBuilder();
        svg.Append(Invariant($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" width=\"{w}\" height=\"{h}\" role=\"img\" aria-label=\"{Escape(title)}\">\n"));
        svg.Append(Invariant($"<rect width=\"{w}\" height=\"{h}\" rx=\"10\" fill=\"{defaultBg}\" stroke=\"#353B46\"/>\n"));
        for (var i = 0; i < 3; i++)
            svg.Append(Invariant($"<circle cx=\"{18 + i * 18}\" cy=\"{TitleBar / 2 + 2}\" r=\"5.5\" fill=\"{(i == 0 ? "#F2777A" : i == 1 ? "#FFC66D" : "#7EC699")}\"/>\n"));
        svg.Append(Invariant($"<text x=\"{w / 2}\" y=\"{TitleBar / 2 + 6}\" text-anchor=\"middle\" font-family=\"{Fonts}\" font-size=\"12\" fill=\"#7D8590\">{Escape(title)}</text>\n"));
        svg.Append(Invariant($"<g transform=\"translate({Pad} {Pad + TitleBar})\" font-family=\"{Fonts}\" font-size=\"{FontSize}\">\n"));

        for (var y = 0; y < height; y++)
        {
            // Backgrounds, and full blocks, which fonts draw with gaps, as rectangles
            for (var x = 0; x < width;)
            {
                var (_, bg) = Colors(cellAt(x, y), defaultFg, defaultBg);
                var end = x + 1;
                while (end < width && Colors(cellAt(end, y), defaultFg, defaultBg).Bg == bg) end++;
                if (bg != defaultBg) Rect(svg, x, y, end - x, bg);
                x = end;
            }
            for (var x = 0; x < width; x++)
            {
                var cell = cellAt(x, y);
                if (cell.Symbol == "█") Rect(svg, x, y, 1, Colors(cell, defaultFg, defaultBg).Fg);
            }

            // Text, in runs of one style
            for (var x = 0; x < width;)
            {
                var cell = cellAt(x, y);
                if (cell.Symbol is " " or "█" or "")
                {
                    x++;
                    continue;
                }
                // A run ends at a change of style, or at two spaces: browsers collapse runs of spaces in SVG text,
                // which would stretch the rest of the run over its cells
                var fg = Colors(cell, defaultFg, defaultBg).Fg;
                var text = new StringBuilder();
                var start = x;
                while (x < width)
                {
                    var next = cellAt(x, y);
                    if (next.Symbol is "█" || next.Modifiers != cell.Modifiers) break;
                    if (next.Symbol == " " ? cellAt(x + 1, y).Symbol == " " : Colors(next, defaultFg, defaultBg).Fg != fg) break;
                    text.Append(next.Symbol);
                    x++;
                }
                var trimmed = text.ToString().TrimEnd();
                Text(svg, start, y, x - start - (text.Length - trimmed.Length), trimmed, fg, cell.Modifiers);
            }
        }
        svg.Append("</g>\n</svg>\n");
        return svg.ToString();
    }

    private static void Rect(StringBuilder svg, int x, int y, int cells, string fill) =>
        svg.Append(Invariant($"<rect x=\"{x * CellWidth}\" y=\"{y * CellHeight}\" width=\"{cells * CellWidth + 0.3}\" height=\"{CellHeight + 0.3}\" fill=\"{fill}\"/>\n"));

    private static void Text(StringBuilder svg, int x, int y, int cells, string text, string fill, Modifiers modifiers)
    {
        var attributes = new StringBuilder();
        if ((modifiers & Modifiers.Bold) != 0) attributes.Append(" font-weight=\"bold\"");
        if ((modifiers & Modifiers.Italic) != 0) attributes.Append(" font-style=\"italic\"");
        if ((modifiers & Modifiers.Dim) != 0) attributes.Append(" opacity=\"0.6\"");
        if ((modifiers & Modifiers.Underlined) != 0) attributes.Append(" text-decoration=\"underline\"");
        if ((modifiers & Modifiers.CrossedOut) != 0) attributes.Append(" text-decoration=\"line-through\"");
        svg.Append(Invariant($"<text x=\"{x * CellWidth}\" y=\"{y * CellHeight + CellHeight * 0.75}\" textLength=\"{cells * CellWidth}\" lengthAdjust=\"spacingAndGlyphs\" fill=\"{fill}\"{attributes}>{Escape(text)}</text>\n"));
    }

    private static (string Fg, string Bg) Colors(Cell cell, string defaultFg, string defaultBg)
    {
        var fg = cell.Fg.Kind == ColorKind.Reset ? defaultFg : Hex(cell.Fg, foreground: true);
        var bg = cell.Bg.Kind == ColorKind.Reset ? defaultBg : Hex(cell.Bg, foreground: false);
        return (cell.Modifiers & Modifiers.Reversed) != 0 ? (bg, fg) : (fg, bg);
    }

    public static string Hex(Color color, bool foreground)
    {
        var (r, g, b) = color.Kind switch
        {
            ColorKind.Rgb => color.RgbValue,
            ColorKind.Named or ColorKind.Indexed => Color.IndexedToRgb(color.Index),
            _ => foreground ? ((byte)0xD4, (byte)0xD7, (byte)0xDD) : ((byte)0x1C, (byte)0x1F, (byte)0x26),
        };
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string Escape(string text) => SecurityElement.Escape(text);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
