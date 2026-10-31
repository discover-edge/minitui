namespace MiniTui;

/// <summary>
/// The options and choice of a dropdown field (1.x spec §7.5), drawn with <see cref="Widget.Select"/>. Opening it
/// shows a <see cref="PopupMenu"/> of the options below the field.
/// </summary>
public sealed class SelectState
{
    private IReadOnlyList<string> _options;
    private int? _selected;
    private Rect _field;
    private PopupMenu? _menu;

    /// <summary>Create a state with options and an optional choice.</summary>
    public SelectState(IReadOnlyList<string>? options = null, int? selected = null)
    {
        _options = options ?? [];
        Selected = selected;
    }

    /// <summary>The options. Setting them keeps <see cref="Selected"/> if it's still in range, otherwise clears it.</summary>
    public IReadOnlyList<string> Options
    {
        get => _options;
        set
        {
            _options = value ?? [];
            if (_selected >= _options.Count) _selected = null;
        }
    }

    /// <summary>The chosen option's index, or null for none (the placeholder shows). Clamped to the options when set.</summary>
    public int? Selected
    {
        get => _selected;
        set => _selected = value is { } i && _options.Count > 0 ? Math.Clamp(i, 0, _options.Count - 1) : null;
    }

    /// <summary>The chosen option, or null.</summary>
    public string? Value => _selected is { } i && i < _options.Count ? _options[i] : null;

    /// <summary>True while its menu is open.</summary>
    public bool IsOpen => _menu is not null;

    /// <summary>Raised on the UI thread when the user's choice changes <see cref="Selected"/> (not when code sets it).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Show the options in a <see cref="PopupMenu"/> below the field from the last draw, as wide as it, with the
    /// current choice selected. Does nothing while open or with no options.
    /// </summary>
    public void Open(App app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (_menu is not null || _options.Count == 0) return;
        var items = new MenuItem[_options.Count];
        for (var i = 0; i < items.Length; i++) items[i] = new MenuItem(_options[i] ?? "");
        _menu = new PopupMenu(items, _field, _selected ?? 0) { MinWidth = _field.Width, Closed = OnClosed };
        app.Overlays.Show(_menu);
    }

    /// <summary>Enter, Space, ↑, ↓, Alt+↓ and F4 open the menu. Returns true if it opened.</summary>
    public bool HandleKey(ConsoleKeyInfo key, App app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var plain = (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        var opens = key.Key switch
        {
            ConsoleKey.Enter or ConsoleKey.Spacebar or ConsoleKey.UpArrow or ConsoleKey.F4 => plain,
            ConsoleKey.DownArrow => plain || key.Modifiers == ConsoleModifiers.Alt,
            _ => plain && key.KeyChar == ' ',
        };
        if (!opens || _options.Count == 0 || _menu is not null) return false;
        Open(app);
        return true;
    }

    /// <summary>A left press on the field from the last draw opens the menu. Returns true if it opened.</summary>
    public bool HandleMouse(MouseEvent mouse, App app)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        ArgumentNullException.ThrowIfNull(app);
        if (!mouse.IsClick || !_field.Contains(mouse.X, mouse.Y) || _options.Count == 0 || _menu is not null) return false;
        Open(app);
        return true;
    }

    /// <summary>Called by the widget while drawing: the field's area, block included.</summary>
    internal void Track(Rect field) => _field = field;

    private void OnClosed(int? choice)
    {
        _menu = null;
        if (choice is not { } i || i >= _options.Count || i == _selected) return;
        _selected = i;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
