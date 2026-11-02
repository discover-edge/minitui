namespace MiniTui;

/// <summary>An item in a <see cref="PopupMenu"/> (1.x spec §7.2).</summary>
/// <param name="Label">The text of the item.</param>
public sealed record MenuItem(string Label)
{
    /// <summary>Display-only text drawn right-aligned and muted, such as a key: <c>"Ctrl+S"</c>.</summary>
    public string? Hint { get; init; }

    /// <summary>A disabled item is drawn muted, skipped by the keys and not chosen by a click. True by default.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>True for <see cref="Separator"/>.</summary>
    public bool IsSeparator { get; private init; }

    /// <summary>A divider line. It is never selected or chosen.</summary>
    public static MenuItem Separator { get; } = new("") { IsSeparator = true };

    internal bool CanChoose => Enabled && !IsSeparator;
}

/// <summary>
/// A menu overlay next to an anchor (1.x spec §7). ↑ ↓ PgUp PgDn Home End and type-ahead move the selection;
/// Enter or a click chooses an item; Esc, Tab or a click outside dismisses the menu. Show it with
/// <see cref="OverlayStack.ShowMenuAsync"/>, or with <see cref="OverlayStack.Show"/> and await <see cref="Result"/>.
/// </summary>
public sealed class PopupMenu : Overlay
{
    private static readonly TimeSpan TypeAheadReset = TimeSpan.FromSeconds(1);

    private readonly TaskCompletionSource<int?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MenuItem[] _items;
    private readonly Rect _anchor;
    private int _selected;
    private int _offset;
    private int _viewport;
    private Rect _rows;
    private int? _choice;
    private string _search = "";
    private long _lastTyped;

    /// <summary>Create a menu. Show it with <see cref="OverlayStack.Show"/> and await <see cref="Result"/>.</summary>
    /// <param name="items">The items, top to bottom.</param>
    /// <param name="anchor">
    /// What the menu is for, in screen cells: a 1×1 rect for a point (a right-click), or the rect of a row or a
    /// field. The menu opens below it, or above when there's more room there.
    /// </param>
    /// <param name="selected">
    /// The item selected at first. A separator or disabled item moves the selection to the next enabled one.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="items"/> contains null.</exception>
    public PopupMenu(IReadOnlyList<MenuItem> items, Rect anchor, int selected = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        if (Array.IndexOf(_items, null) >= 0) throw new ArgumentException("The items can't contain null.", nameof(items));
        _anchor = anchor;
        _selected = _items.Length == 0 ? -1 : Find(Math.Clamp(selected, 0, _items.Length - 1), 1, wrap: true) ?? -1;
    }

    /// <summary>The chosen item's index, or null when the menu was dismissed or closed any other way.</summary>
    public Task<int?> Result => _result.Task;

    /// <summary>The menu is at least this wide, border included. A select passes its field's width. Default 0.</summary>
    public int MinWidth { get; init; }

    /// <summary>At most this many item rows are shown; more scroll. Default 12.</summary>
    public int MaxRows { get; init; } = 12;

    /// <summary>The selected item's index, or -1 when no item is enabled.</summary>
    internal int Selected => _selected;

    /// <inheritdoc/>
    public override bool Framed => false;

    /// <inheritdoc/>
    public override bool CloseOnClickOutside => true;

    /// <inheritdoc/>
    public override string KeysGroup => "Menu";

    /// <summary>Called with the result once the menu has closed, on the UI thread (for <see cref="SelectState"/>).</summary>
    internal Action<int?>? Closed { get; init; }

    /// <summary>Below the anchor, or above it, shifted left to stay on the screen (1.x spec §7.4).</summary>
    public override Rect Placement(Rect screen)
    {
        var width = Math.Min(Math.Max(MinWidth, ContentWidth() + 2), screen.Width);
        var wanted = Math.Min(_items.Length, Math.Max(1, MaxRows)) + 2;

        // An anchor left off the screen by a resize is pulled back onto it
        var top = Math.Clamp(_anchor.Y, screen.Y, screen.Bottom);
        var bottom = Math.Clamp(_anchor.Bottom, top, screen.Bottom);
        var below = screen.Bottom - bottom;
        var above = top - screen.Y;
        var (y, height) = wanted <= below ? (bottom, wanted)
            : wanted <= above ? (top - wanted, wanted)
            : below >= above ? (bottom, below)
            : (screen.Y, above);
        var x = Math.Clamp(_anchor.X, screen.X, Math.Max(screen.X, screen.Right - width));
        return new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    /// <inheritdoc/>
    public override bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Move(-1, wrap: true); return true;
            case ConsoleKey.DownArrow: Move(1, wrap: true); return true;
            case ConsoleKey.PageUp: Page(-1); return true;
            case ConsoleKey.PageDown: Page(1); return true;
            case ConsoleKey.Home: Select(Find(0, 1, wrap: false)); return true;
            case ConsoleKey.End: Select(Find(_items.Length - 1, -1, wrap: false)); return true;
            case ConsoleKey.Enter when KeyInput.IsEnter(key):
                if (_selected >= 0) Choose(_selected);
                return true;
            case ConsoleKey.Escape or ConsoleKey.Tab:
                Close();
                return true;
        }

