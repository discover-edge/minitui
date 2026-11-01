using Buffer = MiniTui.Buffer;
using Microsoft.Extensions.Time.Testing;

namespace MiniTui.Tests;

/// <summary>TreeState and the tree widget (1.x spec §11).</summary>
public class TreeTests
{
    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;
        public List<Node> Children { get; } = [.. children];
        public override string ToString() => Name;
    }

    private sealed class ByName : IEqualityComparer<Node>
    {
        public bool Equals(Node? x, Node? y) => x?.Name == y?.Name;
        public int GetHashCode(Node obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }

    // src
    //   MiniTui
    //     App.cs
    //     Widgets
    //       List.cs
    //   Example
    //     Program.cs
    // tests
    //   Tests.cs
    // README
    private static Node[] Sample() =>
    [
        new("src",
            new Node("MiniTui", new Node("App.cs"), new Node("Widgets", new Node("List.cs"))),
            new Node("Example", new Node("Program.cs"))),
        new("tests", new Node("Tests.cs")),
        new("README"),
    ];

    private static TreeState<Node> State(Node[]? roots = null, IEqualityComparer<Node>? comparer = null) =>
        new(n => n.Children, n => n.Name, comparer) { Roots = roots ?? Sample() };

    private static Node Find(TreeState<Node> state, string name) => Find(state.Roots, name)!;

    private static Node? Find(IEnumerable<Node> nodes, string name)
    {
        foreach (var node in nodes)
        {
            if (node.Name == name) return node;
            if (Find(node.Children, name) is { } found) return found;
        }
        return null;
    }

    private static string Names(TreeState<Node> state) =>
        string.Join(" ", state.Rows.Select(r => new string('.', r.Depth) + r.Node.Name));

    private static ConsoleKeyInfo K(ConsoleKey key, char c = '\0', ConsoleModifiers mods = 0) =>
        new(c, key, (mods & ConsoleModifiers.Shift) != 0, (mods & ConsoleModifiers.Alt) != 0, (mods & ConsoleModifiers.Control) != 0);

    private static ConsoleKeyInfo Char(char c) => K(ConsoleKey.A + (char.ToUpperInvariant(c) - 'A'), c);

    private static Buffer Draw(TreeState<Node> state, int width, int height, Func<TreeBuilder<Node>, TreeBuilder<Node>>? configure = null)
    {
        var buf = new Buffer(width, height);
        var builder = Widget.Tree(state);
        (configure?.Invoke(builder) ?? builder).Draw(buf, buf.Area);
        return buf;
    }

    // ---- Rows ----

    [Fact]
    public void Rows_ShowRootsOnly_UntilExpanded()
    {
        var state = State();
        Assert.Equal("src tests README", Names(state));
        Assert.All(state.Rows, r => Assert.Null(r.Parent));
        Assert.Equal([true, true, false], state.Rows.Select(r => r.HasChildren));
    }

    [Fact]
    public void Rows_WalkExpandedNodesDepthFirst()
    {
        var state = State();
        state.Expand(Find(state, "src"));
        state.Expand(Find(state, "MiniTui"));
        Assert.Equal("src .MiniTui ..App.cs ..Widgets .Example tests README", Names(state));
        var app = state.Rows[2];
        Assert.Equal((2, "MiniTui", false), (app.Depth, app.Parent!.Name, app.IsExpanded));
        Assert.True(state.Rows[1].IsExpanded);
    }

    [Fact]
    public void Collapse_KeepsDescendantsExpanded()
    {
        var state = State();
        state.Expand(Find(state, "src"));
        state.Expand(Find(state, "MiniTui"));
        state.Collapse(Find(state, "src"));
        Assert.Equal("src tests README", Names(state));
        Assert.True(state.IsExpanded(Find(state, "MiniTui")));
        state.Expand(Find(state, "src"));
        Assert.Equal("src .MiniTui ..App.cs ..Widgets .Example tests README", Names(state));
    }

    [Fact]
    public void Children_AreReadOnlyForExpandedNodes_WhenHasChildrenIsSet()
    {
        var reads = new List<string>();
        var roots = Sample();
        var state = new TreeState<Node>(n => { reads.Add(n.Name); return n.Children; }, n => n.Name)
        {
            HasChildren = n => n.Children.Count > 0,
            Roots = roots,
        };
        Assert.Empty(reads);
        state.Expand(roots[0]);
        Assert.Equal(["src"], reads.Distinct());
    }

    [Fact]
    public void HasChildren_ShowsAnExpanderBeforeChildrenAreLoaded()
    {
        var folder = new Node("folder");
        var state = new TreeState<Node>(n => n.Children, n => n.Name) { HasChildren = _ => true, Roots = [folder] };
        state.Expand(folder);
        Assert.Equal((true, true), (state.Rows[0].HasChildren, state.Rows[0].IsExpanded));
        Assert.Single(state.Rows);

        folder.Children.Add(new Node("loaded"));
        Draw(state, 20, 5);
        Assert.Equal("folder .loaded", Names(state));
    }

    [Fact]
    public void Cycles_AreShownAsLeaves_AndExpandAllTerminates()
    {
        var a = new Node("a");
        var b = new Node("b", a);
        a.Children.Add(b);
        var state = State([a]);
        state.ExpandAll();
        Assert.Equal("a .b ..a", Names(state));
        Assert.False(state.Rows[2].HasChildren);
    }

    [Fact]
    public void ExpandAll_AndCollapseAll_RaiseEventsForEachChange()
    {
        var state = State();
        var expanded = new List<string>();
        var collapsed = new List<string>();
        state.Expanded += (_, e) => expanded.Add(e.Node.Name);
        state.Collapsed += (_, e) => collapsed.Add(e.Node.Name);
        state.ExpandAll();
        Assert.Equal(["src", "MiniTui", "Widgets", "Example", "tests"], expanded);
        Assert.Equal(10, state.Rows.Count);
        state.CollapseAll();
        Assert.Equal(5, collapsed.Count);
        Assert.Equal("src tests README", Names(state));
    }

    [Fact]
    public void Expand_DoesNothingForALeafOrTwice()
    {
        var state = State();
        var events = 0;
        state.Expanded += (_, _) => events++;
        state.Expand(Find(state, "README"));
        state.Expand(Find(state, "src"));
        state.Expand(Find(state, "src"));
        state.Collapse(Find(state, "README"));
        Assert.Equal(1, events);
        Assert.False(state.IsExpanded(Find(state, "README")));
    }

    [Fact]
    public void Toggle_ExpandsAndCollapses()
    {
        var state = State();
        var src = Find(state, "src");
        state.Toggle(src);
        Assert.True(state.IsExpanded(src));
        state.Toggle(src);
        Assert.False(state.IsExpanded(src));
    }

    [Fact]
    public void Comparer_KeepsExpandedNodesAndSelectionAcrossARebuiltModel()
    {
        var state = State(comparer: new ByName());
        state.Expand(Find(state, "src"));
        state.Selected = Find(state, "Example");
        Draw(state, 20, 10);

        state.Roots = Sample();
        Assert.Equal("src .MiniTui .Example tests README", Names(state));
        Assert.Equal("Example", state.Selected!.Name);
        Assert.Same(state.Rows[2].Node, state.Selected);
    }

    [Fact]
    public void Roots_SetWithoutComparer_KeepsReferencesOnly()
    {
        var roots = Sample();
        var state = State(roots);
        state.Expand(roots[0]);
        state.Roots = [.. roots.Reverse()];
        Assert.Equal("README tests src .MiniTui .Example", Names(state));
    }

    // ---- Selection ----

    [Fact]
    public void Selected_DefaultsToTheFirstRow_AndIsNullForAnEmptyTree()
    {
        Assert.Equal("src", State().Selected!.Name);
        var empty = State([]);
        Assert.Null(empty.Selected);
        Assert.True(empty.HandleKey(K(ConsoleKey.DownArrow)));
        Assert.True(empty.HandleKey(K(ConsoleKey.Enter)));
        Assert.True(empty.HandleKey(Char('a')));
        Assert.Null(empty.Selected);
    }

    [Fact]
    public void Collapsing_AnAncestor_SelectsIt()
    {
        var state = State();
        state.ExpandAll();
        state.Selected = Find(state, "List.cs");
        state.HandleKey(K(ConsoleKey.Home)); // a refresh: List.cs is found
        state.Selected = Find(state, "List.cs");
        Draw(state, 20, 12);
        state.Collapse(Find(state, "MiniTui"));
        Assert.Equal("MiniTui", state.Selected!.Name);
    }

    [Fact]
    public void Removing_TheSelectedNode_SelectsItsParent_ThenTheOldIndex()
    {
        var roots = Sample();
        var state = State(roots);
        state.Expand(roots[1]);
        state.Selected = roots[1].Children[0];
        Draw(state, 20, 10);

        roots[1].Children.Clear();
        Draw(state, 20, 10);
        Assert.Equal("tests", state.Selected!.Name);

        state.Roots = [roots[0], roots[2]];
        Assert.Equal("README", state.Selected!.Name); // index 1, clamped
    }

    [Fact]
    public void SelectionChanged_IsRaisedForUserMovesOnly()
    {
        var state = State();
        var changes = 0;
        state.SelectionChanged += (_, _) => changes++;
        state.Selected = Find(state, "tests");
        state.HandleKey(K(ConsoleKey.UpArrow));
        state.HandleKey(K(ConsoleKey.UpArrow)); // already first
        Assert.Equal(1, changes);
        Assert.Equal("src", state.Selected!.Name);
    }

    // ---- Keys ----

    [Fact]
    public void Arrows_PagesHomeAndEnd_MoveAndStopAtTheEnds()
    {
        var state = State();
        state.ExpandAll();
        Draw(state, 20, 5); // 3 visible rows
        state.HandleKey(K(ConsoleKey.DownArrow));
        Assert.Equal("MiniTui", state.Selected!.Name);
        state.HandleKey(K(ConsoleKey.PageDown));
        Assert.Equal("List.cs", state.Selected!.Name);
        state.HandleKey(K(ConsoleKey.PageUp));
        Assert.Equal("MiniTui", state.Selected!.Name);
        state.HandleKey(K(ConsoleKey.End));
        Assert.Equal("README", state.Selected!.Name);
        state.HandleKey(K(ConsoleKey.DownArrow));
        Assert.Equal("README", state.Selected!.Name);
        state.HandleKey(K(ConsoleKey.Home));
        Assert.Equal("src", state.Selected!.Name);
    }

    [Fact]
    public void Right_ExpandsThenMovesToTheFirstChild_AndDoesNothingOnALeaf()
    {
        var state = State();
        Assert.True(state.HandleKey(K(ConsoleKey.RightArrow)));
        Assert.True(state.IsExpanded(Find(state, "src")));
        Assert.Equal("src", state.Selected!.Name);
        state.HandleKey(K(ConsoleKey.RightArrow));
        Assert.Equal("MiniTui", state.Selected!.Name);

        state.Selected = Find(state, "README");
        Assert.True(state.HandleKey(K(ConsoleKey.RightArrow)));
        Assert.Equal("README", state.Selected!.Name);
    }

    [Fact]
    public void Left_CollapsesThenMovesToTheParent_AndDoesNothingOnACollapsedRoot()
    {
        var state = State();
        state.ExpandAll();
        state.Selected = Find(state, "Widgets");
        state.HandleKey(K(ConsoleKey.LeftArrow));
        Assert.False(state.IsExpanded(Find(state, "Widgets")));
        state.HandleKey(K(ConsoleKey.LeftArrow));
        Assert.Equal("MiniTui", state.Selected!.Name);

        state.Selected = Find(state, "README");
        Assert.True(state.HandleKey(K(ConsoleKey.LeftArrow)));
        Assert.Equal("README", state.Selected!.Name);
    }

    [Fact]
    public void Enter_TogglesAParent_AndActivatesALeaf()
    {
        var state = State();
        var activated = new List<string>();
        state.Activated += (_, e) => activated.Add(e.Node.Name);
        state.HandleKey(K(ConsoleKey.Enter));
        Assert.True(state.IsExpanded(Find(state, "src")));
        state.HandleKey(K(ConsoleKey.Enter));
        Assert.False(state.IsExpanded(Find(state, "src")));
        Assert.Empty(activated);

        state.Selected = Find(state, "README");
        state.HandleKey(K(ConsoleKey.Enter));
        Assert.Equal(["README"], activated);
    }

    [Fact]
    public void TypeAhead_FindsByPrefix_RepeatsAndWraps_WithoutExpanding()
    {
        var time = new FakeTimeProvider();
        var state = State();
        state.Time = time;
        state.Expand(Find(state, "src"));
        // src MiniTui Example tests README
        state.HandleKey(Char('t'));
        Assert.Equal("tests", state.Selected!.Name);
        time.Advance(TimeSpan.FromSeconds(2));
        state.HandleKey(Char('e'));
        Assert.Equal("Example", state.Selected!.Name); // wraps around
        time.Advance(TimeSpan.FromSeconds(2));
        state.HandleKey(Char('r'));
        state.HandleKey(Char('e'));
        Assert.Equal("README", state.Selected!.Name);
        time.Advance(TimeSpan.FromSeconds(2));
        state.HandleKey(Char('z'));
        Assert.Equal("README", state.Selected!.Name);
        Assert.False(state.IsExpanded(Find(state, "tests")));
    }

    [Fact]
    public void TypeAhead_SameCharacterAgain_MovesToTheNextMatch()
    {
        var state = State([new("aa"), new("ab"), new("b")]);
        state.Time = new FakeTimeProvider();
        state.HandleKey(Char('a'));
        Assert.Equal("aa", state.Selected!.Name);
        state.HandleKey(Char('a'));
        Assert.Equal("ab", state.Selected!.Name);
        state.HandleKey(Char('a'));
        Assert.Equal("aa", state.Selected!.Name);
    }

    [Fact]
    public void KeysThatArentTreeKeys_AreNotHandled()
    {
        var state = State();
        Assert.False(state.HandleKey(K(ConsoleKey.Spacebar, ' ')));
        Assert.False(state.HandleKey(K(ConsoleKey.Tab, '\t')));
        Assert.False(state.HandleKey(K(ConsoleKey.Escape, '\x1b')));
        Assert.False(state.HandleKey(K(ConsoleKey.S, '\x13', ConsoleModifiers.Control)));
        Assert.False(state.HandleKey(K(ConsoleKey.X, 'x', ConsoleModifiers.Alt)));
    }

    // ---- Mouse ----

    [Fact]
    public void Mouse_WheelMoves_PressSelects_ExpanderToggles()
    {
        var state = State();
        Draw(state, 20, 6);
        var changes = 0;
        state.SelectionChanged += (_, _) => changes++;

        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.ScrollDown, MouseButton.None, 5, 2)));
        Assert.Equal("tests", state.Selected!.Name);
        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 8, 3)));
        Assert.Equal("README", state.Selected!.Name);
        Assert.Equal(2, changes);

        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 1, 1)));
        Assert.True(state.IsExpanded(Find(state, "src")));
        Assert.Equal("src", state.Selected!.Name);

        // A press on the label doesn't toggle
        Draw(state, 20, 6);
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 4, 1));
        Assert.True(state.IsExpanded(Find(state, "src")));
    }

    [Fact]
    public void Mouse_OutsideTheRows_IsNotHandled()
    {
        var state = State();
        Draw(state, 20, 8);
        Assert.False(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 3, 5))); // below the last row
        Assert.False(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 0, 1))); // the border
        Assert.False(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 30, 1))); // outside
        Assert.False(state.HandleMouse(new MouseEvent(MouseAction.Drag, MouseButton.Left, 3, 1)));
        Assert.Equal("src", state.Selected!.Name);
    }

    [Fact]
    public void Mouse_PressAfterScrolling_SelectsTheRowUnderThePointer()
    {
        var state = State();
        state.ExpandAll();
        state.Selected = Find(state, "README");
        Draw(state, 20, 5); // rows 7-9 visible
        Assert.Equal(7, state.Offset);
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 5, 1));
        Assert.Equal("tests", state.Selected!.Name);
    }

    // ---- Drawing ----

    [Fact]
    public void Draw_IndentsByDepth_WithExpanders()
    {
        var state = State();
        state.Expand(Find(state, "src"));
        state.Expand(Find(state, "MiniTui"));
        var buf = Draw(state, 22, 9, b => b.Title("Files"));
        Snapshot.AssertLines(buf, """
            ┌Files [7]───────────┐
            │▾ src               │
            │  ▾ MiniTui         │
            │      App.cs        │
            │    ▸ Widgets       │
            │  ▸ Example         │
            │▸ tests             │
            │  README            │
            └────────────────────┘
            """);
    }

    [Fact]
    public void Draw_IndentSetsColumnsPerLevel()
    {
        var state = State();
        state.Expand(Find(state, "src"));
        var buf = Draw(state, 16, 5, b => b.Indent(4).Block(Widget.Block().Borders(Borders.None)));
        Snapshot.AssertTrimmed(buf, """
            ▾ src
                ▸ MiniTui
                ▸ Example
            ▸ tests
              README
            """);
    }

    [Fact]
    public void Draw_Guides()
    {
        var state = State();
        state.ExpandAll();
        var buf = Draw(state, 24, 10, b => b.Guides().Block(Widget.Block().Borders(Borders.None)));
        Snapshot.AssertTrimmed(buf, """
            ▾ src
            ├─▾ MiniTui
            │ ├─  App.cs
            │ └─▾ Widgets
            │   └─  List.cs
            └─▾ Example
              └─  Program.cs
            ▾ tests
            └─  Tests.cs
              README
            """);
    }

    [Fact]
    public void Draw_GuidesWhenScrolled_StillKnowLaterSiblings()
    {
        var state = State();
        state.ExpandAll();
        state.Selected = Find(state, "List.cs");
        state.Offset = 3;
        var buf = Draw(state, 24, 2, b => b.Guides().Block(Widget.Block().Borders(Borders.None)));
        Assert.Equal(["│ └─▾ Widgets", "│   └─  List.cs"], buf.ToLines().Select(l => l[..^1].TrimEnd())); // less the scrollbar
    }

    [Fact]
    public void Draw_BasicGlyphs_UsePlusAndMinus()
    {
        var state = State();
        state.Expand(Find(state, "src"));
        var buf = new Buffer(14, 5);
        using (Glyphs.Use(basic: true))
            Widget.Tree(state).Block(Widget.Block().Borders(Borders.None)).Draw(buf, buf.Area);
        Snapshot.AssertTrimmed(buf, """
            - src
              + MiniTui
              + Example
            + tests
              README
            """);
    }

    [Fact]
    public void Draw_HighlightsTheSelectedRow_WithAHighlightSymbol()
    {
        var state = State();
        state.Selected = Find(state, "tests");
        var buf = Draw(state, 14, 3, b => b.HighlightSymbol("> ").Focused().Block(Widget.Block().Borders(Borders.None)));
        Snapshot.AssertTrimmed(buf, """
              ▸ src
            > ▸ tests
                README
            """);
        var theme = Theme.Default;
        Assert.Equal(theme.Selection.Bg, buf.Get(13, 1).Bg);
        Assert.NotEqual(theme.Selection.Bg, buf.Get(13, 0).Bg);
    }

    [Fact]
    public void Draw_Unfocused_UsesTheUnfocusedSelection()
    {
        var state = State();
        var buf = Draw(state, 10, 3, b => b.Block(Widget.Block().Borders(Borders.None)));
        Assert.Equal(Theme.Default.SelectionUnfocused.Bg, buf.Get(5, 0).Bg);
    }

    [Fact]
    public void Draw_FormatAndTruncation()
    {
        var state = State();
        state.Expand(Find(state, "src"));
        var buf = Draw(state, 9, 5, b => b.Format(n => n.Name.ToUpperInvariant()).Block(Widget.Block().Borders(Borders.None)));
        Snapshot.AssertTrimmed(buf, """
            ▾ SRC
              ▸ MINI…
              ▸ EXAM…
            ▸ TESTS
              README
            """);
    }

    [Fact]
    public void Draw_Overflow_ShowsScrollInfoAndAScrollbar()
    {
        var state = State();
        state.ExpandAll();
        var buf = Draw(state, 20, 5, b => b.Title("T"));
        Assert.StartsWith("┌T [1-3 of 10]", buf.ToLines()[0]);
        Assert.NotEqual('│', buf.Get(19, 1).Symbol[0]);
    }

    [Fact]
    public void Draw_TinyArea_DoesNotThrow()
    {
        var state = State();
        state.ExpandAll();
        foreach (var (w, h) in new[] { (0, 0), (1, 1), (2, 3), (3, 2), (4, 4) })
        {
            var buf = new Buffer(Math.Max(1, w), Math.Max(1, h));
            Widget.Tree(state).Guides().Title("Tree").HighlightSymbol(">>").Draw(buf, new Rect(0, 0, w, h));
        }
    }

    [Fact]
    public void Draw_Monochrome_MarksTheSelection()
    {
        var state = State();
        var buf = new Buffer(10, 2);
        using (Theme.Use(Theme.Monochrome))
            Widget.Tree(state).Focused().Block(Widget.Block().Borders(Borders.None)).Draw(buf, buf.Area);
        Assert.True(buf.Get(4, 0).Modifiers.HasFlag(Modifiers.Reversed));
        Assert.False(buf.Get(4, 1).Modifiers.HasFlag(Modifiers.Reversed));
    }
}
