// Also exercises APIs that are obsolete in 0.9 and removed in 1.0 (§9.7).
#pragma warning disable CS0618

using System.Text;

namespace MiniTui.Tests;

public class BackendDefectTests
{
    [Fact]
    public void D1_Enter_TreatsControlCAsInput_AndLeaveRestoresIt()
    {
        var host = new FakeConsoleHost { TreatControlCAsInput = false };
        using var backend = new AnsiBackend(ColorMode.TrueColor, host);

        backend.Enter();
        Assert.True(host.TreatControlCAsInput);

        backend.Leave();
        Assert.False(host.TreatControlCAsInput);
    }

    [Theory]
    [InlineData('\x03', ConsoleKey.C, true)]
    [InlineData('\x03', (ConsoleKey)0, false)]
    [InlineData('c', ConsoleKey.C, true)]
    public void D1_IsQuit_RecognisesCtrlC(char keyChar, ConsoleKey key, bool control)
    {
        Assert.True(Input.IsQuit(new ConsoleKeyInfo(keyChar, key, false, false, control)));
    }

    [Fact]
    public void D1_IsQuit_PlainC_IsNotQuit()
    {
        Assert.False(Input.IsQuit(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false)));
    }

    [Fact]
    public void D2_EnterAndLeave_PreserveTheUsersScreen()
    {
        var host = new FakeConsoleHost(10, 2);
        var vt = new VirtualTerminal(10, 2);
        vt.Feed("$ ls -la");

        using (var terminal = new Terminal(new AnsiBackend(ColorMode.TrueColor, host)))
            terminal.Draw(buf => buf.SetString(0, 0, "TUI", Style.Default));
        vt.Feed(host.Output.ToArray());

        Assert.False(vt.AltScreen);
        Assert.Equal("$", vt.CellAt(0, 0).Symbol);
        Assert.Equal("s", vt.CellAt(3, 0).Symbol);
    }

    [Fact]
    public void D2_Enter_SwitchesToAlternateScreenBeforeClearing()
    {
        var host = new FakeConsoleHost();
        using var backend = new AnsiBackend(ColorMode.TrueColor, host);

        backend.Enter();

        var text = host.Output.Text;
        Assert.StartsWith("\e[?1049h", text);
        Assert.True(text.IndexOf("\e[2J", StringComparison.Ordinal) > text.IndexOf("\e[?1049h", StringComparison.Ordinal));
    }

    [Fact]
    public void D5_FullRedraw_IsOneWriteAndOneFlush()
    {
        var host = new FakeConsoleHost(80, 24);
        using var terminal = new Terminal(new AnsiBackend(ColorMode.TrueColor, host));
        var (writes, flushes) = (host.Output.Writes, host.Output.Flushes);

        terminal.Draw(buf =>
        {
            for (var y = 0; y < buf.Height; y++)
                buf.SetString(0, y, new string((char)('a' + y % 26), buf.Width), new Style(Color.Indexed((byte)y)));
        });

        Assert.Equal(writes + 1, host.Output.Writes);
        Assert.Equal(flushes + 1, host.Output.Flushes);
    }

    [Fact]
    public void D5_ConsecutiveCells_DoNotRepeatCursorMovesOrColors()
    {
        var host = new FakeConsoleHost(80, 1);
        using var terminal = new Terminal(new AnsiBackend(ColorMode.TrueColor, host));
        var start = host.Output.Length;

        terminal.Draw(buf => buf.SetString(0, 0, new string('x', 80), new Style(Color.Red)));

        var frame = Encoding.UTF8.GetString(host.Output.ToArray()[(int)start..]);
        Assert.Equal(1, Count(frame, "H"));
        Assert.Equal(2, Count(frame, "m")); // one SGR for the row, one reset at the end
    }

    [Fact]
    public void D17_NestedTerminal_DoesNotReenterOrClear_AndOuterRedrawsFully()
    {
        var backend = new TestBackend(10, 2);
        using var outer = new Terminal(backend);
        static void DrawOuter(MiniTui.Buffer buf) => buf.SetString(0, 0, "outer", Style.Default);
        outer.Draw(DrawOuter);

        using (var inner = new Terminal(backend))
            inner.Draw(buf => buf.SetString(0, 1, "inner", Style.Default));

        outer.Draw(DrawOuter);

        Assert.Equal(["outer     ", "          "], backend.Lines);
        Assert.Equal(1, backend.EnterCount);
        Assert.Equal(0, backend.LeaveCount);
    }

    [Fact]
    public void D17_LastTerminal_LeavesOnce()
    {
        var backend = new TestBackend(4, 1);

        using (new Terminal(backend))
        using (new Terminal(backend)) { }

        Assert.Equal(1, backend.EnterCount);
        Assert.Equal(1, backend.LeaveCount);
        Assert.False(backend.IsEntered);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}
