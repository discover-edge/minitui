using System.Globalization;
using MiniTui;

namespace MiniTui.Example;

/// <summary>
/// The file system from the current directory, loaded a directory at a time in the background as it's expanded, with
/// a filter field above it, check boxes, and Ctrl+G to reveal a file deep in the repository (1.x spec §18.7).
/// </summary>
internal sealed class TreeScreen : Screen
{
    private readonly TreeState<Entry> _tree;
    private readonly Entry _root;
    private readonly TextInput _filter;
    private readonly TreeView _view;
    private string _filterText = "";
    private bool _guides = true;

    public override string Title => "Tree";

    public TreeScreen()
    {
        _root = new Entry(new DirectoryInfo(Environment.CurrentDirectory));
        // Entries match by path, so Ctrl+G can name the nodes on its path before they're loaded
        _tree = TreeState.Async<Entry>(LoadAsync, e => e.Name, EqualityComparer<Entry>.Create(
            (a, b) => a?.Info.FullName == b?.Info.FullName, e => e.Info.FullName.GetHashCode(StringComparison.Ordinal)));
        // Files are leaves, and only directories load
        _tree.HasChildren = e => e.Info is DirectoryInfo;
        _tree.Roots = [_root];
        _tree.Activated += (_, e) => App.Toasts.Push(e.Node.Info.FullName);
        _tree.Expand(_root);

        _filter = AddChild(new TextInput("Filter", "Type to filter the loaded files"));
        _view = AddChild(new TreeView(this));
        Focus.Add(_filter).Add(_view);
        Focus.Focus(_view);

        // Letters are the tree's type-ahead, so the screen's own keys use Alt
        Keys.Add("alt+g", "Guides", () => _guides = !_guides)
            .Add("alt+e", "Expand below", () => Below(_tree.Expand))
            .Add("alt+c", "Collapse below", () => Below(_tree.Collapse))
            .Add("alt+x", "Check boxes", () => _tree.Checkboxes = !_tree.Checkboxes)
            .Add("ctrl+g", "Go to App.cs", () => App.RunTask(RevealAsync));
    }

    // A directory's entries, read off the UI thread after a short delay so "Loading…" shows
    private static async Task<IReadOnlyList<Entry>> LoadAsync(Entry entry, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        return await Task.Run(() => entry.Read(), ct);
    }

    // 1.x spec §18.3: load each directory on the way to src/MiniTui/App/App.cs, then reveal it
    private async Task RevealAsync(CancellationToken ct)
    {
        var path = new List<Entry> { _root };
        foreach (var name in new[] { "src", "MiniTui", "App", "App.cs" })
        {
            var full = Path.Combine(path[^1].Info.FullName, name);
            path.Add(new Entry(name.EndsWith(".cs", StringComparison.Ordinal) ? new FileInfo(full) : new DirectoryInfo(full)));
        }
        if (await _tree.RevealAsync(path, ct)) Focus.Focus(_view);
        else App.Toasts.Push("App.cs isn't here (run the example from the repository) or the filter hides it", ToastLevel.Warning);
    }

    // The selected node and the loaded rows below it
    private void Below(Action<Entry> action)
    {
        if (_tree.Selected is not { } selected) return;
        var rows = _tree.Rows;
        var at = rows.ToList().FindIndex(r => r.Node == selected);
        if (at < 0) return;
        var below = new List<Entry> { selected };
        for (var i = at + 1; i < rows.Count && rows[i].Depth > rows[at].Depth; i++) below.Add(rows[i].Node);
        foreach (var entry in below) if (entry.Info is DirectoryInfo) action(entry);
    }

    public override void Render(CellBuffer buf, Rect area, RenderContext ctx)
    {
        // 1.x spec §18.4: the field's text drives the tree's filter
        if (_filter.State.Text != _filterText)
        {
            _filterText = _filter.State.Text;
            var text = _filterText;
            _tree.Filter = text.Length == 0 ? null : e => e.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        var columns = Layout.Horizontal(area).Add("tree", Constraint.Percentage(60)).Add("detail", Constraint.Fill()).Build();
        var left = Layout.Vertical(columns["tree"]).Add("filter", Constraint.Length(3)).Add("tree", Constraint.Fill()).Build();
        ctx.Render(_filter, buf, left["filter"]);
        ctx.Render(_view, buf, left["tree"]);

        var lines = new List<Line>
        {
            new("←/→ collapse and expand, Enter toggles or opens, type a name to jump. Tab moves to the filter; Ctrl+G goes to App.cs; Alt+X shows check boxes (Space checks)", ctx.Theme.TextMuted),
            "",
        };
        if (_tree.Checkboxes)
        {
            lines.Add($"{_tree.CheckedNodes.Count} checked (folders count once)");
            lines.Add("");
        }
        if (_tree.Selected is { } selected)
        {
            lines.Add(new Line(selected.Info.FullName, ctx.Theme.Text.Bold()));
            if (selected.Info is FileInfo file) lines.Add(string.Create(CultureInfo.InvariantCulture, $"{file.Length:N0} bytes"));
            if (selected.Info.Exists) lines.Add($"Modified {selected.Info.LastWriteTime:yyyy-MM-dd HH:mm}");
        }
        Widget.Paragraph().Text(new Text(lines)).Wrap(WrapMode.Word)
            .Block(Widget.Block().Title(" Details "))
            .Draw(buf, columns["detail"]);
    }

    private sealed class TreeView(TreeScreen screen) : Component
    {
        public override string KeysGroup => "Tree";
        public override bool HandleKey(ConsoleKeyInfo key) => screen._tree.HandleKey(key);
        public override bool HandleMouse(MouseEvent mouse) => screen._tree.HandleMouse(mouse);

        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) =>
            Widget.Tree(screen._tree)
                .Title(" Files ")
                .Guides(screen._guides)
                .Format(e => e.Info is DirectoryInfo ? new Line(e.Name, ctx.Theme.Info) : e.Name)
                .Focused(ctx.Focused).Hover(ctx.Hover)
                .Draw(buf, area);
    }

    private sealed class Entry(FileSystemInfo info)
    {
        public FileSystemInfo Info { get; } = info;
        public string Name => Info.Name;

        public IReadOnlyList<Entry> Read()
        {
            try
            {
                var dir = (DirectoryInfo)Info;
                return
                [
                    .. dir.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(d => new Entry(d)),
                    .. dir.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => new Entry(f)),
                ];
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                return [new Entry(new FileInfo(Path.Combine(Info.FullName, "(access denied)")))];
            }
        }
    }
}
