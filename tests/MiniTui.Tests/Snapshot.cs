using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Snapshot assertions for buffers (§10.1).</summary>
internal static class Snapshot
{
    /// <summary>Compare <see cref="Buffer.ToLines"/> with a raw string literal, one line per row.</summary>
    public static void AssertLines(Buffer buffer, string expected)
    {
        var expectedLines = expected.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(string.Join("\n", expectedLines), string.Join("\n", buffer.ToLines()));
    }

    /// <summary>Like <see cref="AssertLines"/>, ignoring trailing spaces on each row.</summary>
    public static void AssertTrimmed(Buffer buffer, string expected)
    {
        var expectedLines = expected.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd());
        Assert.Equal(string.Join("\n", expectedLines), string.Join("\n", buffer.ToLines().Select(l => l.TrimEnd())));
    }

    /// <summary>Check one cell's colors and modifiers.</summary>
    public static void AssertStyle(Buffer buffer, int x, int y, Color fg, Color bg, Modifiers modifiers = Modifiers.None)
    {
        var cell = buffer.Get(x, y);
        Assert.Equal((fg, bg, modifiers), (cell.Fg, cell.Bg, cell.Modifiers));
    }
}
