namespace MiniTui.Tests;

/// <summary>Fixes from building apps from the docs, and TextFieldState.Changed and ConfirmAsync(yesFirst) (1.x spec §22.3, §22.4).</summary>
[Collection("Console")]
public sealed class AppFixesTests : IDisposable
{
    public AppFixesTests() => Clipboard.Reset();

    public void Dispose() => Clipboard.Reset();

    private static ConsoleKeyInfo Key(string binding) => KeyBinding.Parse(binding).ToKeyInfo();

    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.NoName, false, false, false);

    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;
        public Node[] Children { get; } = children;
    }

    private static TreeState<Node> Tree() =>
        new(n => n.Children, n => n.Name) { Roots = [new Node("alpha", new Node("one")), new Node("beta")] };

    private sealed class TreeScreen(TreeState<Node> tree) : Screen
    {
        public override bool HandleKey(ConsoleKeyInfo key) => tree.HandleKey(key);
        public override bool HandleMouse(MouseEvent mouse) => tree.HandleMouse(mouse);
        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) => Widget.Tree(tree).Draw(buf, area);
    }

    private sealed class Blank(string title = "Blank") : Screen
    {
        public override string Title => title;
        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) { }
    }

    // Fix 1

    [Fact]
    public void TypeAhead_HandlesOnlyKeysThatMatchARow()
    {
        var tree = Tree();

        Assert.True(tree.HandleKey(Char('b')));
        Assert.Equal("beta", tree.Selected?.Name);
        Assert.False(tree.HandleKey(Char('?')));
        Assert.Equal("beta", tree.Selected?.Name);
    }

    [Fact]
    public void QuestionMark_OpensHelp_WithATreeFocused()
    {
        using var harness = new ComponentHarness(new TreeScreen(Tree()), 40, 12);

        harness.Press("?");

        Assert.IsType<HelpOverlay>(harness.App.Overlays.Top);
    }

    // Fix 2

    [Fact]
    public void TwoQuickClicksOnTheExpander_OpenAndCloseIt()
    {
        var tree = Tree();
        var buf = new CellBuffer(30, 5);
        Widget.Tree(tree).Draw(buf, buf.Area);
        var row = Array.FindIndex(buf.ToLines(), l => l.Contains("alpha", StringComparison.Ordinal));
        var x = buf.ToLines()[row].IndexOf('▸', StringComparison.Ordinal);

        tree.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, x, row) { ClickCount = 1 });
        Assert.True(tree.IsExpanded(tree.Roots[0]));
        tree.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, x, row) { ClickCount = 2 });

        Assert.False(tree.IsExpanded(tree.Roots[0]));
    }

    // Fix 3

    [Fact]
    public void AGaugeLabel_OverTheEmptyPart_TakesTheThemesTextColor()
    {
        using var theme = Theme.Use(Theme.Light);
        var buf = new CellBuffer(20, 1);

        Widget.Gauge().Ratio(0.03).Draw(buf, buf.Area);

        var x = buf.ToLines()[0].IndexOf('3', StringComparison.Ordinal);
        Assert.Equal(Theme.Light.Text.Fg, buf.Get(x, 0).Fg);
        Assert.Equal(Theme.Light.Text.Fg, buf.Get(x + 1, 0).Fg);
    }

    // Fix 4

    [Theory]
    [InlineData("alt+shift+left")]
    [InlineData("alt+shift+right")]
    [InlineData("alt+up")]
    [InlineData("alt+home")]
    public void TheTextStates_LeaveAltWithTheMovementKeys(string binding)
    {
        var field = new TextFieldState { Text = "abc def" };
        var area = new TextAreaState("one\ntwo");
        var line = new CommandLineState { Text = ":open" };
        line.History = ["first"];

        Assert.False(field.HandleKey(Key(binding)));
        Assert.False(area.HandleKey(Key(binding)));
        Assert.False(line.HandleKey(Key(binding)));
        Assert.False(field.HasSelection);
        Assert.Equal(":open", line.Text);
    }

    // Fix 5

    [Fact]
    public void ThePalette_ListsAnActionOnce_ThoughItsBothABindingAndAMenuItem()
    {
        var saves = 0;
        void Save() => saves++;
        using var harness = new ComponentHarness(new Blank(), 60, 10);
        harness.App.Keys.Add("ctrl+s", "Save", Save);
        harness.App.WithMenuBar(new MenuBar(new Menu("&File", [new("&Save") { Run = Save }])));

        var items = CommandPalette.ItemsFor(harness.App);

        var save = Assert.Single(items, i => i.Label.Contains("Save", StringComparison.Ordinal));
        Assert.Equal("ctrl+s", save.Hint);
    }

    // Fix 6

    [Fact]
    public void Toasts_StartBelowTheMenuBar()
    {
        using var harness = new ComponentHarness(new Blank(), 60, 10);
        harness.App.WithMenuBar(new MenuBar(new Menu("&File", [new("&Quit") { Run = () => { } }])));
        harness.App.Toasts.Push("Saved");
        harness.RunUntilIdle();

        Assert.Contains("File", harness.Lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Saved", harness.Lines[0], StringComparison.Ordinal);
        Assert.Contains(harness.Lines, l => l.Contains("Saved", StringComparison.Ordinal));
    }

    // Fix 7

    [Fact]
    public void TheHints_GetTheStatusBarFirst_AndTheBreadcrumbWhatsLeft()
    {
        using var harness = new ComponentHarness(new Blank("A very long first screen title"), 40, 6);
        harness.App.Screens.Push(new Blank("Another long screen title"));
        harness.RunUntilIdle();

        var bar = harness.Lines[^1];
        Assert.Contains("? Help", bar, StringComparison.Ordinal);
        Assert.Contains("esc Back", bar, StringComparison.Ordinal);
        Assert.Contains("title", bar, StringComparison.Ordinal);
    }

    // Fix 8

    [Fact]
    public void APushedScreen_HintsEscBack_UnlessEscIsBound()
    {
        using var harness = new ComponentHarness(new Blank(), 60, 6);
        Assert.DoesNotContain("esc Back", harness.Lines[^1], StringComparison.Ordinal);

        var second = new Blank("Second");
        harness.App.Screens.Push(second);
        harness.RunUntilIdle();
        Assert.Contains("esc Back", harness.Lines[^1], StringComparison.Ordinal);

        second.Keys.Add("esc", "Close", () => { });
        harness.RunUntilIdle();
        Assert.DoesNotContain("esc Back", harness.Lines[^1], StringComparison.Ordinal);
        Assert.Contains("esc Close", harness.Lines[^1], StringComparison.Ordinal);
    }

    // Fix 9 and fix 10

    private sealed class AskName : Overlay
    {
        private readonly TaskCompletionSource<string?> _result = new();
        private readonly TextFieldState _name = new();
        public Task<string?> Result => _result.Task;
        public override string? Title => "Name";
        public override bool CapturesText => true;
        public override Rect Placement(Rect screen) => Centered(screen, 30, 3);

        public override bool HandleKey(ConsoleKeyInfo key)
        {
            if (key.Key != ConsoleKey.Enter) return _name.HandleKey(key);
            _result.TrySetResult(_name.Text);
            Close();
            return true;
        }

        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) => Widget.TextField().State(_name).Focused().Draw(buf, area);
        protected override void OnUnmount() => _result.TrySetResult(null);
    }

    [Fact]
    public void AnOverlaysTitle_IsPadded_AndAwaitingACustomDialog_DoesntKeepTheHarnessBusy()
    {
        using var harness = new ComponentHarness(new Blank(), 60, 12);
        string? answer = null;
        harness.App.RunTask(async _ =>
        {
            var dialog = new AskName();
            harness.App.Overlays.Show(dialog);
            answer = await dialog.Result;
        });

        harness.RunUntilIdle();
        Assert.Contains(harness.Lines, l => l.Contains("┌ Name ─", StringComparison.Ordinal));

        harness.Type("Ada");
        harness.Press("enter");
        Assert.Equal("Ada", answer);
    }

    // Additions

    [Fact]
    public void TextFieldChanged_IsRaisedOncePerEdit_AndNotForMoves()
    {
        var field = new TextFieldState { Text = "hello" };
        var changes = 0;
        field.Changed += (_, _) => changes++;

        field.HandleKey(Char('!'));
        Assert.Equal(1, changes);
        field.HandleKey(Key("left"));
        field.HandleKey(Key("shift+home"));
        Assert.Equal(1, changes);
        field.Paste("bye");                 // replaces the selection: one change
        Assert.Equal(2, changes);
        field.Undo();
        Assert.Equal(3, changes);
        field.Text = field.Text;
        Assert.Equal(3, changes);
        field.Text = "";
        Assert.Equal(4, changes);
    }

    [Fact]
    public void ConfirmAsync_WithYesFirstFalse_SelectsNo()
    {
        using var harness = new ComponentHarness(new Blank(), 60, 12);
        bool? answer = null;
        harness.App.RunTask(async _ => answer = await harness.App.Overlays.ConfirmAsync("Delete it?", yesFirst: false));
        harness.RunUntilIdle();

        harness.Press("enter");

        Assert.False(answer);
    }
}
