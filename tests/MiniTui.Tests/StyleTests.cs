namespace MiniTui.Tests;

public class StyleTests
{
    private static readonly Cell Base = new("x", Color.Red, Color.Blue, Modifiers.Bold | Modifiers.Italic);

    [Fact]
    public void Default_ChangesNothing()
    {
        Assert.Equal(Base, Style.Default.ApplyTo(Base));
    }

    [Fact]
    public void Apply_PatchesOnlySpecifiedAttributes()
    {
        var cell = new Style(Fg: Color.Green, Remove: Modifiers.Bold).Underlined().ApplyTo(Base);

        Assert.Equal(new Cell("x", Color.Green, Color.Blue, Modifiers.Italic | Modifiers.Underlined), cell);
    }

    [Fact]
    public void Patch_OtherColorsWin_AndNullsKeepOriginal()
    {
        var patched = new Style(Color.Red, Color.Blue).Patch(new Style(Bg: Color.Green));

        Assert.Equal(new Style(Color.Red, Color.Green), patched);
    }

    [Fact]
    public void Patch_CombinesModifiers_LaterWins()
    {
        var a = Style.Default.Bold().Italic() with { Remove = Modifiers.Dim };
        var b = new Style(Add: Modifiers.Dim, Remove: Modifiers.Bold);

        var patched = a.Patch(b);

        Assert.Equal(Modifiers.Italic | Modifiers.Dim, patched.Add);
        Assert.Equal(Modifiers.Bold, patched.Remove);
    }

    [Fact]
    public void Patch_ThenApply_EqualsApplyingInOrder()
    {
        var a = new Style(Color.Red).Bold();
        var b = new Style(Bg: Color.Cyan, Remove: Modifiers.Bold).Reversed();

        Assert.Equal(b.ApplyTo(a.ApplyTo(Base)), a.Patch(b).ApplyTo(Base));
    }

    [Fact]
    public void ModifierHelpers_AddAndCancelRemove()
    {
        var style = new Style(Remove: Modifiers.Bold).Bold().Dim().Italic().Underlined().Reversed().CrossedOut();

        Assert.Equal(Modifiers.Bold | Modifiers.Dim | Modifiers.Italic | Modifiers.Underlined
                     | Modifiers.Reversed | Modifiers.CrossedOut, style.Add);
        Assert.Equal(Modifiers.None, style.Remove);
    }

    [Fact]
    public void WithFgAndWithBg_SetColors()
    {
        Assert.Equal(new Style(Color.Red, Color.Blue), Style.Default.WithFg(Color.Red).WithBg(Color.Blue));
    }
}
