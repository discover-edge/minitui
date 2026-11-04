using System.Text;

namespace MiniTui;

/// <summary>An item in a <see cref="PopupMenu"/> (1.x spec §7.2, §15.2).</summary>
/// <param name="Label">
/// The text of the item. A single <c>&amp;</c> marks the next character as the mnemonic (<c>"&amp;Save"</c>);
/// <c>"&amp;&amp;"</c> is a literal <c>&amp;</c>, as is one before a space or at the end.
/// </param>
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

    /// <summary>
    /// Run on the UI thread when the item is chosen, after the menu and every menu it was opened from have closed.
    /// </summary>
    public Action? Run { get; init; }

    /// <summary>A check mark when true, an empty mark when false; null for an item without one. Default null.</summary>
    public bool? Checked { get; init; }

    /// <summary>Draw <see cref="Checked"/> as a radio mark (● or ○) instead of a check. Default false.</summary>
    public bool Radio { get; init; }

    /// <summary>
    /// The items of a submenu, opened beside the item with → or Enter or a click (1.x spec §15.4), or null. An item
    /// with a submenu is never chosen itself, and its <see cref="Hint"/> isn't drawn.
    /// </summary>
    public IReadOnlyList<MenuItem>? Submenu { get; init; }

    /// <summary>The mnemonic: the character after the first single <c>&amp;</c> in <see cref="Label"/>, lowercase, or null.</summary>
    public char? Mnemonic => Parse(Label).Mnemonic;

    internal bool CanChoose => Enabled && !IsSeparator;

    internal bool HasSubmenu => Submenu is not null;

    /// <summary>The label as drawn (sanitized, without the mnemonic marks) and the mnemonic's index in it, or -1.</summary>
    internal (string Text, int Index) Display => ParseDisplay(Sanitizer.SingleLine(Label ?? ""));

    private static (string Text, int Index, char? Mnemonic) Parse(string? label)
    {
        var (text, index) = ParseDisplay(label ?? "");
        char? mnemonic = index >= 0 && !char.IsSurrogate(text[index]) ? char.ToLowerInvariant(text[index]) : null;
        return (text, index, mnemonic);
    }

    // 1.x spec §15.2: "&&" is "&"; "& " and a trailing "&" stay; later single marks are dropped
    internal static (string Text, int Index) ParseDisplay(string label)
    {
        if (!label.Contains('&', StringComparison.Ordinal)) return (label, -1);
        var text = new StringBuilder(label.Length);
        var index = -1;
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            if (c != '&')
            {
                text.Append(c);
                continue;
            }
            var next = i + 1 < label.Length ? label[i + 1] : ' ';
            if (next == '&')
            {
                text.Append('&');
                i++;
            }
            else if (char.IsWhiteSpace(next))
            {
                text.Append('&');
            }
            else if (index < 0)
            {
                index = text.Length;
            }
        }
        return (text.ToString(), index);
    }
}

/// <summary>
/// A menu overlay next to an anchor (1.x spec §7, §15). ↑ ↓ PgUp PgDn Home End and type-ahead move the selection;
/// an item's mnemonic chooses it; Enter or a click chooses an item; → or Enter opens a submenu and ← closes it;
/// Esc, Tab or a click outside dismisses the menu. Show it with <see cref="OverlayStack.ShowMenuAsync"/>, or with
/// <see cref="OverlayStack.Show"/> and await <see cref="Result"/> or <see cref="Chosen"/>.
/// </summary>
public sealed class PopupMenu : Overlay
{
    private static readonly TimeSpan TypeAheadReset = TimeSpan.FromSeconds(1);

    private readonly TaskCompletionSource<int?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<MenuItem?> _chosen = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MenuItem[] _items;
    private readonly Rect _anchor;
    private readonly PopupMenu? _parent;
    private readonly int _parentIndex;
    private int _selected;
    private int _offset;
    private int _viewport;
    private Rect _rows;
    private int? _choice;
    private MenuItem? _chosenItem;
    private PopupMenu? _child;
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

