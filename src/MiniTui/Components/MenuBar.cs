namespace MiniTui;

/// <summary>A menu in a <see cref="MenuBar"/> (1.x spec §15.2).</summary>
public sealed class Menu
{
    private readonly Func<IReadOnlyList<MenuItem>> _items;

    /// <summary>A menu with fixed items.</summary>
    public Menu(string title, IReadOnlyList<MenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(items);
        Title = title;
        _items = () => items;
    }

    /// <summary>A menu whose items are built each time it opens, so <see cref="MenuItem.Checked"/> and <see cref="MenuItem.Enabled"/> can follow the app's state.</summary>
    public Menu(string title, Func<IReadOnlyList<MenuItem>> items)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(items);
        Title = title;
        _items = items;
    }

    /// <summary>The title, with an optional <c>&amp;</c> mnemonic: <c>"&amp;File"</c>.</summary>
    public string Title { get; }

    /// <summary>The items, built now.</summary>
    public IReadOnlyList<MenuItem> Items() => _items() ?? [];

    /// <summary>The title as drawn, and its mnemonic's index in it or -1.</summary>
    internal (string Text, int Index) Display => MenuItem.ParseDisplay(Sanitizer.SingleLine(Title));

    internal char? Mnemonic => new MenuItem(Title).Mnemonic;
}

/// <summary>
/// A row of menus along the top of the screen (1.x spec §15.5). F10 opens the first menu, Alt and a title's
/// mnemonic opens that one, and a click on a title opens or closes it; ← → move between the menus. Set it with
/// <see cref="App.WithMenuBar"/>.
/// </summary>
public sealed class MenuBar
{
    private readonly Menu[] _menus;
    private readonly List<(int Index, Rect Rect)> _titles = [];
    private App? _app;
    private PopupMenu? _open;
    private int _openIndex = -1;

    /// <summary>Create a bar with <paramref name="menus"/>, left to right.</summary>
    /// <exception cref="ArgumentException"><paramref name="menus"/> contains null.</exception>
    public MenuBar(params Menu[] menus)
    {
        ArgumentNullException.ThrowIfNull(menus);
        _menus = [.. menus];
        if (Array.IndexOf(_menus, null) >= 0) throw new ArgumentException("The menus can't contain null.", nameof(menus));
    }

    /// <summary>The menus, left to right.</summary>
    public IReadOnlyList<Menu> Menus => _menus;

    /// <summary>True while one of its menus (or a submenu of one) is open.</summary>
    public bool IsOpen => _open is not null;

    /// <summary>The open menu's index, or -1.</summary>
    internal int OpenIndex => _open is null ? -1 : _openIndex;

    /// <summary>The open top-level menu, for tests.</summary>
    internal PopupMenu? OpenMenu => _open;

    /// <summary>
    /// Open the menu at <paramref name="index"/>, as F10 opens the first. Does nothing while one is open, while
    /// another overlay is open, before the bar is drawn, or when the index is out of range.
    /// </summary>
    public void Open(int index = 0)
    {
        if (_app is not { } app) return;
        app.OnUiThread(() =>
        {
            if (_open is not null || app.Overlays.Any) return;
            Show(app, index);
        });
    }

    internal void Attach(App? app) => _app = app;

    /// <summary>Close the open menus.</summary>
    internal void Close()
    {
        if (_open is { } menu && _app is { } app) app.Overlays.Close(menu);
    }

    /// <summary>← and → in a bar menu (and → on a submenu item without one): the previous or next menu, wrapping.</summary>
    internal void Step(int direction)
    {
        if (_app is not { } app || _open is null || _menus.Length == 0) return;
        var next = ((_openIndex + direction) % _menus.Length + _menus.Length) % _menus.Length;
        Switch(app, next);
    }

    /// <summary>Alt+mnemonic: open (or, with a menu open, switch to) the menu whose title has it.</summary>
    /// <returns>True if a title has the mnemonic.</returns>
    internal bool HandleKey(ConsoleKeyInfo key)
    {
        if (_app is not { } app) return false;
        if ((key.Modifiers & ConsoleModifiers.Alt) == 0 || KeyInput.HasControl(key) || char.IsControl(key.KeyChar)) return false;
        var lower = char.ToLowerInvariant(key.KeyChar);
        var index = Array.FindIndex(_menus, m => m.Mnemonic == lower);
        if (index < 0) return false;
        if (_open is not null) Switch(app, index);
        else if (!app.Overlays.Any) Show(app, index);
        return true;
    }

