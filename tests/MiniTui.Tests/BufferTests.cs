using Buffer = MiniTui.Buffer;
using static MiniTui.Tests.Snapshot;

namespace MiniTui.Tests;

public class BufferTests
{
    private static readonly Cell Wide = Cell.Empty with { Symbol = "日" };

    // ===== B1: out-of-bounds writes are ignored, nothing throws =====

    public static TheoryData<Rect> Rects => new()
    {
        new Rect(0, 0, 0, 0), new Rect(-5, -5, 3, 3), new Rect(2, 2, -4, -4), new Rect(100, 100, 5, 5),
        new Rect(-10, 0, 100, 1), new Rect(int.MaxValue - 1, 0, 10, 10), new Rect(0, 0, int.MaxValue, 1),
    };

    [Theory]
    [MemberData(nameof(Rects))]
    public void B1_NoMethodThrows_ForAnyRect(Rect rect)
    {
        var buf = new Buffer(10, 5);

        buf.FillRect(rect, Wide);
        buf.SetStyle(rect, new Style(Color.Red));
        buf.DrawBox(rect);
        buf.DrawBoxWithTitle(rect, "Title 日本");
        buf.DrawProgressBar(rect, 0.5f);
        buf.DrawTextCentered(rect, "x");
        buf.SetString(rect.X, rect.Y, "日本語", Style.Default, rect.Width);
        buf.SetLine(rect.X, rect.Y, new Line("abc"), rect.Width);
        buf.Set(rect.X, rect.Y, Wide);
        buf.SetCursor(rect.X, rect.Y);
        buf.DrawHLine(rect.X, rect.Y, rect.Width);
        buf.DrawVLine(rect.X, rect.Y, rect.Height);
    }

    [Fact]
    public void B1_NegativeBufferSize_IsTreatedAsEmpty()
    {
        var buf = new Buffer(-3, -1);

        buf.SetString(0, 0, "x", Style.Default);

        Assert.Equal(0, buf.Width);
        Assert.Empty(buf.ToLines());
    }

    [Fact]
    public void B1_OutOfBoundsGet_ReturnsEmpty()
    {
        Assert.Equal(Cell.Empty, new Buffer(2, 2).Get(5, -1));
    }

    // ===== Wide characters =====

    [Fact]
    public void Set_WideCell_WritesHeadAndContinuationWithSameStyle()
    {
        var buf = new Buffer(4, 1);

        buf.Set(1, 0, new Cell("日", Color.Red, Color.Blue, Modifiers.Bold));

        Assert.Equal("日", buf.Get(1, 0).Symbol);
        Assert.True(buf.Get(2, 0).IsContinuation);
        AssertStyle(buf, 2, 0, Color.Red, Color.Blue, Modifiers.Bold);
        AssertLines(buf, " 日 ");
    }

    [Fact]
    public void B2_WritingOverContinuation_BlanksTheHead_KeepingItsStyle()
    {
        var buf = new Buffer(4, 1);
        buf.Set(0, 0, new Cell("日", Color.Red, Color.Blue, Modifiers.None));

        buf.Set(1, 0, Cell.Empty with { Symbol = "x" });

        AssertLines(buf, " x  ");
        AssertStyle(buf, 0, 0, Color.Red, Color.Blue);
    }

    [Fact]
    public void B3_WritingOverHead_BlanksTheContinuation()
    {
        var buf = new Buffer(4, 1);
        buf.Set(0, 0, Wide);

        buf.Set(0, 0, Cell.Empty with { Symbol = "x" });

        AssertLines(buf, "x   ");
        Assert.False(buf.Get(1, 0).IsContinuation);
    }

    [Fact]
    public void WideOverWide_Offset_BlanksBothNeighbours()
    {
        var buf = new Buffer(5, 1);
        buf.SetString(0, 0, "日本", Style.Default);

        buf.Set(1, 0, Cell.Empty with { Symbol = "中" });

        AssertLines(buf, " 中  ");
    }

