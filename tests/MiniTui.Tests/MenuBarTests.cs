using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>MenuBar, App.WithMenuBar and the palette's menu items (1.x spec §15.5, §15.6).</summary>
public class MenuBarTests
{
    private sealed class Home : Screen
    {
        public Rect Drawn { get; private set; }
        public List<string> Log { get; } = [];

        public Home(bool bindAltE = false)
        {
            if (bindAltE) Keys.Add("alt+e", "Mine", () => Log.Add("screen alt+e"));
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) => Drawn = area;
    }

    private static (ComponentHarness Harness, MenuBar Bar, Home Home, List<string> Log) Start(bool bindAltE = false, Func<IReadOnlyList<MenuItem>>? view = null)
    {
        var log = new List<string>();
        var bar = new MenuBar(
            new Menu("&File", [new("&New") { Hint = "Ctrl+N", Run = () => log.Add("new") }, new("&Export") { Submenu = [new("As &CSV") { Run = () => log.Add("csv") }, new("As &JSON") { Run = () => log.Add("json") }] }, MenuItem.Separator, new("&Quit") { Run = () => log.Add("quit") }]),
            new Menu("&Edit", [new("&Undo") { Run = () => log.Add("undo") }, new("&Redo") { Enabled = false, Run = () => log.Add("redo") }]),
            new Menu("&View", view ?? (() => [new("&Wrap") { Checked = true, Run = () => log.Add("wrap") }])));
        var home = new Home(bindAltE);
        var harness = new ComponentHarness(home, 60, 20);
        harness.App.WithMenuBar(bar);
        harness.RunUntilIdle();
        return (harness, bar, home, log);
    }

    private static PopupMenu TopMenu(ComponentHarness harness) => Assert.IsType<PopupMenu>(harness.App.Overlays.Top);

    // Layout and drawing

