using Microsoft.Extensions.Time.Testing;
using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>PopupMenu, the Overlay additions and Select (1.x spec §7).</summary>
public class PopupMenuTests
{
    private sealed class Blank(List<string>? log = null) : Screen
    {
        public override bool HandleMouse(MouseEvent mouse)
        {
            log?.Add($"screen {mouse.Action}");
            return true;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    private static readonly MenuItem[] Fruit =
    [
        new("Apple") { Hint = "a" },
        new("Banana"),
        MenuItem.Separator,
        new("Blueberry") { Enabled = false },
        new("Cherry") { Hint = "Ctrl+C" },
    ];

    private static (ComponentHarness Harness, PopupMenu Menu) Open(MenuItem[] items, Rect anchor, int selected = 0,
        int width = 40, int height = 20, FakeTimeProvider? time = null, int maxRows = 12, int minWidth = 0)
    {
        var harness = new ComponentHarness(new Blank(), width, height, time);
        var menu = new PopupMenu(items, anchor, selected) { MaxRows = maxRows, MinWidth = minWidth };
        harness.App.Overlays.Show(menu);
        harness.RunUntilIdle();
        return (harness, menu);
    }

    private static string[] Trimmed(ComponentHarness harness) => [.. harness.Lines.Select(l => l.TrimEnd())];

    private static int RowOf(ComponentHarness harness, string text) =>
        Array.FindIndex(harness.Lines, l => l.Contains(text, StringComparison.Ordinal));

    // Keys

    [Fact]
    public async Task Enter_AnswersTheSelectedIndex()
    {
        var (harness, menu) = Open(Fruit, new Rect(0, 0, 1, 1));
        using var _ = harness;

        harness.Press("down enter");

        Assert.Equal(1, await menu.Result);
        Assert.False(harness.App.Overlays.Any);
    }

    [Theory]
    [InlineData("esc")]
    [InlineData("tab")]
    [InlineData("shift+tab")]
    public async Task EscAndTab_Dismiss(string key)
    {
        var (harness, menu) = Open(Fruit, new Rect(0, 0, 1, 1));
        using var _ = harness;

        harness.Press(key);

        Assert.Null(await menu.Result);
        Assert.False(harness.App.Overlays.Any);
    }

    [Fact]
    public void UpAndDown_SkipSeparatorsAndDisabledItems_AndWrap()
    {
        var (harness, menu) = Open(Fruit, new Rect(0, 0, 1, 1));
        using var _ = harness;

        harness.Press("down");
        Assert.Equal(1, menu.Selected);
        harness.Press("down");
        Assert.Equal(4, menu.Selected); // past the separator and the disabled Blueberry
        harness.Press("down");
        Assert.Equal(0, menu.Selected); // wraps
        harness.Press("up");
        Assert.Equal(4, menu.Selected);
    }

    [Fact]
    public void HomeEndAndPages_StopAtTheEnabledEnds()
    {
        MenuItem[] items = [new("off") { Enabled = false }, .. Enumerable.Range(1, 30).Select(i => new MenuItem($"Item {i}")), new("last off") { Enabled = false }];
        var (harness, menu) = Open(items, new Rect(0, 0, 1, 1), maxRows: 5);
        using var _ = harness;
        Assert.Equal(1, menu.Selected); // the disabled first item is skipped

        harness.Press("end");
        Assert.Equal(30, menu.Selected);
        harness.Press("home");
        Assert.Equal(1, menu.Selected);
        harness.Press("pgdn");
        Assert.Equal(6, menu.Selected); // by the 5 visible rows
        harness.Press("pgdn pgdn pgdn pgdn pgdn pgdn");
        Assert.Equal(30, menu.Selected);
        harness.Press("pgup pgup pgup pgup pgup pgup pgup");
        Assert.Equal(1, menu.Selected);
    }

    [Fact]
    public void TheFirstSelection_MovesOffADisabledItem()
    {
        var (harness, menu) = Open(Fruit, new Rect(0, 0, 1, 1), selected: 3);
        using var _ = harness;

        Assert.Equal(4, menu.Selected);
    }

    [Fact]
    public async Task WithNothingEnabled_EnterDoesNothing()
    {
        var (harness, menu) = Open([new("a") { Enabled = false }, MenuItem.Separator], new Rect(0, 0, 1, 1));
        using var _ = harness;

        harness.Press("down enter");

        Assert.True(harness.App.Overlays.Any);
        Assert.False(menu.Result.IsCompleted);
        harness.Press("esc");
        Assert.Null(await menu.Result);
    }

    [Fact]
    public void OtherKeys_DontReachTheScreen()
    {
        var quit = false;
        var (harness, _) = Open(Fruit, new Rect(0, 0, 1, 1));
        using var h = harness;
        harness.App.Keys.Add("q", "Quit", () => quit = true);

        harness.Press("q f5 ctrl+s");

        Assert.False(quit);
        Assert.True(harness.App.Overlays.Any);
    }

    // Type-ahead

    private static readonly MenuItem[] Words =
        [new("Apple"), new("Banana"), new("Blueberry"), new("blackberry"), new("Cherry"), new("Bad") { Enabled = false }];

    [Fact]
    public void TypeAhead_MatchesAPrefix_IgnoringCase()
    {
        var (harness, menu) = Open(Words, new Rect(0, 0, 1, 1), time: new FakeTimeProvider());
        using var _ = harness;

        harness.Type("BL");
        Assert.Equal(2, menu.Selected);
        harness.Type("a");
        Assert.Equal(3, menu.Selected); // "bla"
        harness.Type("z");
        Assert.Equal(3, menu.Selected); // no match: stays
    }

    [Fact]
    public void TypeAhead_TheSameLetterAgain_Cycles_SkippingDisabledItems()
    {
        var (harness, menu) = Open(Words, new Rect(0, 0, 1, 1), time: new FakeTimeProvider());
        using var _ = harness;

        harness.Type("b");
        Assert.Equal(1, menu.Selected);
        harness.Type("b");
        Assert.Equal(2, menu.Selected);
        harness.Type("b");
        Assert.Equal(3, menu.Selected);
        harness.Type("b");
        Assert.Equal(1, menu.Selected); // Bad is disabled; wraps to Banana
    }

    [Fact]
    public void TypeAhead_ResetsAfterOneSecond()
    {
        var time = new FakeTimeProvider();
        var (harness, menu) = Open(Words, new Rect(0, 0, 1, 1), time: time);
        using var _ = harness;

        harness.Type("c");
        Assert.Equal(4, menu.Selected);
        time.Advance(TimeSpan.FromMilliseconds(999));
        harness.Type("h");
        Assert.Equal(4, menu.Selected); // "ch"
        time.Advance(TimeSpan.FromSeconds(1));
        harness.Type("a");
        Assert.Equal(0, menu.Selected); // a new search: "a"
    }

    [Fact]
    public void TypeAhead_NeverChooses()
    {
        var (harness, menu) = Open([new("Only")], new Rect(0, 0, 1, 1), time: new FakeTimeProvider());
        using var _ = harness;

        harness.Type("only");

        Assert.False(menu.Result.IsCompleted);
    }

    // Placement and drawing

    [Fact]
    public void Draws_LabelsHintsSeparatorsAndTheSelection()
    {
        var (harness, _) = Open(Fruit, new Rect(2, 1, 1, 1));
        using var h = harness;

        Assert.Equal(
        [
            "",
            "",
            "  ┌───────────────────┐",
            "  │ Apple           a │",
            "  │ Banana            │",
            "  ├───────────────────┤",
            "  │ Blueberry         │",
            "  │ Cherry     Ctrl+C │",
            "  └───────────────────┘",
        ], Trimmed(harness)[..9]);
        var theme = Theme.Default;
        Assert.Equal(theme.Selection.Bg, harness.CellAt(3, 3).Bg);
        Assert.Equal(theme.Selection.Bg, harness.CellAt(20, 3).Bg);
        Assert.Equal(theme.TextMuted.Fg, harness.CellAt(4, 6).Fg); // disabled
        Assert.Equal(theme.TextMuted.Fg, harness.CellAt(15, 7).Fg); // hint
    }

    [Fact]
    public void OpensBelowTheAnchor_OrAboveWhenItOnlyFitsThere()
    {
        MenuItem[] items = [new("One"), new("Two")];
        var (harness, menu) = Open(items, new Rect(5, 2, 10, 1));
        using var _ = harness;
        var screen = new Rect(0, 0, 40, 19); // above the status bar

        Assert.Equal(new Rect(5, 3, 7, 4), menu.Placement(screen));
        Assert.Equal(new Rect(5, 13, 7, 4), new PopupMenu(items, new Rect(5, 17, 10, 1)).Placement(screen));
    }

    [Fact]
    public void FittingNeitherSide_TakesTheLargerOne_AndScrolls()
    {
        MenuItem[] items = [.. Enumerable.Range(1, 30).Select(i => new MenuItem($"Item {i}"))];
        var screen = new Rect(0, 0, 40, 19);

        Assert.Equal(new Rect(0, 0, 11, 12), new PopupMenu(items, new Rect(0, 12, 1, 1)) { MaxRows = 30 }.Placement(screen));
        Assert.Equal(new Rect(0, 5, 11, 14), new PopupMenu(items, new Rect(0, 4, 1, 1)) { MaxRows = 30 }.Placement(screen));

        var (harness, _) = Open(items, new Rect(0, 4, 1, 1), maxRows: 30);
        using var h = harness;
        harness.Press("end");
        var lines = Trimmed(harness);
        Assert.Contains(lines, l => l.Contains("Item 30", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Item 1 ", StringComparison.Ordinal));
    }

    [Fact]
    public void ShiftsLeft_AtTheRightEdge_AndHonorsMinWidthAndMaxRows()
    {
        MenuItem[] items = [.. Enumerable.Range(1, 30).Select(i => new MenuItem($"Item {i}"))];
        var screen = new Rect(0, 0, 40, 19);

        Assert.Equal(new Rect(29, 1, 11, 7), new PopupMenu(items, new Rect(38, 0, 1, 1)) { MaxRows = 5 }.Placement(screen));
        Assert.Equal(new Rect(0, 1, 25, 14), new PopupMenu(items, new Rect(0, 0, 25, 1)) { MinWidth = 25 }.Placement(screen));
    }

    [Fact]
    public void LongLabels_AreTruncated()
    {
        var (harness, _) = Open([new(new string('x', 50)) { Hint = "F1" }], new Rect(0, 0, 1, 1), width: 20);
        using var h = harness;

        Assert.Equal("│ xxxxxxxxxxx…  F1 │", Trimmed(harness)[2]);
    }

    [Fact]
    public void AfterAResize_TheMenuIsPlacedAgain_OnTheScreen()
    {
        var menu = new PopupMenu([new("One"), new("Two")], new Rect(30, 15, 1, 1));

        Assert.Equal(new Rect(30, 11, 7, 4), menu.Placement(new Rect(0, 0, 40, 19)));
        Assert.Equal(new Rect(13, 5, 7, 4), menu.Placement(new Rect(0, 0, 20, 9))); // the anchor is now off the screen
    }

    [Fact]
    public void TheScrollbar_ShowsOnTheRightBorder()
    {
        MenuItem[] items = [.. Enumerable.Range(1, 30).Select(i => new MenuItem($"Item {i}"))];
        var (harness, _) = Open(items, new Rect(0, 0, 1, 1), maxRows: 5);
        using var h = harness;

        var right = string.Concat(Enumerable.Range(2, 5).Select(y => harness.Lines[y][10]));
        Assert.NotEqual("│││││", right);
    }

    // Mouse

    [Fact]
    public async Task ClickingAnItem_ChoosesIt()
    {
        var (harness, menu) = Open(Fruit, new Rect(2, 1, 1, 1));
        using var _ = harness;

        harness.Click(6, RowOf(harness, "Cherry"));

        Assert.Equal(4, await menu.Result);
    }

    [Theory]
    [InlineData("Blueberry")] // disabled
    [InlineData("├")]         // a separator
    [InlineData("┌")]         // the border
    public void ClickingWhatCantBeChosen_DoesNothing(string text)
    {
        var (harness, menu) = Open(Fruit, new Rect(2, 1, 1, 1));
        using var _ = harness;

        harness.Click(harness.Lines[RowOf(harness, text)].IndexOf(text[0]) + 1, RowOf(harness, text));

        Assert.True(harness.App.Overlays.Any);
        Assert.False(menu.Result.IsCompleted);
    }

    [Fact]
    public void TheWheel_MovesTheSelection()
    {
        var (harness, menu) = Open(Fruit, new Rect(2, 1, 1, 1));
        using var _ = harness;

        harness.Scroll(6, 4, 2);
        Assert.Equal(4, menu.Selected);
        harness.Scroll(6, 4, 5);
        Assert.Equal(4, menu.Selected); // the wheel doesn't wrap
        harness.Scroll(6, 4, -1);
        Assert.Equal(1, menu.Selected);
    }

    [Fact]
    public async Task APressOutside_ClosesTheMenu_AndIsntRoutedToTheScreen()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new Blank(log), 40, 20);
        var menu = new PopupMenu(Fruit, new Rect(2, 1, 1, 1));
        harness.App.Overlays.Show(menu);
        harness.RunUntilIdle();

        harness.Mouse(new MouseEvent(MouseAction.Press, MouseButton.Right, 30, 15));

        Assert.Null(await menu.Result);
        Assert.False(harness.App.Overlays.Any);
        Assert.Empty(log);
    }

    [Fact]
    public void AReleaseOrTheWheelOutside_DoesntClose()
    {
        var (harness, menu) = Open(Fruit, new Rect(2, 1, 1, 1));
        using var _ = harness;

        harness.Mouse(new MouseEvent(MouseAction.Release, MouseButton.Right, 2, 1));
        harness.Scroll(30, 15, 1);

        Assert.True(harness.App.Overlays.Any);
        Assert.False(menu.Result.IsCompleted);
    }

    private sealed class Plain(bool framed = true) : Overlay
    {
        public Rect Drawn;
        public override bool Framed => framed;
        public override Rect Placement(Rect screen) => new(10, 5, 10, 4);
        public override void Render(Buffer buf, Rect area, RenderContext ctx) => Drawn = area;
    }

    [Fact]
    public void AnOverlayWithoutCloseOnClickOutside_KeepsThe1_0Behavior()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new Blank(log), 40, 20);
        harness.App.Overlays.Show(new Plain());
        harness.RunUntilIdle();

