using System.Globalization;
using MiniTui;

namespace MiniTui.Example;

/// <summary>
/// The file system from the current directory, read a directory at a time as it's expanded, with a filter field
/// above it and Alt+R to reveal a file deep in the repository (1.x spec §18.7).
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
        _tree = new TreeState<Entry>(e => e.Children ?? [], e => e.Name)
        {
            // Every directory shows an expander; its entries are read when it's first expanded
            HasChildren = e => e.Info is DirectoryInfo,
            Roots = [_root],
        };
        _tree.Expanded += (_, e) => e.Node.Load();
        _tree.Activated += (_, e) => App.Toasts.Push(e.Node.Info.FullName);
        _tree.Expand(_root);

        _filter = AddChild(new TextInput("Filter", "Type to filter the loaded files"));
        _view = AddChild(new TreeView(this));
        Focus.Add(_filter).Add(_view);
        Focus.Focus(_view);

        // Letters are the tree's type-ahead, so the screen's own keys use Alt
        Keys.Add("alt+g", "Guides", () => _guides = !_guides)
            .Add("alt+e", "Expand all below", () => Walk(_tree.Selected, _tree.Expand))
            .Add("alt+c", "Collapse all below", () => Walk(_tree.Selected, _tree.Collapse))
            .Add("alt+r", "Reveal App.cs", Reveal);
    }

    // Load each directory on the way to src/MiniTui/App/App.cs, then reveal it
    private void Reveal()
    {
        var path = new List<Entry> { _root };
        foreach (var name in new[] { "src", "MiniTui", "App", "App.cs" })
        {
            path[^1].Load();
            if (path[^1].Children?.Find(e => e.Name == name) is not { } next)
            {
                App.Toasts.Push("Run the example from the repository to reveal src/MiniTui/App/App.cs", ToastLevel.Warning);
                return;
            }
            path.Add(next);
        }
        if (!_tree.Reveal(path)) App.Toasts.Push("The filter hides App.cs", ToastLevel.Warning);
        Focus.Focus(_view);
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
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
            new("←/→ collapse and expand, Enter toggles or opens, type a name to jump. Tab moves to the filter; Alt+R reveals App.cs", ctx.Theme.TextMuted),
            "",
        };
        if (_tree.Selected is { } selected)
        {
            lines.Add(new Line(selected.Info.FullName, ctx.Theme.Text.Bold()));
            if (selected.Info is FileInfo file) lines.Add(string.Create(CultureInfo.InvariantCulture, $"{file.Length:N0} bytes"));
            else if (selected.Children is { } children) lines.Add($"{children.Count} entries");
            if (selected.Info.Exists) lines.Add($"Modified {selected.Info.LastWriteTime:yyyy-MM-dd HH:mm}");
        }
        Widget.Paragraph().Text(new Text(lines)).Wrap(WrapMode.Word)
            .Block(Widget.Block().Title(" Details ").Padding(new Margin(1, 0)))
            .Draw(buf, columns["detail"]);
    }

    private sealed class TreeView(TreeScreen screen) : Component
    {
        public override string KeysGroup => "Tree";
        public override bool HandleKey(ConsoleKeyInfo key) => screen._tree.HandleKey(key);
        public override bool HandleMouse(MouseEvent mouse) => screen._tree.HandleMouse(mouse);

        public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
            Widget.Tree(screen._tree)
                .Title(" Files ")
                .Guides(screen._guides)
                .Format(e => e.Info is DirectoryInfo ? new Line(e.Name, ctx.Theme.Info) : e.Name)
                .Focused(ctx.Focused).Hover(ctx.Hover)
                .Draw(buf, area);
    }

    // Expanding reads each directory below, so this can take a while on a large tree
    private static void Walk(Entry? entry, Action<Entry> action)
    {
        if (entry?.Info is not DirectoryInfo) return;
        action(entry);
        foreach (var child in entry.Children ?? []) Walk(child, action);
    }

    private sealed class Entry(FileSystemInfo info)
    {
        public FileSystemInfo Info { get; } = info;
        public string Name => Info.Name;
        public List<Entry>? Children { get; private set; }

        public void Load()
        {
            if (Children is not null) return;
            try
            {
                var dir = (DirectoryInfo)Info;
                Children =
                [
                    .. dir.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(d => new Entry(d)),
                    .. dir.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => new Entry(f)),
                ];
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                Children = [new Entry(new FileInfo(Path.Combine(Info.FullName, "(access denied)")))];
            }
        }
    }
}
