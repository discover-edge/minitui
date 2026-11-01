namespace MiniTui;

/// <summary>
/// The rows, expanded nodes, selection and scroll offset of a tree (1.x spec §11), drawn with
/// <see cref="Widget.Tree{T}"/>. <typeparamref name="T"/> is the app's own node type: the state asks for each
/// expanded node's children instead of holding a copy of the tree.
/// </summary>
public sealed class TreeState<T> where T : class
{
    private static readonly TimeSpan TypeAheadReset = TimeSpan.FromSeconds(1);

    private readonly Func<T, IReadOnlyList<T>> _children;
    private readonly Func<T, string> _text;
    private readonly IEqualityComparer<T> _comparer;
    private readonly HashSet<T> _expanded;
    private IReadOnlyList<T> _roots = [];
    private List<TreeRow<T>> _rows = [];
    private T? _selected;
    private int _index = -1;
    private List<T> _ancestors = [];
    private Rect _mouseArea;
    private Rect _mouseRows;
    private int[] _expanderColumns = [];
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
    /// The selected node, or null for none (the first row is selected when the rows are next computed). Setting it
    /// doesn't raise <see cref="SelectionChanged"/> or expand anything; a node that isn't in <see cref="Rows"/> is
    /// replaced when the rows are next computed.
    /// </summary>
    public T? Selected
    {
        get => _selected;
        set => _selected = value;
    }

    /// <summary>The first visible row. Adjusted when drawn so <see cref="Selected"/> is visible.</summary>
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
            case ConsoleKey.Enter: Enter(); return true;
        }

        if (KeyInput.HasCommandModifier(key) || char.IsControl(key.KeyChar) || key.KeyChar == ' ') return false;
        TypeAhead(key.KeyChar);
        return true;
    }

    /// <summary>
    /// Handle the mouse over the tree drawn last: the wheel moves the selection by one row, and a left press on a
    /// row selects it and, on its expander, also toggles it.
    /// </summary>
    /// <returns>True if the event did either.</returns>
    public bool HandleMouse(MouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!_mouseArea.Contains(mouse.X, mouse.Y)) return false;
        Refresh();
        switch (mouse.Action)
        {
            case MouseAction.ScrollUp: Move(_index - 1); return true;
            case MouseAction.ScrollDown: Move(_index + 1); return true;
        }

        if (!mouse.IsClick || !_mouseRows.Contains(mouse.X, mouse.Y)) return false;
        var visible = mouse.Y - _mouseRows.Y;
        var index = Offset + visible;
        if (index >= _rows.Count) return false;
        Move(index);
        if (visible < _expanderColumns.Length && _expanderColumns[visible] == mouse.X && _rows[index].HasChildren)
            Toggle(_rows[index].Node);
        return true;
    }

    /// <summary>Called by the widget while drawing: compute the rows, then scroll so the selection is visible.</summary>
    internal void Fit(int viewportHeight)
    {
        Refresh();
        ViewportHeight = Math.Max(0, viewportHeight);
        Offset = SelectionState.FitOffset(Offset, Math.Max(0, _index), _rows.Count, ViewportHeight);
    }

    /// <summary>
    /// Called by the widget while drawing: its area, the rows its nodes are drawn in, and the column of each visible
    /// row's expander (-1 where it isn't drawn).
    /// </summary>
    internal void Track(Rect area, Rect rows, int[] expanderColumns)
    {
        _mouseArea = area;
        _mouseRows = rows;
        _expanderColumns = expanderColumns;
    }

    internal string TextOf(T node) => _text(node) ?? "";

    /// <summary>Overrides the app's clock for type-ahead in tests.</summary>
    internal TimeProvider? Time { get; set; }

    private int Page => Math.Max(1, ViewportHeight);

    private bool NodeHasChildren(T node) => HasChildren?.Invoke(node) ?? _children(node).Count > 0;

    /// <summary>Walk the expanded part of the tree, then find the selection in it (1.x spec §11.4).</summary>
    private void Refresh()
    {
        var rows = new List<TreeRow<T>>();
        var path = new List<T>();
        foreach (var root in _roots) Walk(root, null, path, rows);
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

        path.Add(node);
        foreach (var child in _children(node)) Walk(child, node, path, rows);
        path.RemoveAt(path.Count - 1);
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
        if (row.HasChildren && !row.IsExpanded) Expand(row.Node);
        else if (row.IsExpanded && _index + 1 < _rows.Count && _rows[_index + 1].Depth > row.Depth) Move(_index + 1);
    }

    private void Left()
    {
        if (_index < 0) return;
        var row = _rows[_index];
        if (row.IsExpanded) Collapse(row.Node);
        else if (row.Parent is { } parent) Move(IndexOf(parent));
    }

    private void Enter()
    {
        if (_index < 0) return;
        var row = _rows[_index];
        if (row.HasChildren) Toggle(row.Node);
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
