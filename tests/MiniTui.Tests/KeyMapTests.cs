namespace MiniTui.Tests;

public class KeyMapTests
{
    private static ConsoleKeyInfo Char(char c, bool shift = false) =>
        new(c, char.IsAsciiLetter(c) ? ConsoleKey.A + (char.ToLowerInvariant(c) - 'a') : 0, shift, false, false);

    private static ConsoleKeyInfo Ctrl(char letter) =>
        new((char)(letter - 'a' + 1), ConsoleKey.A + (letter - 'a'), false, false, true);

    [Theory]
    [InlineData("r", (ConsoleKey)0, 'r', (ConsoleModifiers)0)]
    [InlineData("R", (ConsoleKey)0, 'R', (ConsoleModifiers)0)]
    [InlineData("shift+r", (ConsoleKey)0, 'R', (ConsoleModifiers)0)]
    [InlineData("?", (ConsoleKey)0, '?', (ConsoleModifiers)0)]
    [InlineData("+", (ConsoleKey)0, '+', (ConsoleModifiers)0)]
    [InlineData("Ctrl+R", ConsoleKey.R, 'r', ConsoleModifiers.Control)]
    [InlineData("alt+1", ConsoleKey.D1, '1', ConsoleModifiers.Alt)]
    [InlineData("shift+tab", ConsoleKey.Tab, null, ConsoleModifiers.Shift)]
    [InlineData("PgDn", ConsoleKey.PageDown, null, (ConsoleModifiers)0)]
    [InlineData("ctrl+alt+f12", ConsoleKey.F12, null, ConsoleModifiers.Control | ConsoleModifiers.Alt)]
    [InlineData(" space ", ConsoleKey.Spacebar, null, (ConsoleModifiers)0)]
    public void Parse_FollowsTheGrammar(string text, ConsoleKey key, char? c, ConsoleModifiers modifiers)
    {
        Assert.Equal(new KeyBinding(key, c, modifiers), KeyBinding.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ctrl+")]
    [InlineData("hyper+x")]
    [InlineData("ctrl+ctrl+x")]
    [InlineData("f13")]
    [InlineData("xy")]
    [InlineData("\t")]
    public void Parse_RejectsInvalidInput(string text)
    {
        Assert.Throws<FormatException>(() => KeyBinding.Parse(text));
    }

    [Fact]
    public void CharacterBindings_MatchWhatWasTyped()
    {
        Assert.True(KeyBinding.Parse("R").Matches(Char('R', shift: true)));
        Assert.True(KeyBinding.Parse("shift+r").Matches(Char('R', shift: true)));
        Assert.False(KeyBinding.Parse("r").Matches(Char('R', shift: true)));
        Assert.False(KeyBinding.Parse("r").Matches(new ConsoleKeyInfo('r', ConsoleKey.R, false, alt: true, false)));
        Assert.True(KeyBinding.Parse("@").Matches(new ConsoleKeyInfo('@', ConsoleKey.Q, false, alt: true, control: true)));
    }

    [Fact]
    public void CtrlBindings_MatchByKeyOrControlCharacter()
    {
        var ctrlC = KeyBinding.Parse("ctrl+c");

        Assert.True(ctrlC.Matches(Ctrl('c')));
        Assert.True(ctrlC.Matches(new ConsoleKeyInfo('\x03', 0, false, false, false)));
        Assert.False(ctrlC.Matches(Char('c')));
        Assert.False(KeyBinding.Parse("tab").Matches(new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: true, false, false)));
    }

    [Fact]
    public void Handle_RunsTheFirstMatch()
    {
        var log = new List<string>();
        var keys = new KeyMap()
            .Add("j,down", "Down", () => log.Add("down"))
            .Add("j", "Shadowed", () => log.Add("shadowed"));

        Assert.True(keys.Handle(Char('j')));
        Assert.True(keys.Handle(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false)));
        Assert.False(keys.Handle(Char('x')));
        Assert.Equal(["down", "down"], log);
    }

    [Fact]
    public void Hints_ListWholeHintsThatFit()
    {
        var keys = new KeyMap()
            .Add("r", "Refresh", () => { })
            .Add("?", "Help", () => { }, showInHints: false)
            .Add("q,ctrl+c", "Quit", () => { });

        Assert.Equal("r Refresh  q Quit", keys.Hints(100));
        Assert.Equal("r Refresh", keys.Hints(12));
        Assert.Equal("r R…", keys.Hints(4));
        Assert.Equal("", keys.Hints(0));
    }

    [Fact]
    public void HelpLines_AreGroupedAndAligned()
    {
        var keys = new KeyMap()
            .Add("q,ctrl+c", "Quit", () => { })
            .Add("j,down", "Down", () => { }, group: "Navigation")
            .Add("pgdn", "Page down", () => { }, group: "Navigation");

        Assert.Equal(
            [
                "  q, ctrl+c  Quit",
                "",
                "Navigation",
                "  j, down    Down",
                "  pgdn       Page down",
            ],
            keys.HelpLines());
    }
}
