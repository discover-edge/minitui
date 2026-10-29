using System.Text;

namespace MiniTui.Tests;

public class AnsiBackendTests
{
    private static (FakeConsoleHost Host, AnsiBackend Backend) Entered(ColorMode mode = ColorMode.TrueColor, int width = 20, int height = 5)
    {
        var host = new FakeConsoleHost(width, height);
        var backend = new AnsiBackend(mode, host);
        backend.Enter();
        backend.GetSize();
        host.Output.SetLength(0);
        return (host, backend);
    }

    private static string Frame(AnsiBackend backend, FakeConsoleHost host, (int X, int Y)? cursor, bool full, params CellUpdate[] updates)
    {
        var start = (int)host.Output.Length;
        backend.Draw(updates, cursor, full);
        return Encoding.UTF8.GetString(host.Output.ToArray()[start..]);
    }

    private static CellUpdate At(int x, int y, string symbol, Color? fg = null, Color? bg = null, Modifiers m = Modifiers.None) =>
        new(x, y, new Cell(symbol, fg ?? Color.Reset, bg ?? Color.Reset, m));

    [Fact]
    public void ModeTest_EnterThenLeave_RestoresEveryMode()
    {
        var host = new FakeConsoleHost { IsWindows = true };
        var originalEncoding = host.OutputEncoding;
        var originalMode = host.OutputMode;
        var vt = new VirtualTerminal(20, 5);
        using var backend = new AnsiBackend(ColorMode.TrueColor, host);

        backend.Enter();
        backend.Draw([At(0, 0, "x", Color.Red, m: Modifiers.Bold)], (1, 1), fullRedraw: true);
        vt.Feed(host.Output.ToArray());
        Assert.True(vt.AltScreen);
        Assert.False(vt.AutoWrap);
        Assert.True(host.OutputEncoding is UTF8Encoding);
        Assert.Equal(originalMode | 0x000C, host.OutputMode);

        host.Output.SetLength(0);
        backend.Leave();
        vt.Feed(host.Output.ToArray());

        Assert.False(vt.AltScreen);
        Assert.True(vt.CursorVisible);
        Assert.True(vt.AutoWrap);
        Assert.False(vt.SynchronizedOutput);
        Assert.True(vt.StyleIsDefault);
        Assert.Same(originalEncoding, host.OutputEncoding);
        Assert.Equal(originalMode, host.OutputMode);
    }

