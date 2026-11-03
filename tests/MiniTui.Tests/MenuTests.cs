
namespace MiniTui.Tests;

/// <summary>Mnemonics, marks, submenus and Run in PopupMenu (1.x spec §15.2–15.4).</summary>
public class MenuTests
{
    private sealed class Blank : Screen
    {
        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) { }
    }

    private static (ComponentHarness Harness, PopupMenu Menu) Open(IReadOnlyList<MenuItem> items, Rect? anchor = null, int width = 60, int height = 20)
    {
        var harness = new ComponentHarness(new Blank(), width, height);
        var menu = new PopupMenu(items, anchor ?? new Rect(2, 0, 1, 1));
        harness.App.Overlays.Show(menu);
        harness.RunUntilIdle();
        return (harness, menu);
    }

    private static int RowOf(ComponentHarness harness, string text) =>
        Array.FindIndex(harness.Lines, l => l.Contains(text, StringComparison.Ordinal));

    private static int Depth(ComponentHarness harness) => harness.App.Overlays.All.Count;

    // Mnemonics

    [Theory]
    [InlineData("&File", "File", 0, 'f')]
    [InlineData("Save &As", "Save As", 5, 'a')]
    [InlineData("Fish && Chips", "Fish & Chips", -1, null)]
    [InlineData("Save & Exit", "Save & Exit", -1, null)]
    [InlineData("Trailing&", "Trailing&", -1, null)]
    [InlineData("&One &Two", "One Two", 0, 'o')]
    [InlineData("&Énergie", "Énergie", 0, 'é')]
    [InlineData("Plain", "Plain", -1, null)]
    public void Mnemonics_AreParsedFromTheLabel(string label, string text, int index, char? mnemonic)
    {
        var item = new MenuItem(label);

        Assert.Equal((text, index), item.Display);
        Assert.Equal(mnemonic, item.Mnemonic);
        Assert.Equal(label, item.Label);
    }

    [Fact]
    public void TheMnemonic_IsDrawnUnderlined_WithoutTheAmpersand()
    {
        var (harness, _) = Open([new("&Open"), new("Save &As")]);
        using var _h = harness;

        var row = RowOf(harness, "Open");
        Assert.DoesNotContain("&", harness.Lines[row], StringComparison.Ordinal);
        var x = harness.Lines[row].IndexOf("Open", StringComparison.Ordinal);
        Assert.True(harness.CellAt(x, row).Modifiers.HasFlag(Modifiers.Underlined));
        Assert.False(harness.CellAt(x + 1, row).Modifiers.HasFlag(Modifiers.Underlined));

        var asRow = RowOf(harness, "Save As");
        var a = harness.Lines[asRow].IndexOf("As", StringComparison.Ordinal);
        Assert.True(harness.CellAt(a, asRow).Modifiers.HasFlag(Modifiers.Underlined));
    }

    [Fact]
    public async Task AMnemonic_ChoosesTheItem_AndRunsIt_AfterTheMenuClosed()
    {
        var ran = new List<string>();
        var harness = new ComponentHarness(new Blank(), 60, 20);
        using var _h = harness;
        PopupMenu? menu = null;
        menu = new PopupMenu([new("&Open") { Run = () => ran.Add("open") }, new("&Save") { Run = () => ran.Add($"save {harness.App.Overlays.Any}") }], new Rect(2, 0, 1, 1));
        harness.App.Overlays.Show(menu);
        harness.RunUntilIdle();

        harness.Type("s");

        Assert.Equal(["save False"], ran);
        Assert.Equal(1, await menu.Result);
        Assert.Equal("&Save", (await menu.Chosen)!.Label);
    }

    [Fact]
    public void AltMnemonic_ChoosesToo_AndOtherAltLettersDoNothing()
    {
        var ran = 0;
        var (harness, menu) = Open([new("&Open") { Run = () => ran++ }, new("Banana")]);
        using var _h = harness;

        harness.Press("alt+b");
        Assert.Equal(0, menu.Selected);
        harness.Press("alt+o");
        Assert.Equal(1, ran);
    }

    [Fact]
    public void SharedMnemonics_CycleWithoutChoosing()
    {
        var (harness, menu) = Open([new("&Save"), new("&Search"), new("O&pen"), new("&Settings") { Enabled = false }]);
        using var _h = harness;

        harness.Type("s");
        Assert.Equal(1, menu.Selected);
        harness.Type("s");
        Assert.Equal(0, menu.Selected);
        Assert.True(harness.App.Overlays.Any);
    }

    [Fact]
    public void ACharacterThatIsNoMnemonic_FallsBackToTypeAhead()
    {
        var (harness, menu) = Open([new("&Apple"), new("Banana"), new("Cherry")]);
        using var _h = harness;

        harness.Type("c");

        Assert.Equal(2, menu.Selected);
        Assert.True(harness.App.Overlays.Any);
    }

    [Fact]
    public void ADisabledItemsMnemonic_DoesNothing()
    {
        var (harness, menu) = Open([new("Apple"), new("&Banana") { Enabled = false }]);
        using var _h = harness;

        harness.Type("b");

        Assert.True(harness.App.Overlays.Any);
        Assert.Equal(0, menu.Selected);
    }

    // Marks

    [Fact]
    public void Marks_AreDrawnInTheirOwnColumn_OnlyWhenSomeItemHasOne()
    {
        var (harness, _) = Open([new("Wrap") { Checked = true }, new("Numbers") { Checked = false }, new("Dark") { Checked = true, Radio = true },
            new("Light") { Checked = false, Radio = true }, new("Plain")]);
        using var _h = harness;

        var lines = harness.Lines.Select(l => l.TrimEnd()).ToArray();
        Assert.Equal("  │ ✓ Wrap    │", lines[RowOf(harness, "Wrap")]);
        Assert.Equal("  │   Numbers │", lines[RowOf(harness, "Numbers")]);
        Assert.Equal("  │ ● Dark    │", lines[RowOf(harness, "Dark")]);
        Assert.Equal("  │ ○ Light   │", lines[RowOf(harness, "Light")]);
        Assert.Equal("  │   Plain   │", lines[RowOf(harness, "Plain")]);

        var (plain, _) = Open([new("Plain")]);
        using var _p = plain;
        Assert.Equal("  │ Plain │", plain.Lines[RowOf(plain, "Plain")].TrimEnd());
    }

    [Fact]
    public void Marks_HaveBasicForms()
    {
        using var _ = Glyphs.Use(basic: true);

        Assert.Equal("√", PopupMenu.Mark(new("A") { Checked = true }));
        Assert.Equal("*", PopupMenu.Mark(new("B") { Checked = true, Radio = true }));
        Assert.Equal("o", PopupMenu.Mark(new("C") { Checked = false, Radio = true }));
        Assert.Null(PopupMenu.Mark(new("D") { Checked = false }));
        Assert.Equal("►", PopupMenu.RightText(new("E") { Submenu = [new("x")] }));
    }

    // Submenus

    private static readonly MenuItem[] Edit =
    [
        new("&Undo") { Hint = "Ctrl+Z" },
        new("&Sort") { Hint = "ignored", Submenu = [new("By &name") { Checked = true, Radio = true }, new("By &size") { Checked = false, Radio = true }, new("&More") { Submenu = [new("&Deep")] }] },
        new("&Find") { Submenu = [new("Next")], Enabled = false },
    ];

    [Fact]
    public void ASubmenuItem_ShowsAnArrow_InPlaceOfItsHint()
    {
        var (harness, _) = Open(Edit);
        using var _h = harness;

        var row = harness.Lines[RowOf(harness, "Sort")];
        Assert.Contains("▸", row, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", row, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Z", harness.Lines[RowOf(harness, "Undo")], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("right")]
    [InlineData("enter")]
    [InlineData("s")]
    public void RightEnterOrTheMnemonic_OpenTheSubmenu_BesideItsRow(string key)
    {
        var (harness, menu) = Open(Edit);
        using var _h = harness;
        if (key == "s") harness.Type("s");
        else harness.Press(key == "enter" ? "down enter" : "down right");

        var sub = Assert.IsType<PopupMenu>(harness.App.Overlays.Top);
        Assert.Same(sub, menu.Submenu);
        Assert.Equal(menu.Area.Right, sub.Area.X);
        Assert.Equal(RowOf(harness, "Sort"), sub.Area.Y + 1);
        Assert.Equal(0, sub.Selected);
    }

    [Fact]
    public void ASubmenu_OpensToTheLeft_WhenTheRightHasNoRoom()
    {
        var (harness, menu) = Open(Edit, new Rect(40, 0, 1, 1), width: 50);
        using var _h = harness;

        harness.Press("down right");

        var sub = Assert.IsType<PopupMenu>(harness.App.Overlays.Top);
        Assert.Equal(menu.Area.X, sub.Area.Right);
    }

    [Fact]
    public async Task ChoosingInASubmenu_ClosesEveryMenu_ThenRuns()
    {
        var ran = new List<string>();
        var harness = new ComponentHarness(new Blank(), 60, 20);
        using var _h = harness;
        var menu = new PopupMenu(
        [
            new("&File"),
            new("&Sort") { Submenu = [new("By &name"), new("By &size") { Run = () => ran.Add($"size, {harness.App.Overlays.All.Count} open") }] },
        ], new Rect(2, 0, 1, 1));
        harness.App.Overlays.Show(menu);
        harness.RunUntilIdle();

        harness.Type("ss");

        Assert.Equal(["size, 0 open"], ran);
        Assert.Equal(1, await menu.Result);
        Assert.Equal("By &size", (await menu.Chosen)!.Label);
    }

    [Fact]
    public async Task TwoLevelsDeep_ResultIsTheTopLevelIndex()
    {
        var (harness, menu) = Open(Edit);
        using var _h = harness;

        harness.Type("smd");

        Assert.False(harness.App.Overlays.Any);
        Assert.Equal(1, (await menu.Result));
        Assert.Equal("&Deep", (await menu.Chosen)!.Label);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("esc")]
    public void LeftOrEsc_InASubmenu_GoBackToTheParent(string key)
    {
        var (harness, menu) = Open(Edit);
        using var _h = harness;
        harness.Press("down right");

        harness.Press(key);

        Assert.Same(menu, harness.App.Overlays.Top);
        Assert.Null(menu.Submenu);
        Assert.Equal(1, menu.Selected);
        Assert.False(menu.Result.IsCompleted);
    }

    [Fact]
    public async Task LeftInATopLevelMenu_DoesNothing_AndTabDismissesEveryMenu()
    {
        var (harness, menu) = Open(Edit);
        using var _h = harness;

        harness.Press("left");
        Assert.Same(menu, harness.App.Overlays.Top);

        harness.Press("down right down down right");
        Assert.Equal(3, Depth(harness));
        harness.Press("tab");
        Assert.False(harness.App.Overlays.Any);
        Assert.Null((await menu.Result));
        Assert.Null((await menu.Chosen));
    }

    [Fact]
    public void ADisabledSubmenuItem_DoesntOpen()
    {
        var (harness, menu) = Open(Edit);
        using var _h = harness;

        harness.Type("f");
        harness.Press("right");

        Assert.Same(menu, harness.App.Overlays.Top);
        Assert.Equal(1, Depth(harness));
    }

    // The mouse

    [Fact]
    public void AClickOnASubmenuItem_OpensIt_AndOnTheParent_OnlyClosesTheSubmenu()
    {
        var (harness, menu) = Open(Edit);
        using var _h = harness;

        harness.Click(4, RowOf(harness, "Sort"));
        Assert.Equal(2, Depth(harness));

        harness.Click(4, RowOf(harness, "Undo"));
        Assert.Same(menu, harness.App.Overlays.Top);
        Assert.Null(menu.Submenu);
    }

    [Fact]
    public async Task AClickInASubmenu_Chooses_AndAClickOutsideEveryMenu_ClosesThemAll()
    {
        var (harness, menu) = Open(Edit);
        using var _h = harness;
        harness.Click(4, RowOf(harness, "Sort"));
        var sub = Assert.IsType<PopupMenu>(harness.App.Overlays.Top);

        harness.Click(sub.Area.X + 3, RowOf(harness, "By size"));
        Assert.False(harness.App.Overlays.Any);
        Assert.Equal("By &size", (await menu.Chosen)!.Label);

        var (again, other) = Open(Edit);
        using var _a = again;
        again.Press("down right");
        again.Click(50, 15);
        Assert.False(again.App.Overlays.Any);
        Assert.Null((await other.Result));
    }

    [Fact]
    public void Width_CountsTheMarksAndTheArrow()
    {
        var (harness, menu) = Open([new("Ab") { Checked = true }, new("Cd") { Submenu = [new("x")] }]);
        using var _h = harness;

        // │ ✓ Ab  ▸ │: border, space, mark and its space, label, two spaces, arrow, space, border
        Assert.Equal(11, menu.Area.Width);
    }
}
