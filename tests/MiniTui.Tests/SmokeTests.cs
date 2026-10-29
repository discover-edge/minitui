using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class SmokeTests
{
    [Fact]
    public void Buffer_DrawText_WritesCells()
    {
        var buf = new Buffer(10, 2);

        buf.DrawText(1, 0, "hi", ConsoleColor.Green);

        Assert.Equal(new Cell('h', ConsoleColor.Green, ConsoleColor.Black), buf.Get(1, 0));
        Assert.Equal(new Cell('i', ConsoleColor.Green, ConsoleColor.Black), buf.Get(2, 0));
        Assert.Equal(Cell.Empty, buf.Get(0, 0));
    }

    [Fact]
    public void Buffer_DrawBox_UsesBoxDrawingGlyphs()
    {
        var buf = new Buffer(3, 3);

        buf.DrawBox(buf.Area);

        Assert.Equal('┌', buf.Get(0, 0).Char);
        Assert.Equal('┘', buf.Get(2, 2).Char);
    }
}
