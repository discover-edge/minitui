namespace MiniTui.Tests;

public class TerminalTests
{
    private static void DrawStatic(MiniTui.Buffer buf)
    {
        buf.SetString(0, 0, "Hello, world", new Style(Color.Green).Bold());
        buf.SetString(2, 1, "status: ok", new Style(Bg: Color.Blue));
        buf.SetStyle(new Rect(0, 2, buf.Width, 1), new Style(Bg: Color.DarkGray));
        buf.SetCursor(3, 3);
    }

    [Fact]
    public void UnchangedFrame_WritesZeroBytes_AndAllocatesNothing()
    {
        var host = new FakeConsoleHost(80, 24);
        using var terminal = new Terminal(new AnsiBackend(ColorMode.TrueColor, host));
        Action<MiniTui.Buffer> render = DrawStatic;
        for (var i = 0; i < 3; i++) terminal.Draw(render);
        var length = host.Output.Length;

        var before = GC.GetAllocatedBytesForCurrentThread();
        terminal.Draw(render);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(length, host.Output.Length);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FirstFrame_IsAFullRedraw_LaterFramesSendOnlyChanges()
    {
        var backend = new TestBackend(10, 3);
        using var terminal = new Terminal(backend);

        terminal.Draw(buf => buf.SetString(0, 0, "abc", Style.Default));
        Assert.Equal(1, backend.FullRedrawCount);
        Assert.Equal(30, backend.LastUpdateCount);

        terminal.Draw(buf => buf.SetString(0, 0, "abd", Style.Default));
        Assert.Equal(1, backend.FullRedrawCount);
        Assert.Equal(1, backend.LastUpdateCount);
        Assert.Equal("abd       ", backend.Lines[0]);
    }

    [Fact]
    public void ContinuationCells_AreNeverSent()
    {
        var backend = new RecordingBackend(6, 1);
        using var terminal = new Terminal(backend);

        terminal.Draw(buf => buf.SetString(0, 0, "日本x", Style.Default));

        Assert.DoesNotContain(backend.Updates, u => u.Cell.IsContinuation);
        Assert.Equal([0, 2, 4, 5], backend.Updates.Select(u => u.X));
    }

    [Fact]
    public void Invalidate_ForcesAFullRedraw()
    {
        var backend = new TestBackend(4, 1);
        using var terminal = new Terminal(backend);
        terminal.Draw(_ => { });

        terminal.Invalidate();
        terminal.Draw(_ => { });

        Assert.Equal(2, backend.FullRedrawCount);
        Assert.Equal(4, backend.LastUpdateCount);
    }

    [Fact]
    public void CheckResize_RecreatesBuffersAndRedrawsFully()
    {
        var backend = new TestBackend(4, 1);
        using var terminal = new Terminal(backend);
        terminal.Draw(_ => { });
        Assert.False(terminal.CheckResize());

        backend.Resize(6, 2);

        Assert.True(terminal.CheckResize());
        Assert.Equal(new Rect(0, 0, 6, 2), terminal.Area);
        terminal.Draw(buf => buf.SetString(0, 1, "resized", Style.Default));
        Assert.Equal(2, backend.FullRedrawCount);
        Assert.Equal(["      ", "resize"], backend.Lines);
    }

    [Fact]
    public void Cursor_IsPassedToTheBackend()
    {
        var backend = new TestBackend(5, 5);
        using var terminal = new Terminal(backend);

        terminal.Draw(buf => buf.SetCursor(2, 3));
        Assert.Equal((2, 3), backend.CursorPosition);

        terminal.Draw(_ => { });
        Assert.False(backend.CursorVisible);
    }

    [Fact]
    public void Dispose_LeavesTheTerminal()
    {
        var backend = new TestBackend();
        var terminal = new Terminal(backend);
        Assert.True(backend.IsEntered);

        terminal.Dispose();
        terminal.Dispose();

        Assert.False(backend.IsEntered);
        Assert.Equal(1, backend.LeaveCount);
    }

    [Fact]
    public void SeparateBackends_HaveSeparateSessions()
    {
        var a = new TestBackend();
        var b = new TestBackend();

        using (new Terminal(a))
        using (new Terminal(b))
        {
            Assert.True(a.IsEntered);
            Assert.True(b.IsEntered);
        }

        Assert.Equal(1, a.LeaveCount);
        Assert.Equal(1, b.LeaveCount);
    }

    [Fact]
    public void NestedTerminal_DrawsThroughTheSessionBackend_AndRedrawsFullyFirst()
    {
        var outerBackend = new TestBackend(6, 1);
        using var outer = new Terminal(outerBackend);
        outer.Draw(buf => buf.SetString(0, 0, "outer", Style.Default));

        using var inner = new Terminal(outerBackend);
        inner.Draw(buf => buf.SetString(0, 0, "in", Style.Default));

        Assert.Equal("in    ", outerBackend.Lines[0]);
        Assert.Equal(2, outerBackend.FullRedrawCount);
    }

    [Fact]
    public void Enter_Failure_DoesNotLeaveASession()
    {
        var host = new FakeConsoleHost { IsOutputRedirected = true };
        var backend = new AnsiBackend(ColorMode.TrueColor, host);

        Assert.Throws<TerminalNotSupportedException>(() => new Terminal(backend));

        Assert.False(TerminalSession.IsActive(backend));
    }

    private sealed class RecordingBackend(int width, int height) : ITerminalBackend
    {
        public List<CellUpdate> Updates { get; } = [];
        public (int Width, int Height) GetSize() => (width, height);
        public void Enter() { }
        public void Leave() { }
        public void Draw(IReadOnlyList<CellUpdate> updates, (int X, int Y)? cursor, bool fullRedraw) => Updates.AddRange(updates);
        public void Dispose() { }
    }
}