    [Fact]
    public void B4_WideCellInLastColumn_IsWrittenAsSpace()
    {
        var buf = new Buffer(3, 1);
        buf.Set(2, 0, Cell.Empty with { Symbol = "x" });

        buf.Set(2, 0, Wide with { Fg = Color.Red });

        AssertLines(buf, "   ");
        AssertStyle(buf, 2, 0, Color.Red, Color.Reset);
    }

    [Fact]
    public void B5_SetString_StopsAtMaxWidth_WithoutSplittingWideCharacters()
    {
        var buf = new Buffer(10, 1);

        var written = buf.SetString(0, 0, "a日本", Style.Default, maxWidth: 4);

        Assert.Equal(3, written);
        AssertLines(buf, "a日       ");
    }

    [Fact]
    public void B5_SetString_StopsAtBufferEdge_WithoutSplittingWideCharacters()
    {
        var buf = new Buffer(4, 1);

        var written = buf.SetString(1, 0, "日本", Style.Default);

        Assert.Equal(2, written);
        AssertLines(buf, " 日 ");
    }

    [Fact]
    public void FillRect_WithWideCell_TilesWithoutBreakingInvariants()
    {
        var buf = new Buffer(5, 2);

        buf.FillRect(buf.Area, Wide);

        AssertLines(buf, """
            日日 
            日日 
            """);
    }

    // ===== SetString / SetLine / SetStyle =====

    [Fact]
    public void SetString_PatchesStyle_KeepingUnspecifiedAttributes()
    {
        var buf = new Buffer(3, 1);
        buf.SetStyle(buf.Area, new Style(Color.Gray, Color.Blue).Bold());

        buf.SetString(0, 0, "ab", new Style(Color.Red).Italic());

        AssertStyle(buf, 0, 0, Color.Red, Color.Blue, Modifiers.Bold | Modifiers.Italic);
        AssertStyle(buf, 2, 0, Color.Gray, Color.Blue, Modifiers.Bold);
    }

    [Fact]
    public void SetString_ClipsOnTheLeft()
    {
        var buf = new Buffer(4, 1);

        var advanced = buf.SetString(-2, 0, "abcd", Style.Default);

        Assert.Equal(4, advanced);
        AssertLines(buf, "cd  ");
    }

    [Fact]
    public void SetString_CombiningMarks_StayWithTheirBase()
    {
        var buf = new Buffer(3, 1);

        buf.SetString(0, 0, "éx", Style.Default);

        Assert.Equal("é", buf.Get(0, 0).Symbol);
        Assert.Equal("x", buf.Get(1, 0).Symbol);
    }

    [Fact]
    public void SetString_SkipsZeroWidthGraphemes()
    {
        var buf = new Buffer(3, 1);

        var written = buf.SetString(0, 0, "a​b", Style.Default);

        Assert.Equal(2, written);
        AssertLines(buf, "ab ");
    }

    [Fact]
    public void SetString_TabsAndLineBreaksBecomeSpaces()
    {
        var buf = new Buffer(7, 1);

        buf.SetString(0, 0, "a\tb\nc\r\nd", Style.Default);

        AssertLines(buf, "a b c d");
    }

    [Fact]
    public void SetLine_DrawsSpansInOrder_AndStopsAtMaxWidth()
    {
        var buf = new Buffer(8, 1);
        var line = new Line([new Span("ab", new Style(Color.Red)), new Span("日c", new Style(Color.Blue)), "zz"]);

        var written = buf.SetLine(1, 0, line, maxWidth: 4);

        Assert.Equal(4, written);
        AssertLines(buf, " ab日   ");
        AssertStyle(buf, 2, 0, Color.Red, Color.Reset);
        AssertStyle(buf, 3, 0, Color.Blue, Color.Reset);
    }

    [Fact]
    public void SetLine_StopsWhenAWideCharacterDoesNotFit()
    {
        var buf = new Buffer(5, 1);

        var written = buf.SetLine(0, 0, new Line([new Span("a日", default), new Span("b", default)]), maxWidth: 2);

        Assert.Equal(1, written);
        AssertLines(buf, "a    ");
    }