    // A submenu, beside its parent's row
    private PopupMenu(IReadOnlyList<MenuItem> items, Rect row, PopupMenu parent, int parentIndex) : this(items, row)
    {
        _parent = parent;
        _parentIndex = parentIndex;
        MaxRows = parent.MaxRows;
    }

    /// <summary>
    /// The chosen item's index, or null when the menu was dismissed or closed any other way. For an item chosen in a
    /// submenu, the index of the top-level item that holds it.
    /// </summary>
    public Task<int?> Result => _result.Task;

    /// <summary>The chosen item, at any depth of submenus, or null when the menu was dismissed. Completes with <see cref="Result"/>.</summary>
    public Task<MenuItem?> Chosen => _chosen.Task;

    /// <summary>The menu is at least this wide, border included. A select passes its field's width. Default 0.</summary>
    public int MinWidth { get; init; }

    /// <summary>At most this many item rows are shown; more scroll. Default 12.</summary>
    public int MaxRows { get; init; } = 12;

    /// <summary>The selected item's index, or -1 when no item is enabled.</summary>
    internal int Selected => _selected;

    /// <summary>The open submenu, or null.</summary>
    internal PopupMenu? Submenu => _child;

    /// <inheritdoc/>
    public override bool Framed => false;

    /// <inheritdoc/>
    public override bool CloseOnClickOutside => true;

    /// <inheritdoc/>
    public override string KeysGroup => "Menu";

    /// <summary>Called with the result once the menu has closed, on the UI thread (for <see cref="SelectState"/>).</summary>
    internal Action<int?>? Closed { get; init; }

    /// <summary>The menu bar this menu (or its root menu) was opened from, for ← → F10 and Alt+title (1.x spec §15.5).</summary>
    internal MenuBar? Bar
    {
        get => _bar ?? _parent?.Bar;
        init => _bar = value;
    }

    private readonly MenuBar? _bar;

    /// <summary>
    /// Below the anchor, or above it, shifted left to stay on the screen (1.x spec §7.4). A submenu goes to the right
    /// of its parent, or to the left when it doesn't fit, with its first row beside the parent's row (§15.4).
    /// </summary>
    public override Rect Placement(Rect screen)
    {
        var width = Math.Min(Math.Max(MinWidth, ContentWidth() + 2), screen.Width);
        var wanted = Math.Min(_items.Length, Math.Max(1, MaxRows)) + 2;
        if (_parent is null) return PlaceNear(_anchor, screen, width, wanted, wanted);

        var parent = _parent.Area;
        var x = parent.Right + width <= screen.Right ? parent.Right : Math.Max(screen.X, parent.X - width);
        var height = Math.Min(wanted, screen.Height);
        var y = Math.Clamp(_anchor.Y - 1, screen.Y, Math.Max(screen.Y, screen.Bottom - height));
        return new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>
    /// A rect <paramref name="width"/> wide below <paramref name="anchor"/>, or above it when <paramref name="wanted"/>
    /// rows fit only there or there's more room there, shifted left to stay on the screen. It's
    /// <paramref name="height"/> rows tall (at most <paramref name="wanted"/>), so a shrinking list keeps its side.
    /// </summary>
    internal static Rect PlaceNear(Rect anchor, Rect screen, int width, int wanted, int height)
    {
        // An anchor left off the screen by a resize is pulled back onto it
        var top = Math.Clamp(anchor.Y, screen.Y, screen.Bottom);
        var bottom = Math.Clamp(anchor.Bottom, top, screen.Bottom);
        var below = screen.Bottom - bottom;
        var above = top - screen.Y;
        var downward = wanted <= below || (wanted > above && below >= above);
        var rows = Math.Min(height, downward ? below : above);
        var y = downward ? bottom : top - rows;
        var x = Math.Clamp(anchor.X, screen.X, Math.Max(screen.X, screen.Right - width));
        return new Rect(x, y, Math.Max(0, width), Math.Max(0, rows));
    }

    /// <inheritdoc/>
    public override bool HandleKey(ConsoleKeyInfo key)
    {
        if (Bar is { } bar)
        {
            switch (key.Key)
            {
                case ConsoleKey.F10:
                    bar.Close();
                    return true;
                case ConsoleKey.LeftArrow when _parent is null:
                    bar.Step(-1);
                    return true;
                case ConsoleKey.RightArrow when _selected < 0 || !_items[_selected].HasSubmenu:
                    bar.Step(1);
                    return true;
            }
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Move(-1, wrap: true); return true;
            case ConsoleKey.DownArrow: Move(1, wrap: true); return true;
            case ConsoleKey.PageUp: Page(-1); return true;
            case ConsoleKey.PageDown: Page(1); return true;
            case ConsoleKey.Home: Select(Find(0, 1, wrap: false)); return true;
            case ConsoleKey.End: Select(Find(_items.Length - 1, -1, wrap: false)); return true;
            case ConsoleKey.Enter when KeyInput.IsEnter(key):
                if (_selected >= 0) Activate(_selected);
                return true;
            case ConsoleKey.RightArrow:
                if (_selected >= 0 && _items[_selected].HasSubmenu) OpenSubmenu(_selected);
                return true;
            case ConsoleKey.LeftArrow:
                if (_parent is not null) Close();
                return true;
            case ConsoleKey.Escape:
                Close();
                return true;
            case ConsoleKey.Tab:
                Dismiss();
                return true;
        }

        if (KeyInput.HasControl(key) || char.IsControl(key.KeyChar) || key.KeyChar == '\0') return true;
        if (Mnemonic(key.KeyChar)) return true;
        if ((key.Modifiers & ConsoleModifiers.Alt) == 0) TypeAhead(key.KeyChar);
        else Bar?.HandleKey(key); // Alt and another menu's title switches to it
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
            case MouseAction.Move:
                // 1.x spec §17.7: the selection follows the pointer over items it can choose; a redraw only when it moved
                if (!_rows.Contains(mouse.X, mouse.Y)) return false;
                var under = _offset + (mouse.Y - _rows.Y);
                if (under >= _items.Length || !_items[under].CanChoose || under == _selected) return false;
                _selected = under;
                return true;
        }

        if (!mouse.IsClick || !_rows.Contains(mouse.X, mouse.Y)) return true;
        var index = _offset + (mouse.Y - _rows.Y);
        if (index < _items.Length && _items[index].CanChoose)
        {
            _selected = index;
            Activate(index);
        }
        return true;
    }

