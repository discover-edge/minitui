using System.Globalization;

namespace MiniTui;

/// <summary>
/// How a <see cref="ParagraphBuilder"/> breaks lines that are wider than its area.
/// </summary>
public enum WrapMode
{
    /// <summary>Don't wrap; long lines are truncated with <c>…</c>.</summary>
    None,
    /// <summary>Break at whitespace. Words wider than the area are broken anywhere.</summary>
    Word,
    /// <summary>Break at any character.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Name from the spec (§8.3), matching Ratatui's wrap modes.")]
    Char,
}

/// <summary>
/// Measuring text the way <see cref="ParagraphBuilder"/> lays it out.
/// </summary>
public static class Paragraph
{
    /// <summary>How many rows <paramref name="text"/> takes at <paramref name="width"/> columns.</summary>
    public static int MeasureLines(Text text, int width, WrapMode wrap)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TextWrap.Wrap(text, width, wrap).Count;
    }
}

/// <summary>
/// Multi-line text with optional wrapping, alignment and scrolling.
/// </summary>
public sealed class ParagraphBuilder
{
    private Text _text = new([]);
    private WrapMode _wrap;
    private Alignment _alignment;
    private int _scroll;
    private ScrollState? _state;
    private BlockBuilder? _block;
    private Style? _style;

    /// <summary>The text. A string is split into lines at line breaks.</summary>
    public ParagraphBuilder Text(Text text) { _text = text ?? new Text([]); return this; }

    /// <summary>How long lines break. The default is <see cref="WrapMode.None"/>.</summary>
    public ParagraphBuilder Wrap(WrapMode wrap) { _wrap = wrap; return this; }

    /// <summary>The alignment of lines that don't set their own.</summary>
    public ParagraphBuilder Alignment(Alignment alignment) { _alignment = alignment; return this; }

    /// <summary>Skip this many rows from the top.</summary>
    public ParagraphBuilder Scroll(int rows) { _scroll = Math.Max(0, rows); return this; }

    /// <summary>Scroll with a state the paragraph keeps in range, and show a scrollbar when the text overflows.</summary>
    public ParagraphBuilder State(ScrollState state) { _state = state; return this; }

    /// <summary>Draw a block around the text.</summary>
    public ParagraphBuilder Block(BlockBuilder block) { _block = block; return this; }

    /// <summary>A style patched over the theme's text style.</summary>
    public ParagraphBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the paragraph.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var inner = _block?.Draw(buf, area) ?? area;
        if (inner.IsEmpty)
        {
            _state?.Fit(0, 0);
            return;
        }

        var rows = TextWrap.Wrap(_text, inner.Width, _wrap);

        // Without a right border, the scrollbar takes the last column, so the text is laid out narrower
        var scrollbarOnBorder = _block?.HasRightBorder == true;
        if (_state is not null && rows.Count > inner.Height && !scrollbarOnBorder && inner.Width > 1)
        {
            inner = inner with { Width = inner.Width - 1 };
            rows = TextWrap.Wrap(_text, inner.Width, _wrap);
        }

        var offset = _scroll;
        if (_state is { } state)
        {
            state.Fit(rows.Count, inner.Height);
            offset = state.Offset;
        }

        var style = Theme.Current.Text.Patch(_style ?? default);
        for (var i = 0; i < inner.Height && offset + i < rows.Count; i++)
        {
            var line = rows[offset + i];
            var alignment = line.Alignment != MiniTui.Alignment.Left ? line.Alignment : _alignment;
            WidgetText.DrawLine(buf, inner.X, inner.Y + i, line, inner.Width, style, alignment);
        }

        if (_state is not null && rows.Count > inner.Height)
        {
            var bar = scrollbarOnBorder ? new Rect(area.Right - 1, inner.Y, 1, inner.Height) : new Rect(inner.Right, inner.Y, 1, inner.Height);
            new ScrollbarBuilder().ContentLength(rows.Count).ViewportLength(inner.Height).Position(offset).Draw(buf, bar);
        }
    }
}

