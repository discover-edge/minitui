using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class SmokeTests
{
    [Fact]
    public void Buffer_SetString_WritesCells()
    {
        var buf = new Buffer(10, 2);

        buf.SetString(1, 0, "hi", new Style(Color.Green));

        Assert.Equal(new Cell("h", Color.Green, Color.Reset, Modifiers.None), buf.Get(1, 0));
        Assert.Equal(new Cell("i", Color.Green, Color.Reset, Modifiers.None), buf.Get(2, 0));
        Assert.Equal(Cell.Empty, buf.Get(0, 0));
    }

    [Fact]
    public void Block_UsesBoxDrawingGlyphs()
    {
        var buf = new Buffer(3, 3);

        Widget.Block().Draw(buf, buf.Area);

        Assert.Equal("┌", buf.Get(0, 0).Symbol);
        Assert.Equal("┘", buf.Get(2, 2).Symbol);
    }
}
