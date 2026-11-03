namespace MiniTui;

/// <summary>
/// The rows, expanded nodes, selection and scroll offset of a tree (1.x spec §11), drawn with
/// <see cref="Widget.Tree{T}"/>. <typeparamref name="T"/> is the app's own node type: the state asks for each
/// expanded node's children instead of holding a copy of the tree.
/// </summary>
public sealed class TreeState<T> where T : class
{
    private static readonly TimeSpan TypeAheadReset = TimeSpan.FromSeconds(1);

    private Func<T, IReadOnlyList<T>> _children;
    private Func<T, CancellationToken, Task<IReadOnlyList<T>>>? _loader;
    private readonly Dictionary<T, IReadOnlyList<T>> _loaded;
    private readonly Dictionary<T, CancellationTokenSource> _loading;
    private readonly Dictionary<T, Task> _loadTasks;
    private readonly Dictionary<T, string> _errors;
    private readonly HashSet<T> _checked;
    private readonly Dictionary<T, bool?> _checkCache;
    private readonly Func<T, string> _text;
    private readonly IEqualityComparer<T> _comparer;
    private readonly HashSet<T> _expanded;
    private readonly HashSet<T> _filterClosed;
    private Func<T, bool>? _filter;
    private IReadOnlyList<T> _roots = [];
    private List<TreeRow<T>> _rows = [];
    private T? _selected;
    private int _index = -1;
    private List<T> _ancestors = [];
    private Rect _mouseArea;
    private Rect _mouseRows;
    private int[] _expanderColumns = [];
    private int[] _checkColumns = [];
    private string _search = "";
    private long _lastTyped;

    /// <summary>Create a state.</summary>
    /// <param name="children">A node's children. Asked for only while the node is expanded.</param>
    /// <param name="text">A node's label: drawn by default, and searched by type-ahead.</param>
    /// <param name="comparer">How nodes are matched, for the expanded set and the selection.</param>
    public TreeState(Func<T, IReadOnlyList<T>> children, Func<T, string> text, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(text);
        _children = children;
        _text = text;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _expanded = new HashSet<T>(_comparer);
        _filterClosed = new HashSet<T>(_comparer);
        _loaded = new Dictionary<T, IReadOnlyList<T>>(_comparer);
        _loading = new Dictionary<T, CancellationTokenSource>(_comparer);
        _loadTasks = new Dictionary<T, Task>(_comparer);
        _errors = new Dictionary<T, string>(_comparer);
        _checked = new HashSet<T>(_comparer);
        _checkCache = new Dictionary<T, bool?>(_comparer);
    }

    // TreeState.Async: children from a loader instead of a function
    internal void UseLoader(Func<T, CancellationToken, Task<IReadOnlyList<T>>> loadChildren)
    {
        _loader = loadChildren;
        _children = node => _loaded.TryGetValue(node, out var children) ? children : [];
    }

    /// <summary>Draw a check box on each row, toggled by Space and a click on it (1.x spec §18.5). Off by default.</summary>
    public bool Checkboxes { get; set; }

    /// <summary>
    /// Whether a node is checked: for a node with children, true when they're all checked, false when none is, and
    /// null when some are; for one without (or with none loaded), whether it was checked.
    /// </summary>
    public bool? IsChecked(T node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return CheckOf(node, []);
    }

    /// <summary>Check or uncheck a node and everything under it, and raise <see cref="CheckedChanged"/>.</summary>
    public void SetChecked(T node, bool isChecked)
    {
        ArgumentNullException.ThrowIfNull(node);
        Mark(node, isChecked, []);
        _checkCache.Clear();
        CheckedChanged?.Invoke(this, new TreeNodeEventArgs<T>(node));
    }

    /// <summary>
    /// The checked nodes in tree order: each checked node without children and each parent whose descendants are all
    /// checked, without what's under it.
    /// </summary>
    public IReadOnlyList<T> CheckedNodes
    {
        get
        {
            var nodes = new List<T>();
            foreach (var root in _roots) CollectChecked(root, [], nodes);
            return nodes;
        }
    }