    [Fact]
    public void SetStyle_KeepsSymbols()
    {
        var buf = new Buffer(3, 1);
        buf.SetString(0, 0, "abc", Style.Default);

        buf.SetStyle(new Rect(1, 0, 5, 1), new Style(Bg: Color.Green));

        AssertLines(buf, "abc");
        AssertStyle(buf, 0, 0, Color.Reset, Color.Reset);
        AssertStyle(buf, 2, 0, Color.Reset, Color.Green);
    }

    // ===== Cursor and ToLines =====

    [Fact]
    public void SetCursor_RecordsRequest_ClearResetsIt()
    {
        var buf = new Buffer(5, 5);

        buf.SetCursor(2, 3);
        Assert.Equal((2, 3), buf.CursorRequest);

        buf.Clear();
        Assert.Null(buf.CursorRequest);
    }

    [Fact]
    public void SetCursor_OutOfBounds_IsIgnored()
    {
        var buf = new Buffer(5, 5);

        buf.SetCursor(5, 0);

        Assert.Null(buf.CursorRequest);
    }

    [Fact]
    public void ToLines_OmitsContinuations()
    {
        var buf = new Buffer(6, 2);
        buf.SetString(0, 1, "👍x日", Style.Default);

        AssertLines(buf, """
                  
            👍x日 
            """);
    }

    // ===== Legacy primitives keep the background (D10) =====

    [Fact]
    public void DrawText_WithBackground_SetsIt()
    {
        var buf = new Buffer(2, 1);

        buf.DrawText(0, 0, "x", ConsoleColor.White, ConsoleColor.DarkBlue);

        AssertStyle(buf, 0, 0, Color.White, Color.DarkBlue);
    }

    [Fact]
    public void DrawHLineAndProgressBar_KeepBackground()
    {
        var buf = new Buffer(4, 2);
        buf.SetStyle(buf.Area, new Style(Bg: Color.Blue));

        buf.DrawHLine(0, 0, 4);
        buf.DrawProgressBar(new Rect(0, 1, 4, 1), 0.5f);

        AssertLines(buf, """
            ────
            ██░░
            """);
        AssertStyle(buf, 0, 0, Color.Gray, Color.Blue);
        AssertStyle(buf, 3, 1, Color.DarkGray, Color.Blue);
    }

    [Fact]
    public void DrawBoxWithTitle_TruncatesTitleByWidth()
    {
        var buf = new Buffer(8, 3);

        buf.DrawBoxWithTitle(buf.Area, "日本語");

        // Truncated with an ellipsis since Phase 5 (rule W2)
        AssertLines(buf, """
            ┌─日…──┐
            │      │
            └──────┘
            """);
    }

    [Fact]
    public void DrawTextCentered_UsesDisplayWidth()
    {
        var buf = new Buffer(6, 1);

        buf.DrawTextCentered(buf.Area, "日本");

        AssertLines(buf, " 日本 ");
    }
}

public class BufferZeroWidthTests
{
    [Theory]
    [InlineData("\u0301")]
    [InlineData("\u200D")]
    [InlineData("\u200B")]
    [InlineData("\uFE0F")]
    public void Set_ZeroWidthSymbol_IsStoredAsSpace(string symbol)
    {
        var buf = new MiniTui.Buffer(2, 1);

        buf.Set(0, 0, Cell.Empty with { Symbol = symbol });

        Assert.Equal(" ", buf.Get(0, 0).Symbol);
    }
}

public class BufferLeadingZeroWidthTests
{
    [Fact]
    public void SetString_SkipsAGraphemeThatStartsWithAZeroWidthRune()
    {
        var buf = new MiniTui.Buffer(3, 1);

        var written = buf.SetString(0, 0, "️ab", Style.Default);

        Assert.Equal(2, written);
        Assert.Equal("ab ", buf.ToLines()[0]);
    }
}