    [Fact]
    public void Enter_NotOnWindows_LeavesEncodingAlone()
    {
        var host = new FakeConsoleHost();
        var encoding = host.OutputEncoding;
        using var backend = new AnsiBackend(ColorMode.TrueColor, host);

        backend.Enter();

        Assert.Same(encoding, host.OutputEncoding);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Enter_Redirected_Throws(bool output, bool input)
    {
        var host = new FakeConsoleHost { IsOutputRedirected = output, IsInputRedirected = input };
        using var backend = new AnsiBackend(ColorMode.TrueColor, host);

        var e = Assert.Throws<TerminalNotSupportedException>(backend.Enter);

        Assert.Equal("MiniTui requires an interactive terminal (stdin/stdout must not be redirected).", e.Message);
        Assert.Equal(0, host.Output.Length);
    }

    [Fact]
    public void EnterAndLeave_AreIdempotent()
    {
        var host = new FakeConsoleHost();
        using var backend = new AnsiBackend(ColorMode.TrueColor, host);

        backend.Enter();
        var afterEnter = host.Output.Length;
        backend.Enter();
        Assert.Equal(afterEnter, host.Output.Length);

        backend.Leave();
        var afterLeave = host.Output.Length;
        backend.Leave();
        Assert.Equal(afterLeave, host.Output.Length);
    }

    [Fact]
    public void Leave_NeverThrows_AndStillRestoresConsoleState_WhenWritesFail()
    {
        var host = new FakeConsoleHost { TreatControlCAsInput = false };
        using var backend = new AnsiBackend(ColorMode.TrueColor, host);
        backend.Enter();
        host.Output.FailWrites = true;

        backend.Leave();

        Assert.False(host.TreatControlCAsInput);
    }

    [Fact]
    public void NeverClearsTheUsersScreen()
    {
        var host = new FakeConsoleHost();
        using var backend = new AnsiBackend(ColorMode.TrueColor, host);

        backend.Enter();
        backend.Leave();

        var text = host.Output.Text;
        Assert.DoesNotContain("\e[2J", text[text.IndexOf("\e[?1049l", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("\e[3J", text);
    }

    [Fact]
    public void Draw_AfterLeave_WritesNothing()
    {
        var (host, backend) = Entered();
        using var _ = backend;
        backend.Leave();
        host.Output.SetLength(0);

        backend.Draw([At(0, 0, "x")], null, fullRedraw: true);

        Assert.Equal(0, host.Output.Length);
    }

    [Fact]
    public void Draw_NoChanges_WritesZeroBytes()
    {
        var (host, backend) = Entered();
        using var _ = backend;
        backend.Draw([At(0, 0, "x")], (2, 2), fullRedraw: false);
        var length = host.Output.Length;
        var writes = host.Output.Writes;

        backend.Draw([], (2, 2), fullRedraw: false);

        Assert.Equal(length, host.Output.Length);
        Assert.Equal(writes, host.Output.Writes);
    }

    [Fact]
    public void Draw_CursorChangeOnly_IsEmitted()
    {
        var (host, backend) = Entered();
        using var _ = backend;
        backend.Draw([], (2, 2), fullRedraw: false);

        Assert.Equal("\e[?2026h\e[0m\e[2;4H\e[?25h\e[?2026l", Frame(backend, host, (3, 1), false));
        Assert.Equal("\e[?2026h\e[0m\e[?25l\e[?2026l", Frame(backend, host, null, false));
    }

    [Fact]
    public void Draw_ExactBytes_ForASimpleFrame()
    {
        var (host, backend) = Entered();
        using var _ = backend;

        var frame = Frame(backend, host, null, true,
            At(0, 0, "a", Color.Red), At(1, 0, "b", Color.Red), At(5, 1, "c", Color.Indexed(200), Color.Rgb(1, 2, 3), Modifiers.Bold | Modifiers.Underlined));

        Assert.Equal(
            "\e[?2026h\e[2J" +
            "\e[1;1H\e[0;91;49mab" +
            "\e[2;6H\e[0;1;4;38;5;200;48;2;1;2;3mc" +
            "\e[0m\e[?25l\e[?2026l",
            frame);
    }

    [Fact]
    public void Draw_AllModifierCodes()
    {
        var (host, backend) = Entered();
        using var _ = backend;
        const Modifiers all = Modifiers.Bold | Modifiers.Dim | Modifiers.Italic | Modifiers.Underlined | Modifiers.Reversed | Modifiers.CrossedOut;

        Assert.Contains("\e[0;1;2;3;4;7;9;39;49m", Frame(backend, host, null, false, At(0, 0, "x", m: all)));
    }

    [Fact]
    public void Draw_WideCell_AdvancesTheCursorByTwo()
    {
        var (host, backend) = Entered();
        using var _ = backend;

        var frame = Frame(backend, host, null, false, At(0, 0, "日"), At(2, 0, "x"));

        Assert.Equal(1, frame.Split('H').Length - 1);
        Assert.Contains("日x", frame);
    }

    [Fact]
    public void Draw_LastColumn_ForcesACursorMoveForTheNextCell()
    {
        var (host, backend) = Entered(width: 3, height: 2);
        using var _ = backend;

        var frame = Frame(backend, host, null, false, At(2, 0, "a"), At(0, 1, "b"));

        Assert.Contains("\e[2;1H", frame);
    }

    [Theory]
    [InlineData(ColorMode.TrueColor, "\e[0;1;38;2;10;20;30;48;5;9m")]
    [InlineData(ColorMode.Ansi256, "\e[0;1;38;5;233;48;5;9m")]
    [InlineData(ColorMode.Ansi16, "\e[0;1;30;101m")]
    [InlineData(ColorMode.None, "\e[0;1m")]
    public void Draw_DownsamplesColorsAtEncodeTime(ColorMode mode, string expectedSgr)
    {
        var (host, backend) = Entered(mode);
        using var _ = backend;

        var frame = Frame(backend, host, null, false, At(0, 0, "x", Color.Rgb(10, 20, 30), Color.Indexed(9), Modifiers.Bold));

        Assert.Contains(expectedSgr + "x", frame);
    }

    [Fact]
    public void Acceptance_EscapeInText_EmitsEscOnlyInFrameworkSequences()
    {
        var host = new FakeConsoleHost(6, 1);
        var vt = new VirtualTerminal(6, 1);
        using var terminal = new Terminal(new AnsiBackend(ColorMode.TrueColor, host));

        terminal.Draw(buf => buf.SetString(0, 0, "\x1b[31mX", Style.Default));

        // The VirtualTerminal throws on any sequence it doesn't recognise, so injected text would fail here
        vt.Feed(host.Output.ToArray());
        Assert.Equal(["�", "[", "3", "1", "m", "X"], Enumerable.Range(0, 6).Select(x => vt.CellAt(x, 0).Symbol));
        var escCount = host.Output.ToArray().Count(b => b == 0x1B);
        Assert.Equal(vt.SequenceCount, escCount);
        Assert.Equal(Color.Reset, vt.CellAt(5, 0).Fg);
    }
}
