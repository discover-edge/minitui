using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Hover: moves, routing and redraws, ctx.Hover, the builders and the menus (1.x spec §17.5–§17.7).</summary>
public class HoverTests
{
    private static MouseEvent Move(int x, int y) => new(MouseAction.Move, MouseButton.None, x, y);

    // Two side-by-side children that log what they see
    private sealed class Pane(string name, List<string> log, bool readsHover = true) : Component
    {
        public Vec2? Seen { get; private set; }
        public int Renders { get; private set; }

        public override bool HandleMouse(MouseEvent mouse)
        {
            log.Add($"{name} {mouse.Action} {mouse.X},{mouse.Y}");
            return false;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            Renders++;
            if (readsHover) Seen = ctx.Hover;
        }
    }

    private sealed class Split : Screen
    {
        public Pane Left { get; }
        public Pane Right { get; }
        public Vec2? Seen { get; private set; }

        private readonly bool readsHover;

        public Split(List<string> log, bool readsHover = true)
        {
            this.readsHover = readsHover;
            Left = AddChild(new Pane("left", log, readsHover));
            Right = AddChild(new Pane("right", log, readsHover));
            Focus.Add(Left).Add(Right);
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            if (readsHover) Seen = ctx.Hover;
            ctx.Render(Left, buf, area with { Width = area.Width / 2 });
            ctx.Render(Right, buf, area with { X = area.X + area.Width / 2, Width = area.Width - area.Width / 2 });
        }
    }

    private static (ComponentHarness Harness, Split Screen, List<string> Log) Start(bool hover = true, bool readsHover = true)
    {
        var log = new List<string>();
        var screen = new Split(log, readsHover);
        var harness = new ComponentHarness(screen, 20, 6);
        if (hover) harness.App.WithHover();
        harness.RunUntilIdle();
        return (harness, screen, log);
    }

    // Moves and routing

    [Fact]
    public void WithoutHover_MovesAreDropped()
    {
        var (harness, screen, log) = Start(hover: false);
        using var _h = harness;
        var seen = new List<MouseAction>();
        harness.App.OnMouse(m => { seen.Add(m.Action); return false; });

        harness.Mouse(Move(2, 2));

        Assert.Empty(seen);
        Assert.Empty(log);
        Assert.Null(harness.App.PointerPosition);
        Assert.Null(screen.Left.Seen);
    }

    [Fact]
    public void Moves_GoToOnMouse_ThenTheComponentUnderThePointer_WithoutFocusingIt()
    {
        var (harness, screen, log) = Start();
        using var _h = harness;
        var seen = new List<MouseAction>();
        harness.App.OnMouse(m => { seen.Add(m.Action); return false; });

        harness.Mouse(Move(15, 2));

        Assert.Equal([MouseAction.Move], seen);
        Assert.Equal(["right Move 15,2"], log);
        Assert.Equal(new Vec2(15, 2), harness.App.PointerPosition);
    }

    [Fact]
    public void CtxHover_IsSetForTheTargetAndItsAncestors_Only()
    {
        var (harness, screen, _) = Start();
        using var _h = harness;

        harness.Mouse(Move(3, 2));
        harness.Mouse(Move(3, 3));

        Assert.Equal(new Vec2(3, 3), screen.Left.Seen);
        Assert.Equal(new Vec2(3, 3), screen.Seen);
        Assert.Null(screen.Right.Seen);
    }

    [Fact]
    public void UnderAnOverlay_CtxHoverIsNull()
    {
        var (harness, screen, _) = Start();
        using var _h = harness;
        _ = harness.App.Overlays.ConfirmAsync("Sure?");
        harness.RunUntilIdle();

        harness.Mouse(Move(0, 0));
        harness.Mouse(Move(1, 0));

        Assert.Null(screen.Left.Seen);
        Assert.Null(screen.Seen);
    }

    [Fact]
    public void AMove_Redraws_OnlyWhenTheHoverChangesOrSomethingDrewWithIt()
    {
        var (harness, screen, _) = Start(readsHover: false);
        using var _h = harness;
        harness.Mouse(Move(3, 2));
        var renders = screen.Left.Renders;

        // The harness draws one frame after each step; a move that needs one draws another before it.
        // Same target, and nothing drew with the pointer: no frame of its own
        harness.Mouse(Move(4, 2));
        Assert.Equal(renders + 1, screen.Left.Renders);

        // Another target: a frame
        harness.Mouse(Move(15, 2));
        Assert.Equal(renders + 3, screen.Left.Renders);
    }

    [Fact]
    public void AMove_WithinAComponentThatDrewWithThePointer_Redraws()
    {
        var (harness, screen, _) = Start();
        using var _h = harness;
        harness.Mouse(Move(3, 2));
        var renders = screen.Left.Renders;

        harness.Mouse(Move(4, 2));

        // Its own frame, and the harness's
        Assert.Equal(renders + 2, screen.Left.Renders);
    }

    [Fact]
    public void QueuedMoves_AreCoalesced()
    {
        var (harness, _, log) = Start();
        using var _h = harness;

        harness.App.InjectMouse(Move(1, 1));
        harness.App.InjectMouse(Move(2, 1));
        harness.App.InjectMouse(Move(15, 1));
        harness.RunUntilIdle();

        Assert.Equal(["right Move 15,1"], log);
    }

    // Builders

    private static readonly string[] Items = ["one", "two", "three"];

