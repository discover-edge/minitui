using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class WidgetDefectTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void D6_List_DoesNotThrow_WhenInnerWidthIsZeroOrNegative(int width)
    {
        var buf = new Buffer(10, 5);

        Widgets.List(buf, new Rect(0, 0, width, 5), ["a long item", "b"], 0, focused: true, title: "T");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void D6_Table_DoesNotThrow_WhenColumnWidthIsBelowTwo(int width)
    {
        var buf = new Buffer(10, 5);

        Widgets.Table(buf, new Rect(0, 0, width, 5), ["Name", "Value"], [["alpha", "1"], ["beta", "2"]], 0);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 3)]
    [InlineData(6, 8)]
    public void D6_Modal_DoesNotThrow_OnTinyBuffers(int width, int height)
    {
        var buf = new Buffer(width, height);

        Widgets.Modal(buf, "Title", ["a line that is much too long"], "footer");
    }

    [Fact]
    public void D9_List_TruncatesWideItemsByDisplayWidth()
    {
        var buf = new Buffer(8, 3);

        Widgets.List(buf, buf.Area, ["日本語テキスト"], 0, focused: true, showScrollInfo: false);

        Assert.Equal("│日本… │", buf.ToLines()[1]);
    }

    [Fact]
    public void D9_Table_TruncatesWideCellsByDisplayWidth()
    {
        var buf = new Buffer(10, 3);

        Widgets.Table(buf, buf.Area, ["名前", "値"], [["日本語", "x"]]);

        // Since Phase 5 the columns are Fill(1) with one column of spacing (5 + 1 + 4)
        Assert.Equal("名前  値  ", buf.ToLines()[0]);
        Assert.Equal("日本… x   ", buf.ToLines()[2]);
    }

    [Fact]
    public void D9_StatusBar_RightAlignsByDisplayWidth()
    {
        var buf = new Buffer(10, 1);

        Widgets.StatusBar(buf, buf.Area, "", "日本");

        Assert.Equal("     日本 ", buf.ToLines()[0]);
    }
}