        harness.Click(1, 1);

        Assert.True(harness.App.Overlays.Any);
        Assert.Empty(log);
    }

    [Fact]
    public void AnUnframedOverlay_GetsTheWholeRect_AndNoBlock()
    {
        using var harness = new ComponentHarness(new Blank(), 40, 20);
        var framed = new Plain();
        harness.App.Overlays.Show(framed);
        harness.RunUntilIdle();
        Assert.Equal(new Rect(12, 6, 6, 2), framed.Drawn); // inside the border and padding
        Assert.Equal('┌', harness.Lines[5][10]);
        harness.App.Overlays.Close(framed);

        var unframed = new Plain(framed: false);
        harness.App.Overlays.Show(unframed);
        harness.RunUntilIdle();

        Assert.Equal(new Rect(10, 5, 10, 4), unframed.Drawn);
        Assert.Equal(' ', harness.Lines[5][10]);
    }

    // ShowMenuAsync

    [Fact]
    public async Task ShowMenuAsync_AnswersTheChoice()
    {
        using var harness = new ComponentHarness(new Blank(), 40, 20);
        var answer = harness.App.Overlays.ShowMenuAsync(Fruit, new Rect(0, 0, 1, 1), selected: 1);
        harness.RunUntilIdle();

        harness.Press("enter");

        Assert.Equal(1, await answer);
    }