    /// <summary>A press on the bar row: open, switch or close by the title under it.</summary>
    /// <returns>True if the bar took the press.</returns>
    internal bool HandleMouse(MouseEvent mouse, Rect row)
    {
        if (_app is not { } app || !row.Contains(mouse.X, mouse.Y)) return false;
        if (mouse.Action == MouseAction.Move)
        {
            // 1.x spec §17.7: with a menu open, moving over another title switches to it
            if (_open is null || _titles.Find(t => t.Rect.Contains(mouse.X, mouse.Y)) is not { Rect.Width: > 0 } over || over.Index == _openIndex) return false;
            Switch(app, over.Index);
            return true;
        }
        if (mouse.Action != MouseAction.Press) return false;
        if (app.Overlays.Any && _open is null) return false;
        var hit = _titles.FindIndex(t => t.Rect.Contains(mouse.X, mouse.Y));
        if (hit < 0 || mouse.Button != MouseButton.Left)
        {
            Close();
            return true;
        }
        var index = _titles[hit].Index;
        if (_open is not null && index == _openIndex) Close();
        else if (_open is not null) Switch(app, index);
        else Show(app, index);
        return true;
    }

    /// <summary>Draw the bar on <paramref name="row"/>, recording each title's rect.</summary>
    internal void Draw(CellBuffer buf, Rect row)
    {
        var theme = Theme.Current;
        WidgetText.Clear(buf, row);
        buf.SetStyle(row, theme.Text.Patch(theme.StatusBar));
        _titles.Clear();
        var x = row.X;
        // 1.x spec §17.7: with no menu (or other overlay) open, the title under the pointer shows hover
        var pointer = _open is null && _app is { } app && !app.Overlays.Any ? app.BarPointer(row) : null;
        for (var i = 0; i < _menus.Length; i++)
        {
            var (text, mnemonic) = _menus[i].Display;
            var width = UnicodeWidth.Of(text) + 2;
            if (x + width > row.Right) break;
            var rect = new Rect(x, row.Y, width, 1);
            var style = i == OpenIndex ? theme.Text.Patch(theme.Selection) : theme.Text.Patch(theme.StatusBar);
            if (pointer is { } p && rect.Contains(p.X, p.Y)) style = style.Patch(theme.Hover);
            buf.SetStyle(rect, style);
            buf.SetString(x + 1, row.Y, text, style, width - 2);
            if (mnemonic >= 0)
            {
                var column = UnicodeWidth.Of(text.AsSpan(0, mnemonic));
                buf.SetStyle(new Rect(x + 1 + column, row.Y, Math.Max(1, UnicodeWidth.Of(text.AsSpan(mnemonic, 1))), 1), new Style(Add: Modifiers.Underlined));
            }
            _titles.Add((i, rect));
            x += width;
        }
    }

    /// <summary>
    /// The palette's items (1.x spec §15.6): every enabled item with a <see cref="MenuItem.Run"/> and no submenu,
    /// at any depth, in menu order, with its path as the detail.
    /// </summary>
    internal IEnumerable<PaletteItem> PaletteItems()
    {
        foreach (var menu in _menus)
            foreach (var item in Leaves(menu.Items(), menu.Display.Text))
                yield return item;
    }

    private static IEnumerable<PaletteItem> Leaves(IReadOnlyList<MenuItem> items, string path)
    {
        foreach (var item in items)
        {
            if (!item.CanChoose) continue;
            var text = item.Display.Text;
            if (item.Submenu is { } sub)
            {
                foreach (var leaf in Leaves(sub, $"{path} › {text}")) yield return leaf;
            }
            else if (item.Run is { } run)
            {
                yield return new PaletteItem(text, run) { Detail = path, Hint = item.Hint };
            }
        }
    }

    private void Switch(App app, int index)
    {
        if (index == _openIndex && _open is not null) return;
        Close();
        Show(app, index);
    }

    private void Show(App app, int index)
    {
        if (index < 0 || index >= _menus.Length) return;
        var title = _titles.Find(t => t.Index == index);
        if (title.Rect.IsEmpty) return;
        PopupMenu? menu = null;
        menu = new PopupMenu(_menus[index].Items(), title.Rect)
        {
            Bar = this,
            Closed = _ =>
            {
                if (!ReferenceEquals(_open, menu)) return;
                _open = null;
                _openIndex = -1;
            },
        };
        _open = menu;
        _openIndex = index;
        app.Overlays.Show(menu);
    }
}