    [Fact]
    public void TheBar_TakesTheTopRow_AndTheScreenGetsTheRest()
    {
        var (harness, _, home, _) = Start();
        using var _h = harness;

        Assert.StartsWith(" File  Edit  View ", harness.Lines[0], StringComparison.Ordinal);
        Assert.Equal(new Rect(0, 1, 60, 18), home.Drawn);
        Assert.True(harness.CellAt(1, 0).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.False(harness.CellAt(2, 0).Modifiers.HasFlag(Modifiers.Underlined));
    }

    [Fact]
    public void WithMenuBarNull_RemovesIt()
    {
        var (harness, _, home, _) = Start();
        using var _h = harness;

        harness.App.WithMenuBar(null);
        harness.RunUntilIdle();

        Assert.Equal(new Rect(0, 0, 60, 19), home.Drawn);
        Assert.Null(harness.App.MenuBar);
        harness.Press("f10");
        Assert.False(harness.App.Overlays.Any);
    }

    [Fact]
    public void Overlays_ArePlacedBelowTheBar()
    {
        var (harness, _, _, _) = Start();
        using var _h = harness;

        _ = harness.App.Overlays.ShowMenuAsync([new("x")], new Rect(0, 0, 1, 1));
        harness.RunUntilIdle();

        Assert.True(TopMenu(harness).Area.Y >= 1);
    }

    [Fact]
    public void TitlesThatDontFit_AreLeftOut()
    {
        var bar = new MenuBar(new Menu("&File", [new("x")]), new Menu("A very long title", [new("y")]));
        using var harness = new ComponentHarness(new Home(), 20, 10);
        harness.App.WithMenuBar(bar);
        harness.RunUntilIdle();

        Assert.Equal(" File", harness.Lines[0].TrimEnd());
        bar.Open(1);
        harness.RunUntilIdle();
        Assert.False(bar.IsOpen);
    }

    // Opening

    [Fact]
    public void F10_OpensTheFirstMenu_UnderItsTitle_Highlighted()
    {
        var (harness, bar, _, _) = Start();
        using var _h = harness;

        harness.Press("f10");

        Assert.True(bar.IsOpen);
        var menu = TopMenu(harness);
        Assert.Equal(new Rect(0, 1, menu.Area.Width, menu.Area.Height), menu.Area);
        Assert.Equal(Theme.Current.Selection.Bg ?? Color.Reset, harness.CellAt(1, 0).Bg);
    }

    [Fact]
    public void AltMnemonic_OpensThatMenu()
    {
        var (harness, bar, _, _) = Start();
        using var _h = harness;

        harness.Press("alt+e");

        Assert.Equal(1, bar.OpenIndex);
        Assert.Equal(6, TopMenu(harness).Area.X);
    }

    [Fact]
    public void TheAppsOwnAltBindings_Win()
    {
        var (harness, bar, home, _) = Start(bindAltE: true);
        using var _h = harness;

        harness.Press("alt+e");

        Assert.False(bar.IsOpen);
        Assert.Equal(["screen alt+e"], home.Log);
    }

    [Fact]
    public void WithAnotherOverlayOpen_TheBarDoesntOpen()
    {
        var (harness, bar, _, _) = Start();
        using var _h = harness;
        _ = harness.App.Overlays.ConfirmAsync("Sure?");
        harness.RunUntilIdle();

        bar.Open();
        harness.Press("alt+f");
        harness.Click(2, 0);

        Assert.False(bar.IsOpen);
        Assert.IsType<ConfirmDialog>(harness.App.Overlays.Top);
    }

    [Fact]
    public void F10_IsListedInHelp_ButNotInTheHints()
    {
        var (harness, _, _, _) = Start();
        using var _h = harness;

        Assert.DoesNotContain("f10", harness.Lines[^1], StringComparison.Ordinal);
        Assert.Contains(KeyRouter.HelpLines(harness.App), l => l.Contains("f10", StringComparison.Ordinal) && l.Contains("Menu", StringComparison.Ordinal));
    }

    // In an open menu

    [Fact]
    public void LeftAndRight_MoveBetweenMenus_Wrapping()
    {
        var (harness, bar, _, _) = Start();
        using var _h = harness;
        harness.Press("f10");

        harness.Press("right");
        Assert.Equal(1, bar.OpenIndex);
        harness.Press("right right");
        Assert.Equal(0, bar.OpenIndex);
        harness.Press("left");
        Assert.Equal(2, bar.OpenIndex);
        Assert.Single(harness.App.Overlays.All);
    }

    [Fact]
    public void InASubmenu_LeftClosesIt_AndRightOnALeafMovesToTheNextMenu()
    {
        var (harness, bar, _, _) = Start();
        using var _h = harness;
        harness.Press("f10");

        harness.Press("down right");
        Assert.Equal(2, harness.App.Overlays.All.Count);
        harness.Press("left");
        Assert.Single(harness.App.Overlays.All);
        Assert.Equal(0, bar.OpenIndex);

        harness.Press("right right");
        Assert.Equal(1, bar.OpenIndex);
        Assert.Single(harness.App.Overlays.All);
    }

    [Theory]
    [InlineData("f10")]
    [InlineData("esc")]
    public void F10OrEsc_CloseTheMenu(string key)
    {
        var (harness, bar, _, _) = Start();
        using var _h = harness;
        harness.Press("f10");

        harness.Press(key);

        Assert.False(bar.IsOpen);
        Assert.False(harness.App.Overlays.Any);
    }

    [Fact]
    public void AltAndAnotherTitle_SwitchesToIt_AndItemMnemonicsChoose()
    {
        var (harness, bar, _, log) = Start();
        using var _h = harness;
        harness.Press("f10");

        harness.Press("alt+v");
        Assert.Equal(2, bar.OpenIndex);
        harness.Press("alt+w");

        Assert.Equal(["wrap"], log);
        Assert.False(bar.IsOpen);
    }

    [Fact]
    public void ChoosingAnItem_RunsIt_AfterTheMenusClosed()
    {
        var (harness, bar, _, log) = Start();
        using var _h = harness;

        harness.Press("f10");
        harness.Type("ej");

        Assert.Equal(["json"], log);
        Assert.False(bar.IsOpen);
        Assert.False(harness.App.Overlays.Any);
    }

    [Fact]
    public void AFactory_RunsAtEachOpening()
    {
        var builds = 0;
        var (harness, _, _, _) = Start(view: () => { builds++; return [new("&Wrap") { Checked = builds % 2 == 1 }]; });
        using var _h = harness;

        harness.Press("alt+v");
        Assert.Contains("✓", string.Join("\n", harness.Lines), StringComparison.Ordinal);
        harness.Press("esc alt+v");

        Assert.Equal(2, builds);
        Assert.DoesNotContain("✓", string.Join("\n", harness.Lines), StringComparison.Ordinal);
    }

    // The mouse

    [Fact]
    public void AClickOnATitle_OpensSwitchesAndCloses()
    {
        var (harness, bar, _, _) = Start();
        using var _h = harness;

        harness.Click(2, 0);
        Assert.Equal(0, bar.OpenIndex);
        harness.Click(8, 0);
        Assert.Equal(1, bar.OpenIndex);
        harness.Click(8, 0);
        Assert.False(bar.IsOpen);

        harness.Click(2, 0);
        harness.Click(40, 0);
        Assert.False(bar.IsOpen);
    }

    [Fact]
    public void AClickOutside_ClosesTheMenu()
    {
        var (harness, bar, _, _) = Start();
        using var _h = harness;
        harness.Press("f10");

        harness.Click(40, 12);

        Assert.False(bar.IsOpen);
    }

    // The palette

    [Fact]
    public void ThePalette_ListsTheMenuItems_WithTheirPaths()
    {
        var (harness, _, _, _) = Start();
        using var _h = harness;

        var items = CommandPalette.ItemsFor(harness.App).Where(i => i.Detail?.StartsWith("File", StringComparison.Ordinal) == true || i.Detail is "Edit" or "View").ToList();

        Assert.Equal(["New", "As CSV", "As JSON", "Quit", "Undo", "Wrap"], items.Select(i => i.Label));
        Assert.Equal(["File", "File › Export", "File › Export", "File", "Edit", "View"], items.Select(i => i.Detail));
        Assert.Equal("Ctrl+N", items[0].Hint);
    }

    [Fact]
    public void NullMenus_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new MenuBar(new Menu("a", [new("x")]), null!));
    }
}
