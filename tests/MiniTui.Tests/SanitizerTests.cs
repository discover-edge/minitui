using Buffer = MiniTui.Buffer;
using static MiniTui.Tests.Snapshot;

namespace MiniTui.Tests;

public class SanitizerTests
{
    [Fact]
    public void Acceptance_EscapeSequence_IsDrawnAsReplacementCharacterAndText()
    {
        var buf = new Buffer(6, 1);

        buf.SetString(0, 0, "\x1b[31mX", Style.Default);

        Assert.Equal(["�", "[", "3", "1", "m", "X"], Enumerable.Range(0, 6).Select(x => buf.Get(x, 0).Symbol));
    }

    [Fact]
    public void AllControlCharacters_AreReplaced()
    {
        for (var c = 0; c <= 0x9F; c++)
        {
            if (c is >= 0x20 and < 0x7F || c is '\t' or '\n' or '\r') continue;
            Assert.Equal(Sanitizer.Replacement, Sanitizer.SingleLine([(char)c]));
        }
    }

    [Fact]
    public void SingleLine_LoneSurrogates_AreReplaced()
    {
        Assert.Equal("a�b�", Sanitizer.SingleLine("a\uD83Db\uDC4D"));
        Assert.Equal("👍", Sanitizer.SingleLine("👍"));
    }

    [Fact]
    public void SingleLine_CleanText_IsReturnedUnchanged()
    {
        Assert.Equal("日本 é", Sanitizer.SingleLine("日本 é"));
    }

    [Fact]
    public void LoneCarriageReturn_IsReplaced()
    {
        Assert.Equal("a�b", Sanitizer.SingleLine("a\rb"));
    }

    [Fact]
    public void Set_SanitizesSymbol_AndKeepsOnlyTheFirstGrapheme()
    {
        var buf = new Buffer(3, 1);

        buf.Set(0, 0, Cell.Empty with { Symbol = "\x1b[" });
        buf.Set(1, 0, Cell.Empty with { Symbol = "ab" });
        buf.Set(2, 0, Cell.Empty with { Symbol = "" });

        AssertLines(buf, "�a ");
    }

    [Fact]
    public void LineAndSpan_AreSanitizedWhenDrawn()
    {
        var buf = new Buffer(4, 1);

        buf.SetLine(0, 0, new Line([new Span("\x07", default), "\u001bA"]), 4);

        AssertLines(buf, "��A ");
    }
}
