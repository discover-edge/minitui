using static MiniTui.Tests.Snapshot;
using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

// Conhost draws with one font and no fallback, so widgets must avoid glyphs Consolas/Lucida Console lack.
public class GlyphTests
{
    // WGL4 block and box characters that conhost's default fonts have
    private const string Wgl4 = " ─│┌┐└┘├┤┬┴┼═║╔╗╚╝█▌▄▀░▒▓▲▼►◄…✓";

    [Theory]
    [InlineData(true, null, null, false, true)]          // conhost
    [InlineData(true, "abc", null, false, false)]        // Windows Terminal
    [InlineData(true, null, "vscode", false, false)]     // VS Code terminal
    [InlineData(false, null, null, false, false)]        // Linux/macOS
    [InlineData(false, null, null, true, true)]          // legacy console backend
    public void Detect_UsesBasicGlyphsOnConhost(bool windows, string? wtSession, string? termProgram, bool legacy, bool basic)
    {
        var env = new Dictionary<string, string?> { ["WT_SESSION"] = wtSession, ["TERM_PROGRAM"] = termProgram };

        Assert.Equal(basic, Glyphs.DetectBasic(legacy, windows, name => env.GetValueOrDefault(name)));
    }

    [Fact]
    public void Gauge_Basic_UsesOnlyHalfBlocks()
    {
        var buf = new Buffer(10, 1);

        using (Glyphs.Use(basic: true)) Widget.Gauge().Ratio(0.47).Label("").Draw(buf, buf.Area);

        // 4.7 cells: the 5/8 block becomes the nearest basic glyph, ▌
        AssertLines(buf, "████▌░░░░░");
    }

    [Fact]
    public void Sparkline_Basic_UsesHalfAndFullBlocks()
    {
        var buf = new Buffer(4, 1);

        using (Glyphs.Use(basic: true)) Widget.Sparkline().Data([0, 1, 4, 8]).Draw(buf, buf.Area);

        AssertLines(buf, "  ▄█");
    }

    [Fact]
    public void Spinner_Basic_FallsBackFromBraille()
    {
        var buf = new Buffer(4, 1);

        using (Glyphs.Use(basic: true)) Widget.Spinner().Frames(SpinnerFrames.Braille).Draw(buf, buf.Area);

        Assert.Contains(buf.ToLines()[0][0], "-\\|/");
    }

    [Fact]
    public void Block_Basic_DrawsRoundedAsSingle()
    {
        var buf = new Buffer(3, 2);

        using (Glyphs.Use(basic: true)) Widget.Block().BorderType(BoxStyle.Rounded).Draw(buf, buf.Area);

        AssertLines(buf, """
            ┌─┐
            └─┘
            """);
    }

    [Fact]
    public void EveryWidget_InBasicMode_DrawsOnlyWgl4Glyphs()
    {
        var buf = new Buffer(40, 12);
        var toasts = new ToastQueue();
        toasts.Push("hi");

        using (Glyphs.Use(basic: true))
        {
            Widget.Gauge().Ratio(0.37).Block(Widget.Block().BorderType(BoxStyle.Rounded)).Draw(buf, new Rect(0, 0, 20, 3));
            Widget.Sparkline().Data([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]).Draw(buf, new Rect(0, 3, 20, 3));
            Widget.Spinner().Draw(buf, new Rect(0, 6, 5, 1));
            Widget.List().Items(Enumerable.Repeat("x", 30).ToList()).Draw(buf, new Rect(20, 0, 20, 6));
            Widget.Toasts().Queue(toasts).Draw(buf, buf.Area);
        }

        foreach (var line in buf.ToLines())
            foreach (var c in line)
                Assert.True(char.IsAsciiLetterOrDigit(c) || char.IsAsciiLetter(c) || Wgl4.Contains(c) || c is '%' or '-' or '\\' or '|' or '/' or '.',
                    $"'{c}' (U+{(int)c:X4}) isn't in conhost's fonts");
    }
}