    [Fact]
    public async Task ShowMenuAsync_WithNoItems_ShowsNothing()
    {
        using var harness = new ComponentHarness(new Blank(), 40, 20);

        var answer = harness.App.Overlays.ShowMenuAsync([], new Rect(0, 0, 1, 1));

        Assert.True(answer.IsCompleted);
        Assert.Null(await answer);
        Assert.False(harness.App.Overlays.Any);
    }

    [Fact]
    public void NullItems_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new PopupMenu([new("a"), null!], default));
    }

    // Select

    private sealed class Form : Screen
    {
        public Form(SelectState state)
        {
            Field = AddChild(new SelectField(state));
            Other = AddChild(new SelectField(new SelectState(["x", "y"])));
            Focus.Add(Field).Add(Other);
        }

        public SelectField Field { get; }
        public SelectField Other { get; }

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            ctx.Render(Field, buf, new Rect(2, 1, 20, 3));
            ctx.Render(Other, buf, new Rect(2, 10, 20, 3));
        }
    }

    private sealed class SelectField(SelectState state) : Component
    {
        public SelectState State => state;
        public override bool HandleKey(ConsoleKeyInfo key) => state.HandleKey(key, App);
        public override bool HandleMouse(MouseEvent mouse) => state.HandleMouse(mouse, App);

        public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
            Widget.Select().State(state).Placeholder("Pick one").Block(Widget.Block().Title(" Plan ")).Focused(ctx.Focused).Draw(buf, area);
    }

    private static (ComponentHarness Harness, SelectState State, List<int> Changes) OpenForm(int? selected = null)
    {
        var state = new SelectState(["Free", "Pro", "Team"], selected);
        var changes = new List<int>();
        state.Changed += (_, _) => changes.Add(state.Selected ?? -1);
        var harness = new ComponentHarness(new Form(state), 40, 20);
        harness.RunUntilIdle();
        return (harness, state, changes);
    }

    [Theory]
    [InlineData("enter")]
    [InlineData("space")]
    [InlineData("up")]
    [InlineData("down")]
    [InlineData("alt+down")]
    [InlineData("f4")]
    public void TheOpeningKeys_OpenTheMenu_UnderTheField_AsWideAsIt(string key)
    {
        var (harness, state, _) = OpenForm();
        using var _ = harness;

        harness.Press(key);

        Assert.True(state.IsOpen);
        var menu = Assert.IsType<PopupMenu>(harness.App.Overlays.Top);
        Assert.Equal(new Rect(2, 4, 20, 5), menu.Area);
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("x")]
    [InlineData("ctrl+down")]
    public void OtherKeys_DontOpenIt(string key)
    {
        var (harness, state, _) = OpenForm();
        using var _ = harness;

        harness.Press(key);

        Assert.False(state.IsOpen);
        Assert.False(harness.App.Overlays.Any);
    }

    [Fact]
    public void Choosing_SetsSelected_AndRaisesChangedOnce()
    {
        var (harness, state, changes) = OpenForm();
        using var _ = harness;

        harness.Press("enter down enter");

        Assert.Equal(1, state.Selected);
        Assert.Equal("Pro", state.Value);
        Assert.Equal([1], changes);
        Assert.False(state.IsOpen);
        Assert.Equal("  │Pro              ▼│", harness.Lines[2].TrimEnd());
    }

    [Fact]
    public void TheMenu_StartsOnTheCurrentChoice()
    {
        var (harness, _, _) = OpenForm(selected: 2);
        using var h = harness;

        harness.Press("enter");

        Assert.Equal(2, Assert.IsType<PopupMenu>(harness.App.Overlays.Top).Selected);
    }

    [Fact]
    public void ChoosingTheSameOption_OrDismissing_DoesntRaiseChanged()
    {
        var (harness, state, changes) = OpenForm(selected: 1);
        using var _ = harness;

        harness.Press("enter enter");
        harness.Press("enter down esc");

        Assert.Equal(1, state.Selected);
        Assert.Empty(changes);
    }

    [Fact]
    public void SettingSelected_FromCode_DoesntRaiseChanged()
    {
        var (harness, state, changes) = OpenForm();
        using var _ = harness;

        state.Selected = 2;

        Assert.Empty(changes);
    }

    [Fact]
    public void AClick_FocusesAndOpens_AndAClickOnAnOptionChoosesIt()
    {
        var (harness, state, changes) = OpenForm();
        using var _ = harness;
        var other = ((Form)harness.App.Screens.Top!).Other;

        harness.Click(5, 11);
        Assert.True(other.State.IsOpen);
        harness.Press("esc");

        harness.Click(5, 2);
        Assert.True(state.IsOpen);
        harness.Click(5, RowOf(harness, "Team"));

        Assert.Equal(2, state.Selected);
        Assert.Equal([2], changes);
    }

    [Fact]
    public void TheWheel_OverAClosedField_DoesNothing()
    {
        var (harness, state, _) = OpenForm(selected: 0);
        using var _ = harness;

        harness.Scroll(5, 2, 3);

        Assert.False(state.IsOpen);
        Assert.Equal(0, state.Selected);
    }

    [Fact]
    public void WithNoOptions_NothingOpens()
    {
        using var harness = new ComponentHarness(new Form(new SelectState()), 40, 20);
        harness.RunUntilIdle();

        harness.Press("enter");

        Assert.False(harness.App.Overlays.Any);
    }

    [Fact]
    public void SelectState_ClampsAndClears()
    {
        var state = new SelectState(["a", "b", "c"], 9);
        Assert.Equal(2, state.Selected);
        state.Selected = -4;
        Assert.Equal(0, state.Selected);

        state.Selected = 2;
        state.Options = ["x", "y", "z", "w"];
        Assert.Equal(2, state.Selected);
        state.Options = ["x"];
        Assert.Null(state.Selected);
        Assert.Null(state.Value);

        Assert.Null(new SelectState(selected: 1).Selected);
    }

    [Fact]
    public void TheField_DrawsThePlaceholderOrTheValue_AndTheArrow()
    {
        var state = new SelectState(["A very long option name"]);
        var buf = new Buffer(12, 1);

        Widget.Select().State(state).Placeholder("Pick one").Draw(buf, buf.Area);
        Assert.Equal("Pick one   ▼", buf.ToLines()[0]);
        Assert.Equal(Theme.Default.TextMuted.Fg, buf.Get(0, 0).Fg);

        state.Selected = 0;
        Widget.Select().State(state).Focused().Draw(buf, buf.Area);
        Assert.Equal("A very lo… ▼", buf.ToLines()[0]);
    }
}
