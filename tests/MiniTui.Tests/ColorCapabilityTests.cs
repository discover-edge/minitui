namespace MiniTui.Tests;

public class ColorCapabilityTests
{
    private static ColorMode Detect(ColorMode? explicitMode = null, params (string Key, string Value)[] env)
    {
        var map = env.ToDictionary(e => e.Key, e => e.Value);
        return ColorCapability.Detect(key => map.GetValueOrDefault(key), explicitMode);
    }

    [Fact]
    public void ExplicitMode_Wins() =>
        Assert.Equal(ColorMode.Ansi256, Detect(ColorMode.Ansi256, ("NO_COLOR", "1"), ("COLORTERM", "truecolor")));

    [Fact]
    public void NoColor_DisablesColor() =>
        Assert.Equal(ColorMode.None, Detect(null, ("NO_COLOR", "1"), ("COLORTERM", "truecolor")));

    [Fact]
    public void EmptyNoColor_IsIgnored() =>
        Assert.Equal(ColorMode.TrueColor, Detect(null, ("NO_COLOR", ""), ("COLORTERM", "truecolor")));

    [Theory]
    [InlineData("truecolor")]
    [InlineData("24bit")]
    public void ColorTerm_GivesTrueColor(string value) =>
        Assert.Equal(ColorMode.TrueColor, Detect(null, ("COLORTERM", value), ("TERM", "xterm")));

    [Fact]
    public void WindowsTerminal_GivesTrueColor() =>
        Assert.Equal(ColorMode.TrueColor, Detect(null, ("WT_SESSION", "abc")));

    [Theory]
    [InlineData("iTerm.app")]
    [InlineData("WezTerm")]
    [InlineData("vscode")]
    public void TermProgram_GivesTrueColor(string value) =>
        Assert.Equal(ColorMode.TrueColor, Detect(null, ("TERM_PROGRAM", value), ("TERM", "xterm-256color")));

    [Fact]
    public void Term256Color_GivesAnsi256() =>
        Assert.Equal(ColorMode.Ansi256, Detect(null, ("TERM", "screen-256color"), ("TERM_PROGRAM", "Apple_Terminal")));

    [Fact]
    public void Otherwise_Ansi16() =>
        Assert.Equal(ColorMode.Ansi16, Detect(null, ("TERM", "xterm")));
}