    [Fact]
    public void AList_PatchesTheRowUnderThePointer_ButNotTheSelection()
    {
        var buf = new Buffer(12, 5);
        var state = new SelectionState { Selected = 0 };

        Widget.List().Items(Items).State(state).Hover(new Vec2(4, 2)).Draw(buf, buf.Area);
        Assert.True(buf.Get(2, 2).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.False(buf.Get(2, 3).Modifiers.HasFlag(Modifiers.Underlined));

        buf = new Buffer(12, 5);
        Widget.List().Items(Items).State(state).Hover(new Vec2(4, 1)).Draw(buf, buf.Area);
        Assert.False(buf.Get(2, 1).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.Equal(Theme.Current.SelectionUnfocused.Bg, buf.Get(2, 1).Bg);
    }

    [Fact]
    public void ATableAndATree_PatchTheRowUnderThePointer()
    {
        var table = new Buffer(12, 6);
        Widget.Table().Header("Name").Columns(Constraint.Length(8)).Rows(Items, i => new Row(i)).Hover(new Vec2(3, 3)).Draw(table, table.Area);
        Assert.True(table.Get(2, 3).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.False(table.Get(2, 4).Modifiers.HasFlag(Modifiers.Underlined));

        var tree = new Buffer(12, 5);
        var state = new TreeState<string>(_ => [], s => s) { Roots = Items };
        Widget.Tree(state).Hover(new Vec2(3, 3)).Draw(tree, tree.Area);
        Assert.True(tree.Get(2, 3).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.False(tree.Get(2, 1).Modifiers.HasFlag(Modifiers.Underlined));
    }

    [Fact]
    public void AButtonACheckboxAndATab_ShowHover()
    {
        var buf = new Buffer(20, 3);
        Widget.Button().Label("OK").Hover(new Vec2(1, 0)).Draw(buf, new Rect(0, 0, 6, 1));
        Widget.Button().Label("No").Hover(new Vec2(1, 0)).Draw(buf, new Rect(8, 0, 6, 1));
        Assert.True(buf.Get(0, 0).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.False(buf.Get(8, 0).Modifiers.HasFlag(Modifiers.Underlined));

        Widget.Checkbox().Label("Go").Hover(new Vec2(4, 1)).Draw(buf, new Rect(0, 1, 20, 1));
        Assert.True(buf.Get(0, 1).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.True(buf.Get(4, 1).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.False(buf.Get(6, 1).Modifiers.HasFlag(Modifiers.Underlined));

        var tabs = new TabBarState();
        Widget.TabBar().Tabs("One", "Two").State(tabs).Hover(new Vec2(10, 2)).Draw(buf, new Rect(0, 2, 20, 1));
        Assert.True(buf.Get(10, 2).Modifiers.HasFlag(Modifiers.Underlined));
        Widget.TabBar().Tabs("One", "Two").State(tabs).Hover(new Vec2(1, 2)).Draw(buf, new Rect(0, 2, 20, 1));
        Assert.False(buf.Get(1, 2).Modifiers.HasFlag(Modifiers.Underlined));
    }

    [Fact]
    public void BuiltInThemes_SetHover()
    {
        Assert.All([Theme.Default, Theme.Dark, Theme.Light, Theme.Monochrome], t => Assert.NotEqual(default, t.Hover));
        Assert.NotNull(Theme.Dark.Hover.Bg);
        Assert.NotNull(Theme.Light.Hover.Bg);
    }

    // Menus

    private sealed class Blank : Screen
    {
        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    [Fact]
    public async Task InAPopupMenu_TheSelectionFollowsThePointer_SkippingWhatCantBeChosen()
    {
        using var harness = new ComponentHarness(new Blank(), 30, 12);
        harness.App.WithHover();
        harness.RunUntilIdle();
        var result = harness.App.Overlays.ShowMenuAsync([new("One"), MenuItem.Separator, new("Off") { Enabled = false }, new("Two")], new Rect(0, 0, 1, 1));
        harness.RunUntilIdle();
        var menu = Assert.IsType<PopupMenu>(harness.App.Overlays.Top);
        var top = menu.Area.Y;

        harness.Mouse(Move(menu.Area.X + 2, top + 4));
        harness.Mouse(Move(menu.Area.X + 2, top + 3));
        harness.Mouse(Move(menu.Area.X + 2, top + 2));
        harness.Press("enter");

        Assert.True(result.IsCompleted);
        Assert.Equal(3, await result);
    }

    [Fact]
    public void WithAMenuOpen_MovingOverAnotherTitle_SwitchesToIt()
    {
        var bar = new MenuBar(new Menu("&File", [new("x")]), new Menu("&Edit", [new("y")]));
        using var harness = new ComponentHarness(new Blank(), 40, 10);
        harness.App.WithHover().WithMenuBar(bar);
        harness.RunUntilIdle();

        harness.Mouse(Move(8, 0));
        Assert.False(bar.IsOpen);
        Assert.True(harness.CellAt(8, 0).Modifiers.HasFlag(Modifiers.Underlined));

        harness.Press("f10");
        harness.Mouse(Move(8, 0));
        Assert.Equal(1, bar.OpenIndex);
    }

    [Fact]
    public void InThePalette_TheSelectionFollowsThePointer()
    {
        using var harness = new ComponentHarness(new Blank(), 50, 16);
        harness.App.WithHover();
        harness.RunUntilIdle();
        var ran = new List<string>();
        harness.App.Overlays.Show(new CommandPalette([new PaletteItem("Alpha", () => ran.Add("a")), new PaletteItem("Beta", () => ran.Add("b"))]));
        harness.RunUntilIdle();
        var palette = harness.App.Overlays.Top!;
        var row = Enumerable.Range(palette.Area.Y, palette.Area.Height).First(y => harness.Lines[y].Contains("Beta", StringComparison.Ordinal));

        harness.Mouse(Move(palette.Area.X + 3, row));
        harness.Press("enter");

        Assert.Equal(["b"], ran);
    }
}