/// <summary>The line breaking behind <see cref="ParagraphBuilder"/>.</summary>
internal static class TextWrap
{
    public static List<Line> Wrap(Text text, int width, WrapMode mode)
    {
        var rows = new List<Line>();
        foreach (var line in text.Lines)
        {
            if (mode == WrapMode.None || width <= 0) rows.Add(line);
            else WrapLine(line, width, mode, rows);
        }
        return rows;
    }

    private static void WrapLine(Line line, int width, WrapMode mode, List<Line> rows)
    {
        var row = new RowBuilder(line.Alignment);
        if (mode == WrapMode.Char)
        {
            foreach (var g in Graphemes(line)) AddBreaking(g, width, row, rows);
            rows.Add(row.Build());
            return;
        }

        var firstRow = true;
        var pending = new List<Grapheme>();
        var pendingWidth = 0;
        foreach (var (word, isSpace) in Tokens(line))
        {
            if (isSpace)
            {
                // Leading whitespace is kept on a line's first row (indentation) and dropped at wrap points
                if (row.Width == 0 && !firstRow) continue;
                pending.AddRange(word);
                foreach (var g in word) pendingWidth += g.Width;
                continue;
            }

            var wordWidth = 0;
            foreach (var g in word) wordWidth += g.Width;

            if (row.Width + pendingWidth + wordWidth <= width)
            {
                row.AddRange(pending);
                row.AddRange(word);
            }
            else
            {
                if (row.Width > 0)
                {
                    rows.Add(row.Build());
                    firstRow = false;
                }
                if (wordWidth <= width) row.AddRange(word);
                else
                {
                    foreach (var g in word)
                        if (AddBreaking(g, width, row, rows)) firstRow = false;
                }
            }
            pending.Clear();
            pendingWidth = 0;
        }
        rows.Add(row.Build());
    }

    // Add a grapheme, starting a new row first if it doesn't fit. Returns true if a row was emitted.
    private static bool AddBreaking(Grapheme g, int width, RowBuilder row, List<Line> rows)
    {
        if (row.Width + g.Width <= width)
        {
            row.Add(g);
            return false;
        }
        if (row.Width == 0) return false; // wider than the whole area: dropped
        rows.Add(row.Build());
        if (g.Width <= width) row.Add(g);
        return true;
    }

    private static IEnumerable<(List<Grapheme> Graphemes, bool IsSpace)> Tokens(Line line)
    {
        List<Grapheme>? token = null;
        var tokenIsSpace = false;
        foreach (var g in Graphemes(line))
        {
            if (token is not null && g.IsSpace != tokenIsSpace)
            {
                yield return (token, tokenIsSpace);
                token = null;
            }
            token ??= [];
            tokenIsSpace = g.IsSpace;
            token.Add(g);
        }
        if (token is not null) yield return (token, tokenIsSpace);
    }

    private static IEnumerable<Grapheme> Graphemes(Line line)
    {
        foreach (var span in line.Spans)
        {
            var content = Sanitizer.SingleLine(span.Content ?? "");
            for (var i = 0; i < content.Length;)
            {
                var length = StringInfo.GetNextTextElementLength(content, i);
                var text = content.Substring(i, length);
                i += length;
                var w = UnicodeWidth.OfGrapheme(text);
                if (w == 0) continue;
                yield return new Grapheme(text, span.Style, w, text.Length == 1 && char.IsWhiteSpace(text[0]));
            }
        }
    }

    private readonly record struct Grapheme(string Text, Style Style, int Width, bool IsSpace);

    private sealed class RowBuilder(Alignment alignment)
    {
        private readonly List<Span> _spans = [];
        private readonly System.Text.StringBuilder _current = new();
        private Style _currentStyle;

        public int Width { get; private set; }

        public void Add(Grapheme g)
        {
            if (_current.Length > 0 && g.Style != _currentStyle) Flush();
            _currentStyle = g.Style;
            _current.Append(g.Text);
            Width += g.Width;
        }

        public void AddRange(List<Grapheme> graphemes)
        {
            foreach (var g in graphemes) Add(g);
        }

        public Line Build()
        {
            Flush();
            var line = new Line([.. _spans], alignment);
            _spans.Clear();
            Width = 0;
            return line;
        }

        private void Flush()
        {
            if (_current.Length == 0) return;
            _spans.Add(new Span(_current.ToString(), _currentStyle));
            _current.Clear();
        }
    }
}
