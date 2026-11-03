
namespace MiniTui.Tests;

/// <summary>Revealing a node and filtering a tree (1.x spec §18.3, §18.4).</summary>
public class TreeFilterTests
{
    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;
        public List<Node> Children { get; } = [.. children];
        public override string ToString() => Name;
    }

    // src/{app/{App.cs, AppLoop.cs}, widgets/{List.cs}}, docs/{spec.md}, README
    private static (TreeState<Node> State, Dictionary<string, Node> Nodes) Tree()
    {
        var app = new Node("App.cs");
        var loop = new Node("AppLoop.cs");
        var list = new Node("List.cs");
        var spec = new Node("spec.md");
        var appDir = new Node("app", app, loop);
        var widgets = new Node("widgets", list);
        var src = new Node("src", appDir, widgets);
        var docs = new Node("docs", spec);
        var readme = new Node("README");
        var state = new TreeState<Node>(n => n.Children, n => n.Name) { Roots = [src, docs, readme] };
        var nodes = new[] { app, loop, list, spec, appDir, widgets, src, docs, readme }.ToDictionary(n => n.Name);
        return (state, nodes);
    }

    private static string[] Names(TreeState<Node> state) => [.. state.Rows.Select(r => new string(' ', r.Depth * 2) + r.Node.Name)];

    private static void Press(TreeState<Node> state, ConsoleKey key, char c = '\0') =>
        state.HandleKey(new ConsoleKeyInfo(c, key, false, false, false));

    // Reveal

    [Fact]
    public void Reveal_ExpandsTheAncestors_AndSelectsTheNode()
    {
        var (state, n) = Tree();
        var expanded = new List<string>();
        var changes = 0;
        state.Expanded += (_, e) => expanded.Add(e.Node.Name);
        state.SelectionChanged += (_, _) => changes++;

        Assert.True(state.Reveal([n["src"], n["app"], n["AppLoop.cs"]]));

        Assert.Equal(["src", "app"], expanded);
        Assert.Equal("AppLoop.cs", state.Selected?.Name);
        Assert.Equal(1, changes);
        Assert.Equal(["src", "  app", "    App.cs", "    AppLoop.cs", "  widgets", "docs", "README"], Names(state));
    }

    [Fact]
    public void Reveal_ScrollsTheNodeIntoView()
    {
        var (state, n) = Tree();
        Widget.Tree(state).Draw(new CellBuffer(20, 4), new Rect(0, 0, 20, 4));

        state.Reveal([n["docs"], n["spec.md"]]);
        Widget.Tree(state).Draw(new CellBuffer(20, 4), new Rect(0, 0, 20, 4));

        // Rows src, docs, spec.md, README; two fit inside the border, so spec.md (row 2) shows from row 1
        Assert.Equal(1, state.Offset);
        Assert.Equal("spec.md", state.Selected?.Name);
    }

    [Fact]
    public void Reveal_APathThatDoesntMatch_ChangesNothing()
    {
        var (state, n) = Tree();
        var expanded = 0;
        state.Expanded += (_, _) => expanded++;

        Assert.False(state.Reveal([]));
        Assert.False(state.Reveal([n["app"]]));                        // not a root
        Assert.False(state.Reveal([n["src"], n["List.cs"]]));          // not a child of src
        Assert.False(state.Reveal([n["docs"], n["spec.md"], n["App.cs"]]));

        Assert.Equal(0, expanded);
        Assert.Equal(["src", "docs", "README"], Names(state));
        Assert.Equal("src", state.Selected?.Name);
    }

    [Fact]
    public void Reveal_AnAlreadyExpandedPath_RaisesNoExpanded()
    {
        var (state, n) = Tree();
        state.Expand(n["src"]);
        var expanded = new List<string>();
        state.Expanded += (_, e) => expanded.Add(e.Node.Name);

        Assert.True(state.Reveal([n["src"], n["widgets"]]));

        Assert.Empty(expanded);
        Assert.Equal("widgets", state.Selected?.Name);
    }

    // Filter

    [Fact]
    public void AFilter_ShowsMatchesAndTheirAncestors_Open()
    {
        var (state, _) = Tree();

        state.Filter = node => node.Name.Contains("App", StringComparison.Ordinal);

        Assert.Equal(["src", "  app", "    App.cs", "    AppLoop.cs"], Names(state));
        Assert.All(state.Rows.Take(2), r => Assert.True(r.IsExpanded));
        Assert.False(state.IsExpanded(state.Rows[0].Node));
    }

    [Fact]
    public void AMatchingParent_IsShown_WithoutItsOtherChildren()
    {
        var (state, _) = Tree();

        state.Filter = node => node.Name == "docs";

        Assert.Equal(["docs"], Names(state));
        Assert.False(state.Rows[0].HasChildren);
    }

    [Fact]
    public void WhileFiltering_LeftClosesARow_AndANewFilterOpensItAgain()
    {
        var (state, n) = Tree();
        state.Filter = node => node.Name.EndsWith(".cs", StringComparison.Ordinal);
        state.Selected = n["src"];

        Press(state, ConsoleKey.LeftArrow);
        Assert.Equal(["src"], Names(state));
        Assert.False(state.IsExpanded(n["src"]));

        Press(state, ConsoleKey.RightArrow);
        Assert.Equal(6, state.Rows.Count);

        Press(state, ConsoleKey.LeftArrow);
        state.Filter = node => node.Name.EndsWith(".cs", StringComparison.Ordinal);
        Assert.Equal(6, state.Rows.Count);
    }

    [Fact]
    public void ClearingTheFilter_KeepsTheSelection_OrSelectsTheNearestAncestor()
    {
        var (state, n) = Tree();
        state.Expand(n["src"]);
        state.Filter = node => node.Name == "AppLoop.cs";
        state.Selected = n["AppLoop.cs"];
        state.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        state.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        Assert.Equal("AppLoop.cs", state.Selected?.Name);

        // app isn't expanded in the tree itself, so AppLoop.cs goes away and app is selected
        state.Filter = null;

        Assert.Equal(["src", "  app", "  widgets", "docs", "README"], Names(state));
        Assert.Equal("app", state.Selected?.Name);
    }

    [Fact]
    public void TypeAhead_SearchesTheFilteredRows()
    {
        var (state, _) = Tree();
        state.Filter = node => node.Name.Contains("List", StringComparison.Ordinal) || node.Name == "README";

        Press(state, ConsoleKey.L, 'l');

        Assert.Equal("List.cs", state.Selected?.Name);
    }

    [Fact]
    public void TheTitleCount_IsTheFilteredRows()
    {
        var (state, _) = Tree();
        state.Filter = node => node.Name == "spec.md";
        var buf = new CellBuffer(30, 6);

        Widget.Tree(state).Title("Files").Draw(buf, buf.Area);

        Assert.Contains("[2]", buf.ToLines()[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ACyclicTree_FiltersWithoutHanging()
    {
        var a = new Node("a");
        var b = new Node("b", a);
        a.Children.Add(b);
        var state = new TreeState<Node>(n => n.Children, n => n.Name) { Roots = [a] };

        state.Filter = node => node.Name == "b";

        Assert.Equal(["a", "  b"], Names(state));
    }

    [Fact]
    public void Reveal_UnderAFilter_OnlyWhatItShows()
    {
        var (state, n) = Tree();
        state.Filter = node => node.Name.EndsWith(".cs", StringComparison.Ordinal);

        Assert.False(state.Reveal([n["docs"], n["spec.md"]]));
        Assert.True(state.Reveal([n["src"], n["widgets"], n["List.cs"]]));
        Assert.Equal("List.cs", state.Selected?.Name);
    }

    [Fact]
    public void Reveal_UnderAFilter_ReopensARowClosedWhileFiltering()
    {
        var (state, n) = Tree();
        state.Filter = node => node.Name.EndsWith(".cs", StringComparison.Ordinal);
        state.Selected = n["src"];
        Press(state, ConsoleKey.LeftArrow);

        Assert.True(state.Reveal([n["src"], n["app"], n["App.cs"]]));

        Assert.Equal("App.cs", state.Selected?.Name);
    }
}