    /// <inheritdoc/>
    public override void Render(CellBuffer buf, Rect area, RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(ctx);
        var theme = ctx.Theme;
        var inner = Widget.Block().BorderStyle(theme.ModalBorder).Padding(new Margin(0)).Draw(buf, area);
        _viewport = Math.Max(0, inner.Height);
        _offset = SelectionState.FitOffset(_offset, Math.Max(0, _selected), _items.Length, _viewport);
        var visible = Math.Clamp(_items.Length - _offset, 0, _viewport);
        _rows = inner with { Height = visible };
        if (inner.IsEmpty) return;

        var muted = theme.Text.Patch(theme.TextMuted);
        var marks = HasMarks ? 2 : 0;
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

            var style = item.Enabled ? theme.Text : muted;
            if (marks > 0 && Mark(item) is { } mark) buf.SetString(inner.X + 1, y, mark, style, 1);

            var hint = RightText(item);
            var hintWidth = Math.Min(UnicodeWidth.Of(hint), Math.Max(0, inner.Width - 2 - marks));
            var labelX = inner.X + 1 + marks;
            var labelWidth = Math.Max(0, inner.Width - 2 - marks - (hintWidth > 0 ? hintWidth + 2 : 0));
            var (text, mnemonic) = item.Display;
            var drawn = WidgetText.DrawString(buf, labelX, y, text, labelWidth, style);
            if (hintWidth > 0) WidgetText.DrawString(buf, inner.Right - 1 - hintWidth, y, hint, hintWidth, item.HasSubmenu ? style : muted);
            if (index == _selected) buf.SetStyle(new Rect(inner.X, y, inner.Width, 1), theme.Selection);

            // 1.x spec §15.3: the mnemonic underlined, when it was drawn
            if (mnemonic >= 0)
            {
                var column = UnicodeWidth.Of(text.AsSpan(0, mnemonic));
                var cell = Math.Max(1, UnicodeWidth.Of(text.AsSpan(mnemonic, 1)));
                if (column + cell <= drawn) buf.SetStyle(new Rect(labelX + column, y, cell, 1), new Style(Add: Modifiers.Underlined));
            }
        }

