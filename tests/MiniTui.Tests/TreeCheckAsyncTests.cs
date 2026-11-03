
namespace MiniTui.Tests;

/// <summary>Check boxes in trees and async children (1.x spec §18.5, §18.6).</summary>
public class TreeCheckAsyncTests
{
    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;
        public List<Node> Children { get; } = [.. children];
        public override string ToString() => Name;
    }

    // root/{a/{a1, a2}, b}
    private static (TreeState<Node> State, Node Root, Node A, Node A1, Node A2, Node B) Tree()
    {
        var a1 = new Node("a1");
        var a2 = new Node("a2");
        var a = new Node("a", a1, a2);
        var b = new Node("b");
        var root = new Node("root", a, b);
        var state = new TreeState<Node>(n => n.Children, n => n.Name) { Roots = [root], Checkboxes = true };
        return (state, root, a, a1, a2, b);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static string[] Draw(TreeState<Node> state, int width = 24, int height = 8)
    {
        var buf = new CellBuffer(width, height);
        Widget.Tree(state).Draw(buf, buf.Area);
        return [.. buf.ToLines().Select(l => l.TrimEnd())];
    }

    // Checks

    [Fact]
    public void Parents_AreCheckedUncheckedOrPartly_FromTheirChildren()
    {
        var (state, root, a, a1, a2, b) = Tree();

        state.SetChecked(a1, true);
        Assert.Null(state.IsChecked(a));
        Assert.Null(state.IsChecked(root));

        state.SetChecked(a2, true);
        Assert.True(state.IsChecked(a));
        Assert.Null(state.IsChecked(root));

        state.SetChecked(b, true);
        Assert.True(state.IsChecked(root));

        state.SetChecked(root, false);
        Assert.All([root, a, a1, a2, b], n => Assert.False(state.IsChecked(n)));
    }

    [Fact]
    public void CheckingAParent_ChecksEverythingUnderIt()
    {
        var (state, root, a, a1, a2, b) = Tree();

        state.SetChecked(a, true);

        Assert.True(state.IsChecked(a1));
        Assert.True(state.IsChecked(a2));
        Assert.False(state.IsChecked(b));
    }

    [Fact]
    public void Space_TogglesTheSelectedRow_AndAPartlyCheckedParentChecks()
    {
        var (state, root, a, a1, _, _) = Tree();
        var changed = new List<string>();
        state.CheckedChanged += (_, e) => changed.Add(e.Node.Name);
        state.SetChecked(a1, true);
        changed.Clear();
        state.Selected = root;

        Assert.True(state.HandleKey(Key(ConsoleKey.Spacebar, ' ')));
        Assert.True(state.IsChecked(root));
        Assert.True(state.HandleKey(Key(ConsoleKey.Spacebar, ' ')));
        Assert.False(state.IsChecked(a));

        Assert.Equal(["root", "root"], changed);
    }

    [Fact]
    public void WithoutCheckboxes_SpaceIsntHandled()
    {
        var (state, _, _, _, _, _) = Tree();
        state.Checkboxes = false;

        Assert.False(state.HandleKey(Key(ConsoleKey.Spacebar, ' ')));
    }

    [Fact]
    public void CheckedNodes_AreFullParentsAndLeaves_InTreeOrder()
    {
        var (state, root, a, a1, a2, b) = Tree();

        state.SetChecked(a, true);
        Assert.Equal([a], state.CheckedNodes);

        state.SetChecked(b, true);
        Assert.Equal([root], state.CheckedNodes);

        state.SetChecked(a2, false);
        Assert.Equal([a1, b], state.CheckedNodes);
    }

    [Fact]
    public void TheBoxes_AreDrawnBeforeTheLabels()
    {
        var (state, root, a, a1, _, _) = Tree();
        state.Expand(root);
        state.Expand(a);
        state.SetChecked(a1, true);

        var lines = Draw(state);

        Assert.Contains("▾ [-] root", lines[1], StringComparison.Ordinal);
        Assert.Contains("[-] a", lines[2], StringComparison.Ordinal);
        Assert.Contains("[✓] a1", lines[3], StringComparison.Ordinal);
        Assert.Contains("[ ] a2", lines[4], StringComparison.Ordinal);
    }

    [Fact]
    public void AClickOnTheBox_TogglesIt_AndOnTheLabelOnlySelects()
    {
        var (state, root, _, _, _, b) = Tree();
        state.Expand(root);
        var lines = Draw(state);
        var y = Array.FindIndex(lines, l => l.Contains("[ ] b", StringComparison.Ordinal));
        var box = lines[y].IndexOf('[', StringComparison.Ordinal);

        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, box + 1, y));
        Assert.True(state.IsChecked(b));
        Assert.Equal(b, state.Selected);

        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, box + 5, y));
        Assert.True(state.IsChecked(b));
    }

    // Async children

    private sealed class Loader
    {
        public Dictionary<string, TaskCompletionSource<IReadOnlyList<Node>>> Pending { get; } = [];
        public List<string> Calls { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<IReadOnlyList<Node>> Load(Node node, CancellationToken token)
        {
            Calls.Add(node.Name);
            Tokens.Add(token);
            var tcs = new TaskCompletionSource<IReadOnlyList<Node>>();
            token.Register(() => tcs.TrySetCanceled(token));
            Pending[node.Name] = tcs;
            return tcs.Task;
        }
    }

    [Fact]
    public void AnUnloadedNode_ShowsLoading_ThenItsChildren()
    {
        var loader = new Loader();
        var root = new Node("root");
        var state = TreeState.Async<Node>(loader.Load, n => n.Name);
        state.Roots = [root];
        Assert.True(state.Rows[0].HasChildren);

        state.Expand(root);
        Assert.Equal(["root"], loader.Calls);
        Assert.Contains("root Loading…", Draw(state)[1], StringComparison.Ordinal);

        loader.Pending["root"].SetResult([new Node("x"), new Node("y")]);

        Assert.Equal(["root", "x", "y"], state.Rows.Select(r => r.Node.Name));
        Assert.DoesNotContain("Loading", Draw(state)[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ANodeLoadsOnce()
    {
        var loader = new Loader();
        var root = new Node("root");
        var state = TreeState.Async<Node>(loader.Load, n => n.Name);
        state.Roots = [root];

        state.Expand(root);
        Draw(state);
        state.Collapse(root);
        state.Expand(root);
        loader.Pending["root"].SetResult([]);
        state.Collapse(root);
        state.Expand(root);

        Assert.Equal(["root"], loader.Calls);
        Assert.False(state.Rows[0].HasChildren);
    }

    [Fact]
    public void AFailedLoad_ShowsTheError_Collapses_AndExpandingRetries()
    {
        var loader = new Loader();
        var root = new Node("root");
        var state = TreeState.Async<Node>(loader.Load, n => n.Name);
        state.Roots = [root];
        var collapsed = 0;
        state.Collapsed += (_, _) => collapsed++;

        state.Expand(root);
        loader.Pending["root"].SetException(new IOException("Disk on fire"));

        Assert.False(state.IsExpanded(root));
        Assert.Equal(1, collapsed);
        Assert.Contains("root Disk on fire", Draw(state, width: 30)[1], StringComparison.Ordinal);

        state.Expand(root);
        Assert.Equal(["root", "root"], loader.Calls);
        Assert.Contains("Loading…", Draw(state, width: 30)[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Reload_CancelsAndLoadsAgain_WhileExpanded()
    {
        var loader = new Loader();
        var root = new Node("root");
        var state = TreeState.Async<Node>(loader.Load, n => n.Name);
        state.Roots = [root];
        state.Expand(root);

        state.Reload(root);

        Assert.True(loader.Tokens[0].IsCancellationRequested);
        Assert.Equal(["root", "root"], loader.Calls);
        loader.Pending["root"].SetResult([new Node("fresh")]);
        Assert.Equal(["root", "fresh"], state.Rows.Select(r => r.Node.Name));
    }

    [Fact]
    public void Reload_WhileCollapsed_LoadsAtTheNextExpand()
    {
        var loader = new Loader();
        var root = new Node("root");
        var state = TreeState.Async<Node>(loader.Load, n => n.Name);
        state.Roots = [root];
        state.Expand(root);
        loader.Pending["root"].SetResult([new Node("old")]);
        state.Collapse(root);

        state.Reload(root);
        Assert.Single(loader.Calls);
        state.Expand(root);

        Assert.Equal(2, loader.Calls.Count);
    }

    [Fact]
    public void ExpandAll_StartsLoadsForWhatIsntLoaded()
    {
        var loader = new Loader();
        var a = new Node("a");
        var b = new Node("b");
        var state = TreeState.Async<Node>(loader.Load, n => n.Name);
        state.Roots = [a, b];

        state.ExpandAll();

        Assert.Equal(["a", "b"], loader.Calls);
    }

    [Fact]
    public void AParentCheckedBeforeItLoads_ChecksItsChildren()
    {
        var loader = new Loader();
        var root = new Node("root");
        var state = TreeState.Async<Node>(loader.Load, n => n.Name);
        state.Roots = [root];
        state.Checkboxes = true;

        state.SetChecked(root, true);
        state.Expand(root);
        loader.Pending["root"].SetResult([new Node("x")]);

        Assert.True(state.IsChecked(state.Rows[1].Node));
        Assert.True(state.IsChecked(root));
    }

    [Fact]
    public async Task RevealAsync_LoadsThePath_ThenReveals()
    {
        var loader = new Loader();
        var byName = EqualityComparer<Node>.Create((x, y) => x?.Name == y?.Name, n => n.Name.GetHashCode(StringComparison.Ordinal));
        var state = TreeState.Async<Node>(loader.Load, n => n.Name, byName);
        state.Roots = [new Node("root")];

        var reveal = state.RevealAsync([new Node("root"), new Node("dir"), new Node("file")]);
        loader.Pending["root"].SetResult([new Node("dir")]);
        loader.Pending["dir"].SetResult([new Node("file")]);

        Assert.True(await reveal);
        Assert.Equal("file", state.Selected?.Name);
        Assert.Equal(["root", "dir"], loader.Calls);
    }

    [Fact]
    public async Task RevealAsync_APathThatDoesntLoad_IsFalse()
    {
        var loader = new Loader();
        var byName = EqualityComparer<Node>.Create((x, y) => x?.Name == y?.Name, n => n.Name.GetHashCode(StringComparison.Ordinal));
        var state = TreeState.Async<Node>(loader.Load, n => n.Name, byName);
        state.Roots = [new Node("root")];

        var reveal = state.RevealAsync([new Node("root"), new Node("missing"), new Node("file")]);
        loader.Pending["root"].SetResult([new Node("dir")]);

        Assert.False(await reveal);
        Assert.False(state.IsExpanded(new Node("root")));
    }

    // Through an app

    private sealed class Files : Screen
    {
        public TreeState<Node> State { get; }

        public Files(Func<Node, CancellationToken, Task<IReadOnlyList<Node>>> load)
        {
            State = TreeState.Async(load, n => n.Name);
            State.Roots = [new Node("root")];
        }

        public override bool HandleKey(ConsoleKeyInfo key) => State.HandleKey(key);

        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) => Widget.Tree(State).Draw(buf, area);
    }

    [Fact]
    public void InAnApp_LoadsRunAsAppWork_AndTheNextFrameShowsThem()
    {
        var screen = new Files(async (node, ct) =>
        {
            await Task.Delay(10, ct);
            return [new Node("child")];
        });
        using var harness = new ComponentHarness(screen, 20, 6);
        harness.RunUntilIdle();

        harness.Press("right");

        Assert.Contains("child", harness.Lines[2], StringComparison.Ordinal);
    }
}
