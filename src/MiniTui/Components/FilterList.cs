namespace MiniTui;

/// <summary>
/// The filter line and fuzzy-filtered rows shared by <see cref="CommandPalette"/> and the filterable select
/// (1.x spec §13.3, §13.6): the text, the matches, the selection and the drawing.
/// </summary>
internal sealed class FilterList
{
    /// <summary>A row: the matched label, muted text after it, and a right-aligned hint.</summary>
    internal readonly record struct Row(string Label, string? Detail = null, string? Hint = null);

    private readonly Row[] _rows;
    private readonly string[] _labels;
    private List<(int Item, FuzzyMatch Match)> _matches = [];
    private string? _filtered;
    private int _selected;
    private int _offset;
    private int _viewport;
    private Rect _rowsArea;

    public FilterList(IEnumerable<Row> rows, string initialText = "")
    {
        _rows = [.. rows];
        _labels = Array.ConvertAll(_rows, r => Sanitizer.SingleLine(r.Label ?? ""));
        Filter = new TextFieldState(initialText);
        Refilter();
    }

    /// <summary>The filter line.</summary>
    public TextFieldState Filter { get; }

    /// <summary>How many rows match the filter.</summary>
    public int MatchCount => _matches.Count;

    /// <summary>The selected row's index in the rows, or null when nothing matches.</summary>
    public int? SelectedItem => _selected >= 0 && _selected < _matches.Count ? _matches[_selected].Item : null;

    /// <summary>The rows matching, best first, as indexes into the rows.</summary>
    internal IEnumerable<int> Matches => _matches.Select(m => m.Item);

    /// <summary>Select <paramref name="item"/> if it matches.</summary>
    public void SelectItem(int item)
    {
        var index = _matches.FindIndex(m => m.Item == item);
        if (index >= 0) _selected = index;
    }

