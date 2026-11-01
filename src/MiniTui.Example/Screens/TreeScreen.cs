using System.Globalization;
using MiniTui;

namespace MiniTui.Example;

/// <summary>The file system from the current directory, read a directory at a time as it's expanded.</summary>
internal sealed class TreeScreen : Screen
{
    private readonly TreeState<Entry> _tree;
    private bool _guides = true;

    public override string Title => "Tree";

    public TreeScreen()
    {
        var root = new Entry(new DirectoryInfo(Environment.CurrentDirectory));
        _tree = new TreeState<Entry>(e => e.Children ?? [], e => e.Name)
        {
            // Every directory shows an expander; its entries are read when it's first expanded
            HasChildren = e => e.Info is DirectoryInfo,
            Roots = [root],
        };
        _tree.Expanded += (_, e) => e.Node.Load();
        _tree.Activated += (_, e) => App.Toasts.Push(e.Node.Info.FullName);
        _tree.Expand(root);

        // Letters are the tree's type-ahead, so the screen's own keys use Alt
        Keys.Add("alt+g", "Guides", () => _guides = !_guides)
            .Add("alt+e", "Expand all below", () => Walk(_tree.Selected, _tree.Expand))
            .Add("alt+c", "Collapse all below", () => Walk(_tree.Selected, _tree.Collapse));
    }

    public override bool HandleKey(ConsoleKeyInfo key) => _tree.HandleKey(key);
    public override bool HandleMouse(MouseEvent mouse) => _tree.HandleMouse(mouse);

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var columns = Layout.Horizontal(area).Add("tree", Constraint.Percentage(60)).Add("detail", Constraint.Fill()).Build();
        Widget.Tree(_tree)
            .Title(" Files ")
            .Guides(_guides)
            .Format(e => e.Info is DirectoryInfo ? new Line(e.Name, ctx.Theme.Info) : e.Name)
            .Focused(ctx.Focused)
            .Draw(buf, columns["tree"]);

        var lines = new List<Line> { new("←/→ collapse and expand, Enter toggles or opens, type a name to jump", ctx.Theme.TextMuted), "" };
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