        if (_items.Length > _viewport)
        {
            new ScrollbarBuilder().ContentLength(_items.Length).ViewportLength(_viewport).Position(_offset)
                .Draw(buf, new Rect(area.Right - 1, inner.Y, 1, inner.Height));
        }
    }

    /// <inheritdoc/>
    protected override void OnUnmount()
    {
        if (_child is { } child)
        {
            _child = null;
            App.Overlays.Close(child);
        }
        _result.TrySetResult(_choice);
        _chosen.TrySetResult(_chosenItem);
        Closed?.Invoke(_choice);
        _parent?.SubmenuClosed(this);
    }

    private bool HasMarks => Array.Exists(_items, i => i.Checked is not null && !i.IsSeparator);

    internal static string? Mark(MenuItem item) => item.Checked switch
    {
        true when item.Radio => Glyphs.Basic ? "*" : "●",
        false when item.Radio => Glyphs.Basic ? "o" : "○",
        true => Glyphs.Basic ? "√" : "✓",
        _ => null,
    };

    // The hint, or the submenu arrow in its place
    internal static string RightText(MenuItem item) =>
        item.HasSubmenu ? (Glyphs.Basic ? "►" : "▸") : item.Hint is { Length: > 0 } h ? Sanitizer.SingleLine(h) : "";

    // " ✓ Label", two spaces, "Hint ": the widest of each
    private int ContentWidth()
    {
        var labels = 0;
        var hints = 0;
        foreach (var item in _items)
        {
            if (item.IsSeparator) continue;
            labels = Math.Max(labels, UnicodeWidth.Of(item.Display.Text));
            hints = Math.Max(hints, UnicodeWidth.Of(RightText(item)));
        }
        return (HasMarks ? 2 : 0) + labels + 2 + (hints > 0 ? hints + 2 : 0);
    }

    // Enter, a click or a mnemonic: open a submenu, or choose the item
    private void Activate(int index)
    {
        var item = _items[index];
        if (!item.CanChoose) return;
        if (item.HasSubmenu) OpenSubmenu(index);
        else Choose(index, item);
    }

    private void OpenSubmenu(int index)
    {
        var item = _items[index];
        if (!item.CanChoose || item.Submenu is not { } items) return;
        if (_child is { } open)
        {
            if (open._parentIndex == index) return;
            App.Overlays.Close(open);
        }
        _selected = index;
        var row = new Rect(Area.X, _rows.Y + index - _offset, Area.Width, 1);
        _child = new PopupMenu(items, row, this, index);
        App.Overlays.Show(_child);
    }

    // 1.x spec §15.4: close this menu and every menu it was opened from, then complete the results and run the item
    private void Choose(int index, MenuItem item)
    {
        var app = App;
        _choice = index;
        _chosenItem = item;
        Close();
        if (_parent is { } parent) parent.Choose(_parentIndex, item);
        else Run(app, item);
    }

    private void SubmenuClosed(PopupMenu child)
    {
        if (ReferenceEquals(_child, child)) _child = null;
    }

    private static void Run(App app, MenuItem item) => app.OnUiThread(() => item.Run?.Invoke());

    // Tab dismisses the whole chain
    private void Dismiss()
    {
        Close();
        _parent?.Dismiss();
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

    // 1.x spec §15.4: one item with the mnemonic is activated; several are cycled through
    private bool Mnemonic(char c)
    {
        var lower = char.ToLowerInvariant(c);
        var matches = 0;
        foreach (var item in _items)
            if (item.CanChoose && item.Mnemonic == lower) matches++;
        if (matches == 0) return false;

        var next = Find(_selected < 0 ? 0 : _selected + (matches > 1 ? 1 : 0), 1, wrap: true, item => item.Mnemonic == lower);
        if (next is not { } index) return false;
        _selected = index;
        if (matches == 1) Activate(index);
        return true;
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
        item.Display.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