    /// <summary>
    /// ↑ ↓ (wrapping), PgUp PgDn, Ctrl+Home and Ctrl+End move the selection; any other key goes to the filter
    /// line, and a change of its text filters again. Enter, Esc and Tab are left to the caller.
    /// </summary>
    /// <returns>True if the key moved the selection or the filter line used it.</returns>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        var ctrl = KeyInput.HasControl(key);
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Move(-1); return true;
            case ConsoleKey.DownArrow: Move(1); return true;
            case ConsoleKey.PageUp: Page(-1); return true;
            case ConsoleKey.PageDown: Page(1); return true;
            case ConsoleKey.Home when ctrl: _selected = _matches.Count > 0 ? 0 : -1; return true;
            case ConsoleKey.End when ctrl: _selected = _matches.Count - 1; return true;
        }
        var handled = Filter.HandleKey(key);
        Refilter();
        return handled;
    }

    /// <summary>Paste into the filter line, filtering again.</summary>
    public bool Paste(string text)
    {
        var handled = Filter.Paste(text);
        Refilter();
        return handled;
    }

    /// <summary>
    /// The wheel moves the selection and the filter line takes its clicks and drags, using the areas from the last
    /// draw.
    /// </summary>
    /// <returns>The row clicked, to choose it, or null.</returns>
    public int? HandleMouse(MouseEvent mouse)
    {
        switch (mouse.Action)
        {
            case MouseAction.ScrollUp: Move(-1, wrap: false); return null;
            case MouseAction.ScrollDown: Move(1, wrap: false); return null;
        }
        if (mouse.IsClick && _rowsArea.Contains(mouse.X, mouse.Y))
        {
            var index = _offset + (mouse.Y - _rowsArea.Y);
            return index < _matches.Count ? _matches[index].Item : null;
        }
        Filter.HandleMouse(mouse);
        return null;
    }

    /// <summary>The rows the list wants: the matches, at least 1 (for "No matches"), at most <paramref name="maxRows"/>.</summary>
    public int WantedRows(int maxRows) => Math.Clamp(_matches.Count, 1, Math.Max(1, maxRows));

    /// <summary>The widest row: label, detail and hint with their gaps and padding.</summary>
    public int ContentWidth()
    {
        var width = 0;
        for (var i = 0; i < _rows.Length; i++)
        {
            var row = _rows[i];
            var w = UnicodeWidth.Of(_labels[i]);
            if (row.Detail is { Length: > 0 } detail) w += 2 + UnicodeWidth.Of(Sanitizer.SingleLine(detail));
            if (row.Hint is { Length: > 0 } hint) w += 2 + UnicodeWidth.Of(Sanitizer.SingleLine(hint));
            width = Math.Max(width, w);
        }
        return width + 2;
    }

    /// <summary>
    /// Draw a block over <paramref name="area"/> with <paramref name="title"/>, the filter line in its first row, a
    /// divider, and the matching rows under it.
    /// </summary>
    public void Draw(Buffer buf, Rect area, string? title, string? placeholder)
    {
        var theme = Theme.Current;
        var inner = Widget.Block()
            .Title(title is { Length: > 0 } t ? new Line(t) : null)
            .BorderStyle(theme.ModalBorder)
            .TitleStyle(theme.ModalTitle)
            .Draw(buf, area);
        if (inner.IsEmpty)
        {
            _rowsArea = default;
            return;
        }

        var field = new Rect(inner.X + 1, inner.Y, Math.Max(0, inner.Width - 2), 1);
        TextFieldRenderer.Draw(buf, field, Filter, focused: true, theme.Text, placeholder);
        if (inner.Height < 2)
        {
            _rowsArea = default;
            return;
        }

        WidgetText.HorizontalLine(buf, inner.X, inner.Y + 1, inner.Width, theme.ModalBorder);
        if (inner.X > area.X) buf.SetString(area.X, inner.Y + 1, "├", theme.ModalBorder, 1);
        if (inner.Right < area.Right) buf.SetString(area.Right - 1, inner.Y + 1, "┤", theme.ModalBorder, 1);

        var rows = new Rect(inner.X, inner.Y + 2, inner.Width, Math.Max(0, inner.Height - 2));
        _viewport = rows.Height;
        if (_matches.Count == 0)
        {
            _rowsArea = default;
            if (rows.Height > 0) WidgetText.DrawString(buf, rows.X + 1, rows.Y, "No matches", Math.Max(0, rows.Width - 2), theme.Text.Patch(theme.TextMuted));
            return;
        }

        _offset = SelectionState.FitOffset(_offset, Math.Max(0, _selected), _matches.Count, _viewport);
        var visible = Math.Clamp(_matches.Count - _offset, 0, _viewport);
        _rowsArea = rows with { Height = visible };
        for (var i = 0; i < visible; i++)
            DrawRow(buf, rows.X, rows.Y + i, rows.Width, _offset + i, theme);

        if (_matches.Count > _viewport)
        {
            new ScrollbarBuilder().ContentLength(_matches.Count).ViewportLength(_viewport).Position(_offset)
                .Draw(buf, new Rect(area.Right - 1, rows.Y, 1, rows.Height));
        }
    }

    private void DrawRow(Buffer buf, int x, int y, int width, int index, Theme theme)
    {
        var (item, match) = _matches[index];
        var row = _rows[item];
        var label = _labels[item];
        var muted = theme.Text.Patch(theme.TextMuted);

        var hint = row.Hint is { Length: > 0 } h ? Sanitizer.SingleLine(h) : "";
        var hintWidth = Math.Min(UnicodeWidth.Of(hint), Math.Max(0, width - 2));
        var available = width - 2 - (hintWidth > 0 ? hintWidth + 2 : 0);
        var labelWidth = WidgetText.DrawString(buf, x + 1, y, label, Math.Max(0, available), theme.Text);

        var detail = row.Detail is { Length: > 0 } d ? Sanitizer.SingleLine(d) : "";
        var detailWidth = available - labelWidth - 2;
        if (detail.Length > 0 && detailWidth > 0) WidgetText.DrawString(buf, x + 1 + labelWidth + 2, y, detail, detailWidth, muted);
        if (hintWidth > 0) WidgetText.DrawString(buf, x + width - 1 - hintWidth, y, hint, hintWidth, muted);

        var selected = index == _selected;
        if (selected) buf.SetStyle(new Rect(x, y, width, 1), theme.Selection);

        // Matched characters, inside the part of the label that was drawn
        var highlight = selected ? new Style(Add: Modifiers.Bold) : theme.Title.Bold();
        foreach (var position in match.Positions)
        {
            if (position >= label.Length) continue;
            var column = UnicodeWidth.Of(label.AsSpan(0, position));
            var cellWidth = Math.Max(1, UnicodeWidth.Of(label.AsSpan(position, char.IsHighSurrogate(label[position]) && position + 1 < label.Length ? 2 : 1)));
            if (column + cellWidth > labelWidth) continue;
            buf.SetStyle(new Rect(x + 1 + column, y, cellWidth, 1), highlight);
        }
    }

    // Filter again when the text changed; the selection goes back to the best match
    private void Refilter()
    {
        var text = Filter.Text;
        if (text == _filtered) return;
        _filtered = text;
        var matches = new List<(int Item, FuzzyMatch Match)>();
        for (var i = 0; i < _rows.Length; i++)
            if (Fuzzy.Match(text, _labels[i]) is { } match) matches.Add((i, match));

        // Best score first; then the shorter label, then the rows' order (a stable sort)
        _matches = [.. matches
            .OrderByDescending(m => m.Match.Score)
            .ThenBy(m => text.Trim().Length == 0 ? 0 : _labels[m.Item].Length)
            .ThenBy(m => m.Item)];
        _selected = _matches.Count > 0 ? 0 : -1;
        _offset = 0;
    }

    private void Move(int direction, bool wrap = true)
    {
        if (_matches.Count == 0) return;
        var target = _selected + direction;
        _selected = wrap ? ((target % _matches.Count) + _matches.Count) % _matches.Count : Math.Clamp(target, 0, _matches.Count - 1);
    }

    private void Page(int direction)
    {
        if (_matches.Count == 0) return;
        _selected = Math.Clamp(_selected + direction * Math.Max(1, _viewport), 0, _matches.Count - 1);
    }
}
