// Also exercises APIs that are obsolete in 0.9 and removed in 1.0 (§9.7).
#pragma warning disable CS0618

using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class SmokeTests
{
    [Fact]
    public void Buffer_DrawText_WritesCells()
    {
        var buf = new Buffer(10, 2);

        buf.DrawText(1, 0, "hi", ConsoleColor.Green);

        Assert.Equal(new Cell("h", Color.Green, Color.Reset, Modifiers.None), buf.Get(1, 0));
        Assert.Equal(new Cell("i", Color.Green, Color.Reset, Modifiers.None), buf.Get(2, 0));
        Assert.Equal(Cell.Empty, buf.Get(0, 0));
    }

    [Fact]
    public void Buffer_DrawBox_UsesBoxDrawingGlyphs()
    {
        var buf = new Buffer(3, 3);

        buf.DrawBox(buf.Area);

        Assert.Equal("┌", buf.Get(0, 0).Symbol);
        Assert.Equal("┘", buf.Get(2, 2).Symbol);
    }
}