    /// <summary>Raised when Space, a click or <see cref="SetChecked"/> checks or unchecks a node (the one toggled).</summary>
    public event EventHandler<TreeNodeEventArgs<T>>? CheckedChanged;

    /// <summary>
    /// Forget a node's loaded children (and cancel a load that's running), so they load again when it's next expanded,
    /// or now if it's expanded. Does nothing for a state without a loader.
    /// </summary>
    public void Reload(T node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_loader is null) return;
        if (_loading.Remove(node, out var running)) running.Cancel();
        _loadTasks.Remove(node);
        _loaded.Remove(node);
        _errors.Remove(node);
        Refresh();
    }

    /// <summary>The top-level nodes. Setting them keeps the expanded nodes and the selection where they still match.</summary>
    public IReadOnlyList<T> Roots
    {
        get => _roots;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _roots = value;
            Refresh();
        }
    }

    /// <summary>
    /// Whether a node can expand, without reading its children; drawn as an expander. By default, whether its
    /// children aren't empty. Set it when reading children is slow, or to show an expander before they're loaded.
    /// </summary>
    public Func<T, bool>? HasChildren { get; set; }

    /// <summary>
    /// Show only the nodes this matches and their ancestors, drawn open so the matches show (1.x spec §18.4). The
    /// children of every node are read, expanded or not. ← and the expander can still close a node while filtering;
    /// that's forgotten when the filter changes, and the expanded nodes aren't touched. Null shows the whole tree.
    /// </summary>
    public Func<T, bool>? Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            _filterClosed.Clear();
            Refresh();
        }
    }

    /// <summary>
    /// Expand each node of <paramref name="path"/> but the last, select the last, and scroll it into view at the next
    /// draw (1.x spec §18.3). The path starts at a root, and each node must be among the previous one's children.
    /// </summary>
    /// <returns>False, changing nothing, when the path doesn't match the tree or the filter hides its last node.</returns>
    public bool Reveal(IReadOnlyList<T> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0 || !_roots.Any(r => _comparer.Equals(r, path[0]))) return false;
        for (var i = 1; i < path.Count; i++)
        {
            if (!_children(path[i - 1]).Any(c => _comparer.Equals(c, path[i]))) return false;
        }
        if (_filter is not null && !Shown(path[^1], [.. path.Take(path.Count - 1)])) return false;

        var added = new List<T>();
        for (var i = 0; i < path.Count - 1; i++)
        {
            _filterClosed.Remove(path[i]);
            if (_expanded.Add(path[i])) added.Add(path[i]);
        }
        Refresh();
        foreach (var node in added) Expanded?.Invoke(this, new TreeNodeEventArgs<T>(node));
        _reveal = true;
        Move(IndexOf(path[^1]));
        return true;
    }

    /// <summary>
    /// As <see cref="Reveal"/>, after loading the children of each node on the path that aren't loaded yet, for a
    /// state from <see cref="TreeState.Async"/> (1.x spec §18.3). Without a loader it's <see cref="Reveal"/>.
    /// </summary>
    /// <returns>False when the path doesn't match the tree (as far as it loaded) or the filter hides its node.</returns>
    public async Task<bool> RevealAsync(IReadOnlyList<T> path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0 || !_roots.Any(r => _comparer.Equals(r, path[0]))) return false;
        for (var i = 0; _loader is not null && i < path.Count - 1; i++)
        {
            if (i > 0 && !_children(path[i - 1]).Any(c => _comparer.Equals(c, path[i]))) return false;
            if (_loaded.ContainsKey(path[i])) continue;
            _errors.Remove(path[i]);
            StartLoad(path[i]);
            if (_loadTasks.TryGetValue(path[i], out var load)) await load.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        return Reveal(path);
    }

    /// <summary>
    /// The selected node, or null for none (the first row is selected when the rows are next computed). Setting it
    /// doesn't raise <see cref="SelectionChanged"/> or expand anything; a node that isn't in <see cref="Rows"/> is
    /// replaced when the rows are next computed.
    /// </summary>
    public T? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            _reveal = true;
        }
    }

    /// <summary>
    /// The first visible row. Adjusted when drawn so <see cref="Selected"/> is visible after the selection moves;
    /// the wheel and the scrollbar scroll without moving the selection (1.x spec §14.4).
    /// </summary>
    public int Offset { get; set; }

    /// <summary>How many rows were visible at the last draw. Used by PgUp and PgDn.</summary>
    public int ViewportHeight { get; private set; }

    /// <summary>
    /// The rows the tree shows: every root and, depth first, the children of each expanded node. Computed when
    /// drawn, on each key and mouse event, and when the roots or the expanded nodes change (1.x spec §11.4).
    /// </summary>
    public IReadOnlyList<TreeRow<T>> Rows => _rows;

    /// <summary>Raised on the UI thread when the user moves the selection (not when code sets it).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when Enter is pressed on a node without children.</summary>
    public event EventHandler<TreeNodeEventArgs<T>>? Activated;

    /// <summary>Raised when a node expands, from a key, a click or code. Lazy loaders start loading here.</summary>
    public event EventHandler<TreeNodeEventArgs<T>>? Expanded;

    /// <summary>Raised when a node collapses, from a key, a click or code.</summary>
    public event EventHandler<TreeNodeEventArgs<T>>? Collapsed;

    /// <summary>Whether <paramref name="node"/> is expanded.</summary>
    public bool IsExpanded(T node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _expanded.Contains(node);
    }

    /// <summary>Expand a node. Does nothing for a node without children or one already expanded.</summary>
    public void Expand(T node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _errors.Remove(node);
        if (!NodeHasChildren(node) || !_expanded.Add(node)) return;
        Refresh();
        Expanded?.Invoke(this, new TreeNodeEventArgs<T>(node));
    }

    /// <summary>Collapse a node. If the selection was inside it, the node is selected.</summary>
    public void Collapse(T node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_expanded.Remove(node)) return;
        Refresh();
        Collapsed?.Invoke(this, new TreeNodeEventArgs<T>(node));
    }

    /// <summary>Expand a collapsed node or collapse an expanded one.</summary>
    public void Toggle(T node)
    {
        if (IsExpanded(node)) Collapse(node);
        else Expand(node);
    }

    /// <summary>Expand every node that has children. This reads the whole tree.</summary>
    public void ExpandAll()
    {
        var added = new List<T>();
        var path = new List<T>();
        foreach (var root in _roots) ExpandAll(root, path, added);
        Refresh();
        foreach (var node in added) Expanded?.Invoke(this, new TreeNodeEventArgs<T>(node));
    }

    /// <summary>Collapse every node.</summary>
    public void CollapseAll()
    {
        var removed = _expanded.ToList();
        _expanded.Clear();
        Refresh();
        foreach (var node in removed) Collapsed?.Invoke(this, new TreeNodeEventArgs<T>(node));
    }

    /// <summary>
    /// Handle ↑ ↓ PgUp PgDn Home End, → (expand, or the first child), ← (collapse, or the parent), Enter (toggle,
    /// or <see cref="Activated"/> on a leaf) and type-ahead.
    /// </summary>
    /// <returns>True if the key was one of these.</returns>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        var handled = HandleKeyCore(key);
        if (handled) _reveal = true;
        return handled;
    }

    private bool HandleKeyCore(ConsoleKeyInfo key)
    {
        Refresh();
        var count = _rows.Count;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Move(_index - 1); return true;
            case ConsoleKey.DownArrow: Move(_index + 1); return true;
            case ConsoleKey.PageUp: Move(_index - Page); return true;
            case ConsoleKey.PageDown: Move(_index + Page); return true;
            case ConsoleKey.Home: Move(0); return true;
            case ConsoleKey.End: Move(count - 1); return true;
            case ConsoleKey.RightArrow: Right(); return true;
            case ConsoleKey.LeftArrow: Left(); return true;
            case ConsoleKey.Enter when KeyInput.IsEnter(key): Enter(); return true;
        }

        if (Checkboxes && key.Key == ConsoleKey.Spacebar && !KeyInput.HasCommandModifier(key))
        {
            ToggleCheck();
            return true;
        }
        if (KeyInput.HasCommandModifier(key) || char.IsControl(key.KeyChar) || key.KeyChar == ' ') return false;
        TypeAhead(key.KeyChar);
        return true;
    }

    /// <summary>
    /// Handle the mouse over the tree drawn last: the wheel scrolls by one row, a left press on a row selects it
    /// and, on its expander, also toggles it, and the scrollbar's thumb drags and its track pages (1.x spec §14.4).
    /// A double-click on a row activates it as Enter does: a leaf raises <see cref="Activated"/>, a parent expands or
    /// collapses (§17.4). Scrolling doesn't move the selection.
    /// </summary>
    /// <returns>True if the event did any of these.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        var offset = Offset;
        if (_bar.Handle(mouse, ref offset))
        {
            Offset = offset;
            return true;
        }
        if (!_mouseArea.Contains(mouse.X, mouse.Y)) return false;
        Refresh();
        switch (mouse.Action)
        {
            case MouseAction.ScrollUp: Offset = SelectionState.ClampOffset(Offset - 1, _rows.Count, ViewportHeight); return true;
            case MouseAction.ScrollDown: Offset = SelectionState.ClampOffset(Offset + 1, _rows.Count, ViewportHeight); return true;
        }

        if (!mouse.IsClick || !_mouseRows.Contains(mouse.X, mouse.Y)) return false;
        var visible = mouse.Y - _mouseRows.Y;
        var index = Offset + visible;
        if (index >= _rows.Count) return false;
        Move(index);
        _reveal = true;
        if (Checkboxes && visible < _checkColumns.Length && _checkColumns[visible] is var check and >= 0 && mouse.X >= check && mouse.X < check + 3)
        {
            if (mouse.ClickCount <= 1) ToggleCheck();
            return true;
        }
        var onExpander = visible < _expanderColumns.Length && _expanderColumns[visible] == mouse.X && _rows[index].HasChildren;
        // The first press of a double-click on the expander already toggled it; later presses don't toggle it back
        if (onExpander && mouse.ClickCount <= 1) ToggleRow(_rows[index]);
        else if (!onExpander && mouse.ClickCount == 2) Enter();
        return true;
    }

    /// <summary>Called by the widget while drawing: compute the rows, then scroll so the selection is visible.</summary>
    internal void Fit(int viewportHeight)
    {
        Refresh();
        ViewportHeight = Math.Max(0, viewportHeight);
        Offset = _reveal
            ? SelectionState.FitOffset(Offset, Math.Max(0, _index), _rows.Count, ViewportHeight)
            : SelectionState.ClampOffset(Offset, _rows.Count, ViewportHeight);
        _reveal = false;
    }

    /// <summary>
    /// Called by the widget while drawing: its area, the rows its nodes are drawn in, and the column of each visible
    /// row's expander (-1 where it isn't drawn).
    /// </summary>
    internal void Track(Rect area, Rect rows, int[] expanderColumns, int[]? checkColumns = null)
    {
        _mouseArea = area;
        _mouseRows = rows;
        _expanderColumns = expanderColumns;
        _checkColumns = checkColumns ?? [];
        _bar.Track(default, 0, 0);
    }

    /// <summary>Called by the widget while drawing, after <see cref="Track"/>: where the scrollbar was drawn.</summary>
    internal void TrackScrollbar(Rect bar) => _bar.Track(bar, _rows.Count, ViewportHeight);

    private readonly ScrollbarDrag _bar = new();

    // Set when the selection moves, so the next draw scrolls it into view
    private bool _reveal = true;

    internal string TextOf(T node) => _text(node) ?? "";

    /// <summary>Overrides the app's clock for type-ahead in tests.</summary>
    internal TimeProvider? Time { get; set; }

    private int Page => Math.Max(1, ViewportHeight);

    // With a loader, a node that hasn't loaded may have children
    private bool NodeHasChildren(T node) =>
        HasChildren?.Invoke(node) ?? (_loader is not null && !_loaded.ContainsKey(node) || _children(node).Count > 0);

    /// <summary>For the widget: "Loading…" while a node's children load, or the error the last load threw.</summary>
    internal (bool Loading, string? Error) LoadStatus(T node) =>
        (_loading.ContainsKey(node), _errors.TryGetValue(node, out var error) ? error : null);

    // 1.x spec §18.6: an expanded node with a loader and nothing loaded starts loading; one load at a time per node
    private void StartLoad(T node)
    {
        if (_loader is null || _loaded.ContainsKey(node) || _loading.ContainsKey(node) || _errors.ContainsKey(node)) return;
        var cancel = new CancellationTokenSource();
        _loading[node] = cancel;
        var app = App.Current;
        _loadTasks[node] = app is not null
            ? app.RunTask(ct => LoadAsync(node, cancel, ct))
            : LoadAsync(node, cancel, CancellationToken.None);
    }

    private async Task LoadAsync(T node, CancellationTokenSource cancel, CancellationToken stopping)
    {
        using var both = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token, stopping);
        try
        {
            var children = await _loader!(node, both.Token);
            if (!IsCurrentLoad(node, cancel)) return;
            _loading.Remove(node);
            _loaded[node] = children ?? [];
            // A parent checked before its children were known checks them now
            if (_checked.Contains(node)) foreach (var child in _loaded[node]) Mark(child, true, []);
            _checkCache.Clear();
        }
        catch (OperationCanceledException) when (both.IsCancellationRequested)
        {
            if (IsCurrentLoad(node, cancel)) _loading.Remove(node);
        }
        catch (Exception e)
        {
            if (!IsCurrentLoad(node, cancel)) return;
            _loading.Remove(node);
            _errors[node] = e.Message;
            if (_expanded.Remove(node)) Collapsed?.Invoke(this, new TreeNodeEventArgs<T>(node));
        }
        finally
        {
            cancel.Dispose();
        }
        Refresh();
        App.Current?.RequestRedraw();
    }

    private bool IsCurrentLoad(T node, CancellationTokenSource cancel) =>
        _loading.TryGetValue(node, out var current) && ReferenceEquals(current, cancel);

    // 1.x spec §18.5: a node with children is checked when they all are, unchecked when none is, and partly otherwise
    private bool? CheckOf(T node, List<T> path)
    {
        if (_checkCache.TryGetValue(node, out var cached)) return cached;
        var children = path.Exists(a => _comparer.Equals(a, node)) ? [] : _children(node);
        bool? state;
        if (children.Count == 0)
        {
            state = _checked.Contains(node);
        }
        else
        {
            path.Add(node);
            bool any = false, all = true;
            foreach (var child in children)
            {
                var c = CheckOf(child, path);
                any |= c != false;
                all &= c == true;
            }
            path.RemoveAt(path.Count - 1);
            state = all ? true : any ? null : false;
        }
        _checkCache[node] = state;
        return state;
    }

    private void Mark(T node, bool isChecked, List<T> path)
    {
        if (isChecked) _checked.Add(node);
        else _checked.Remove(node);
        if (path.Exists(a => _comparer.Equals(a, node))) return;
        path.Add(node);
        foreach (var child in _children(node)) Mark(child, isChecked, path);
        path.RemoveAt(path.Count - 1);
    }

    private void CollectChecked(T node, List<T> path, List<T> nodes)
    {
        var state = CheckOf(node, path);
        if (state == true)
        {
            nodes.Add(node);
            return;
        }
        if (state == false || path.Exists(a => _comparer.Equals(a, node))) return;
        path.Add(node);
        foreach (var child in _children(node)) CollectChecked(child, path, nodes);
        path.RemoveAt(path.Count - 1);
    }

    // Space or a click on the box: a checked node unchecks, an unchecked or partly checked one checks
    private void ToggleCheck()
    {
        if (_index < 0) return;
        var node = _rows[_index].Node;
        SetChecked(node, IsChecked(node) != true);
    }

    /// <summary>Walk the expanded part of the tree, then find the selection in it (1.x spec §11.4).</summary>
    private void Refresh()
    {
        _checkCache.Clear();
        var rows = new List<TreeRow<T>>();
        var path = new List<T>();
        foreach (var root in _roots)
        {
            if (_filter is null) Walk(root, null, path, rows);
            else WalkFiltered(root, null, path, rows);
        }
        _rows = rows;
        KeepSelection();
    }

    private void Walk(T node, T? parent, List<T> path, List<TreeRow<T>> rows)
    {
        var cycle = path.Exists(ancestor => _comparer.Equals(ancestor, node));
        var hasChildren = !cycle && NodeHasChildren(node);
        var expanded = hasChildren && _expanded.Contains(node);
        rows.Add(new TreeRow<T>(node, parent, path.Count, hasChildren, expanded));
        if (!expanded) return;
        StartLoad(node);

        path.Add(node);
        foreach (var child in _children(node)) Walk(child, node, path, rows);
        path.RemoveAt(path.Count - 1);
    }

    // While filtering, opening and closing a row only changes what the filter shows (1.x spec §18.4)
    private void ToggleRow(TreeRow<T> row)
    {
        if (_filter is null)
        {
            Toggle(row.Node);
            return;
        }
        if (!_filterClosed.Remove(row.Node)) _filterClosed.Add(row.Node);
        Refresh();
    }

    // Whether the filter shows a node: it matches, or something under it does
    private bool Shown(T node, List<T> path)
    {
        if (_filter!(node)) return true;
        if (path.Exists(ancestor => _comparer.Equals(ancestor, node))) return false;
        path.Add(node);
        var shown = _children(node).Any(child => Shown(child, path));
        path.RemoveAt(path.Count - 1);
        return shown;
    }

    // The filtered walk: a node's row, then its shown children's when it's open; false when nothing here is shown
    private bool WalkFiltered(T node, T? parent, List<T> path, List<TreeRow<T>> rows)
    {
        if (path.Exists(ancestor => _comparer.Equals(ancestor, node))) return false;
        var matches = _filter!(node);
        var children = new List<TreeRow<T>>();
        path.Add(node);
        foreach (var child in _children(node)) WalkFiltered(child, node, path, children);
        path.RemoveAt(path.Count - 1);
        if (!matches && children.Count == 0) return false;

        var hasChildren = children.Count > 0;
        var open = hasChildren && !_filterClosed.Contains(node);
        rows.Add(new TreeRow<T>(node, parent, path.Count, hasChildren, open));
        if (open) rows.AddRange(children);
        return true;
    }

    private void ExpandAll(T node, List<T> path, List<T> added)
    {
        if (path.Exists(ancestor => _comparer.Equals(ancestor, node)) || !NodeHasChildren(node)) return;
        if (_expanded.Add(node)) added.Add(node);
        path.Add(node);
        foreach (var child in _children(node)) ExpandAll(child, path, added);
        path.RemoveAt(path.Count - 1);
    }

    private void KeepSelection()
    {
        var index = _selected is null ? (_rows.Count > 0 ? 0 : -1) : IndexOf(_selected);
        for (var i = 0; index < 0 && _selected is not null && i < _ancestors.Count; i++) index = IndexOf(_ancestors[i]);
        if (index < 0 && _rows.Count > 0) index = Math.Clamp(_index, 0, _rows.Count - 1);
        if (index != _index) _reveal = true;
        SetIndex(index);
    }

    private int IndexOf(T node) => _rows.FindIndex(row => _comparer.Equals(row.Node, node));

    // The selected row, its node, and its ancestors (nearest first), read back from the rows
    private void SetIndex(int index)
    {
        _index = index;
        _selected = index >= 0 ? _rows[index].Node : null;
        _ancestors = [];
        if (index < 0) return;
        var depth = _rows[index].Depth;
        for (var i = index - 1; i >= 0 && depth > 0; i--)
        {
            if (_rows[i].Depth >= depth) continue;
            _ancestors.Add(_rows[i].Node);
            depth = _rows[i].Depth;
        }
    }

    private void Move(int index)
    {
        if (_rows.Count == 0) return;
        index = Math.Clamp(index, 0, _rows.Count - 1);
        if (index == _index) return;
        SetIndex(index);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Right()
    {
        if (_index < 0) return;
        var row = _rows[_index];
        if (row.HasChildren && !row.IsExpanded) ToggleRow(row);
        else if (row.IsExpanded && _index + 1 < _rows.Count && _rows[_index + 1].Depth > row.Depth) Move(_index + 1);
    }

    private void Left()
    {
        if (_index < 0) return;
        var row = _rows[_index];
        if (row.IsExpanded) ToggleRow(row);
        else if (row.Parent is { } parent) Move(IndexOf(parent));
    }

    private void Enter()
    {
        if (_index < 0) return;
        var row = _rows[_index];
        if (row.HasChildren) ToggleRow(row);
        else Activated?.Invoke(this, new TreeNodeEventArgs<T>(row.Node));
    }

    // As PopupMenu (1.x spec §7.3): a prefix cleared 1 s after the last character; the same character again moves on
    private void TypeAhead(char c)
    {
        if (_rows.Count == 0) return;
        var time = Time ?? App.Current?.Time ?? TimeProvider.System;
        if (_search.Length > 0 && time.GetElapsedTime(_lastTyped) >= TypeAheadReset) _search = "";
        _lastTyped = time.GetTimestamp();
        _search += c;

        var first = _search[0];
        var repeated = _search.Length > 1 && _search.All(ch => char.ToUpperInvariant(ch) == char.ToUpperInvariant(first));
        var match = repeated ? Find(_index + 1, first.ToString()) : Find(_index, _search);
        if (match >= 0) Move(match);
    }

    private int Find(int start, string prefix)
    {
        for (var n = 0; n < _rows.Count; n++)
        {
            var i = (Math.Max(0, start) + n) % _rows.Count;
            if (TextOf(_rows[i].Node).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}

/// <summary>Creating a <see cref="TreeState{T}"/> whose children load asynchronously.</summary>
public static class TreeState
{
    /// <summary>
    /// Create a state whose children are loaded asynchronously (1.x spec §18.6): when a node first expands, its label
    /// shows "Loading…" until <paramref name="loadChildren"/> finishes, and the children are kept until
    /// <see cref="TreeState{T}.Reload"/>. A load that throws shows its message on the row and collapses the node.
    /// </summary>
    /// <param name="loadChildren">Loads a node's children. Runs through <see cref="App.RunTask"/> while an app is running.</param>
    /// <param name="text">A node's label: drawn by default, and searched by type-ahead.</param>
    /// <param name="comparer">How nodes are matched, for the expanded set, the selection and the loaded children.</param>
    public static TreeState<T> Async<T>(Func<T, CancellationToken, Task<IReadOnlyList<T>>> loadChildren, Func<T, string> text, IEqualityComparer<T>? comparer = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(loadChildren);
        var state = new TreeState<T>(_ => [], text, comparer);
        state.UseLoader(loadChildren);
        return state;
    }
}

/// <summary>One visible row of a tree: a node, its parent (null for a root) and its depth (0 for a root).</summary>
public readonly record struct TreeRow<T>(T Node, T? Parent, int Depth, bool HasChildren, bool IsExpanded)
    where T : class;

/// <summary>The node a tree event is about.</summary>
public sealed class TreeNodeEventArgs<T> : EventArgs where T : class
{
    /// <summary>Create the arguments.</summary>
    public TreeNodeEventArgs(T node) => Node = node;

    /// <summary>The node.</summary>
    public T Node { get; }
}