        if (!KeyInput.HasCommandModifier(key) && !char.IsControl(key.KeyChar)) TypeAhead(key.KeyChar);
        return true;
    }

    /// <inheritdoc/>
    public override bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        switch (mouse.Action)
        {
            case MouseAction.ScrollUp: Move(-1, wrap: false); return true;
            case MouseAction.ScrollDown: Move(1, wrap: false); return true;
        }

        if (!mouse.IsClick || !_rows.Contains(mouse.X, mouse.Y)) return true;
        var index = _offset + (mouse.Y - _rows.Y);
        if (index < _items.Length && _items[index].CanChoose) Choose(index);
        return true;
    }

    /// <inheritdoc/>
    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(ctx);
        var theme = ctx.Theme;
        var inner = Widget.Block().BorderStyle(theme.ModalBorder).Draw(buf, area);
        _viewport = Math.Max(0, inner.Height);
        _offset = SelectionState.FitOffset(_offset, Math.Max(0, _selected), _items.Length, _viewport);
        var visible = Math.Clamp(_items.Length - _offset, 0, _viewport);
        _rows = inner with { Height = visible };
        if (inner.IsEmpty) return;

        var muted = theme.Text.Patch(theme.TextMuted);
        for (var i = 0; i < visible; i++)
        {
            var index = _offset + i;
            var item = _items[index];
            var y = inner.Y + i;
            if (item.IsSeparator)
            {
                WidgetText.HorizontalLine(buf, inner.X, y, inner.Width, theme.ModalBorder);
                if (inner.X > area.X) buf.SetString(area.X, y, "├", theme.ModalBorder, 1);
                if (inner.Right < area.Right) buf.SetString(area.Right - 1, y, "┤", theme.ModalBorder, 1);
                continue;
            }

            var hint = item.Hint is { Length: > 0 } h ? Sanitizer.SingleLine(h) : "";
            var hintWidth = Math.Min(UnicodeWidth.Of(hint), Math.Max(0, inner.Width - 2));
            var labelWidth = inner.Width - 2 - (hintWidth > 0 ? hintWidth + 2 : 0);
            WidgetText.DrawString(buf, inner.X + 1, y, Sanitizer.SingleLine(item.Label ?? ""), labelWidth, item.Enabled ? theme.Text : muted);
            if (hintWidth > 0) WidgetText.DrawString(buf, inner.Right - 1 - hintWidth, y, hint, hintWidth, muted);
            if (index == _selected) buf.SetStyle(new Rect(inner.X, y, inner.Width, 1), theme.Selection);
        }

        if (_items.Length > _viewport)
        {
            new ScrollbarBuilder().ContentLength(_items.Length).ViewportLength(_viewport).Position(_offset)
                .Draw(buf, new Rect(area.Right - 1, inner.Y, 1, inner.Height));
        }
    }

    /// <inheritdoc/>
    protected override void OnMount() => App.BeginUserWait();

    /// <inheritdoc/>
    protected override void OnUnmount()
    {
        App.EndUserWait();
        _result.TrySetResult(_choice);
        Closed?.Invoke(_choice);
    }

    // " Label", two spaces, "Hint ": the widest of each
    private int ContentWidth()
    {
        var labels = 0;
        var hints = 0;
        foreach (var item in _items)
        {
            if (item.IsSeparator) continue;
            labels = Math.Max(labels, UnicodeWidth.Of(Sanitizer.SingleLine(item.Label ?? "")));
            if (item.Hint is { Length: > 0 } hint) hints = Math.Max(hints, UnicodeWidth.Of(Sanitizer.SingleLine(hint)));
        }
        return labels + 2 + (hints > 0 ? hints + 2 : 0);
    }

    private void Choose(int index)
    {
        _choice = index;
        Close();
    }

    private void Select(int? index)
    {
        if (index is { } i) _selected = i;
    }

    private void Move(int direction, bool wrap)
    {
        if (_selected >= 0) Select(Find(_selected + direction, direction, wrap));
    }

    // By the visible rows, stopping at the first or last enabled item
    private void Page(int direction)
    {
        if (_selected < 0) return;
        var target = Math.Clamp(_selected + direction * Math.Max(1, _viewport), 0, _items.Length - 1);
        Select(Find(target, direction, wrap: false) ?? Find(target, -direction, wrap: false));
    }

    /// <summary>
    /// The first item that can be chosen from <paramref name="start"/> on, stepping by <paramref name="direction"/>,
    /// around the ends if <paramref name="wrap"/>; null if there's none.
    /// </summary>
    private int? Find(int start, int direction, bool wrap, Func<MenuItem, bool>? match = null)
    {
        var n = _items.Length;
        for (var step = 0; step < n; step++)
        {
            var i = start + step * direction;
            if (wrap) i = ((i % n) + n) % n;
            else if (i < 0 || i >= n) return null;
            if (_items[i].CanChoose && (match is null || match(_items[i]))) return i;
        }
        return null;
    }

    // 1.x spec §7.3: a prefix cleared 1 s after the last character; the same character again moves on
    private void TypeAhead(char c)
    {
        if (_selected < 0) return;
        var time = App.Time;
        if (_search.Length > 0 && time.GetElapsedTime(_lastTyped) >= TypeAheadReset) _search = "";
        _lastTyped = time.GetTimestamp();
        _search += c;

        var first = _search[0];
        var repeated = _search.Length > 1 && _search.All(ch => char.ToUpperInvariant(ch) == char.ToUpperInvariant(first));
        Select(repeated
            ? Find(_selected + 1, 1, wrap: true, item => StartsWith(item, first.ToString()))
            : Find(_selected, 1, wrap: true, item => StartsWith(item, _search)));
    }

    private static bool StartsWith(MenuItem item, string prefix) =>
        (item.Label ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
