using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniTui.Tests;

public class TerminalInputSourceTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void EscapeTimeout_OutOfRange_Throws(int ms)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalInputSource(TimeSpan.FromMilliseconds(ms)));
    }

    [Fact]
    public void EscapeTimeout_DefaultsTo50ms()
    {
        using var source = new TerminalInputSource();

        Assert.Equal(TimeSpan.FromMilliseconds(50), TerminalInputSource.DefaultEscapeTimeout);
        Assert.Equal(TerminalInputSource.DefaultEscapeTimeout, source.EscapeTimeout);
        Assert.Equal(TimeSpan.Zero, new TerminalInputSource(TimeSpan.Zero).EscapeTimeout);
    }

    [Fact]
    public void NonTerminalInput_BehavesLikeConsoleInput()
    {
        // stdin isn't a terminal under the test runner
        using var source = new TerminalInputSource();
        ((IInputLifecycle)source).Activate();

        Assert.False(source.IsRaw);
        using var console = new ConsoleInputSource();
        var expected = Record.Exception(() => console.TryRead(out _));
        var actual = Record.Exception(() => source.TryRead(out _));
        Assert.Equal(expected?.GetType(), actual?.GetType());
        Assert.True(((IWaitableInputSource)source).WaitForInput(TimeSpan.FromMilliseconds(1)));
        ((IInputLifecycle)source).Deactivate();
    }

    [Fact]
    public void DefaultInput_IsTerminalInput_OnlyForTheConsole()
    {
        Assert.Same(InputPump.Terminal, InputPump.DefaultFor(null));
        Assert.Same(InputPump.Terminal, InputPump.DefaultFor(new AnsiBackend(ColorMode.Ansi16)));
        Assert.Same(InputPump.Console, InputPump.DefaultFor(new TestBackend(10, 5)));
    }

    [Fact]
    public void DefaultInput_MinituiInputConsole_SelectsConsoleInput()
    {
        var previous = Environment.GetEnvironmentVariable("MINITUI_INPUT");
        try
        {
            Environment.SetEnvironmentVariable("MINITUI_INPUT", "Console");
            Assert.Same(InputPump.Console, InputPump.DefaultFor(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINITUI_INPUT", previous);
        }
    }

    [Fact]
    public void InputPump_ActivatesForTheFirstSubscriber_AndDeactivatesWithTheLast()
    {
        var source = new LifecycleSource();
        var first = InputPump.Subscribe(source, _ => { });
        var second = InputPump.Subscribe(source, _ => { });
        Assert.Equal(1, source.Activations);

        first.Dispose();
        Assert.Equal(0, source.Deactivations);
        second.Dispose();

        Assert.Equal(1, source.Deactivations);
    }

    private sealed class LifecycleSource : IInputSource, IInputLifecycle, IWaitableInputSource
    {
        public int Activations, Deactivations;
        public void Activate() => Activations++;
        public void Deactivate() => Deactivations++;
        public bool WaitForInput(TimeSpan timeout)
        {
            Thread.Sleep(1);
            return false;
        }
        public bool TryRead(out ConsoleKeyInfo key)
        {
            key = default;
            return false;
        }
        public void Dispose() { }
    }

    [Fact]
    public void RawMode_OnAPseudoTerminal_DeliversBytesUnchanged_AndRestores()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var pty = Pty.Open();
        var before = pty.GetMode();

        using var source = new TerminalInputSource(pty.Follower, TimeSpan.FromMilliseconds(50));
        ((IInputLifecycle)source).Activate();
        Assert.True(source.IsRaw);
        Assert.NotEqual(before, pty.GetMode());

        pty.Write("a\x03\x1a\x13\x11\x1b[A\x1b[1;5C日\r");
        var keys = ReadKeys(source, 9);

        Assert.Equal(
        [
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true),
            new ConsoleKeyInfo('\x1a', ConsoleKey.Z, false, false, true),
            new ConsoleKeyInfo('\x13', ConsoleKey.S, false, false, true),
            new ConsoleKeyInfo('\x11', ConsoleKey.Q, false, false, true),
            new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, true),
            new ConsoleKeyInfo('日', 0, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ], keys);
        Assert.Equal("", pty.ReadEcho()); // no echo in raw mode

        ((IInputLifecycle)source).Deactivate();
        Assert.False(source.IsRaw);
        Assert.Equal(before, pty.GetMode());
    }

    [Fact]
    public void RawMode_IsReapplied_WhenSomethingElseResetsIt()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var pty = Pty.Open();
        var before = pty.GetMode();
        using var source = new TerminalInputSource(pty.Follower, TimeSpan.FromMilliseconds(50));
        ((IInputLifecycle)source).Activate();
        var raw = pty.GetMode();

        // What .NET does after a child process exits
        pty.SetMode(before);
        Thread.Sleep(300);
        source.TryRead(out _);

        Assert.Equal(raw, pty.GetMode());
        ((IInputLifecycle)source).Deactivate();
        Assert.Equal(before, pty.GetMode());
    }

    [Fact]
    public void LoneEscape_ResolvesAfterTheTimeout()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var pty = Pty.Open();
        using var source = new TerminalInputSource(pty.Follower, TimeSpan.FromMilliseconds(50));
        ((IInputLifecycle)source).Activate();

        pty.Write("\x1b");
        var clock = Stopwatch.StartNew();
        var keys = ReadKeys(source, 1);

        Assert.Equal(ConsoleKey.Escape, keys[0].Key);
        Assert.InRange(clock.ElapsedMilliseconds, 40, 1000);
        ((IInputLifecycle)source).Deactivate();
    }

    [Fact]
    public void RawMode_TurnsBracketedPasteOn_AndRestoreTurnsItOff()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var pty = Pty.Open();
        using var source = new TerminalInputSource(pty.Follower, TimeSpan.FromMilliseconds(50), outputFd: pty.Follower);

        ((IInputLifecycle)source).Activate();
        Assert.Equal("\x1b[?2004h", pty.ReadEcho());

        pty.Write("a\x1b[200~line 1\rline 2\x1b[201~b");
        var items = new List<string>();
        var clock = Stopwatch.StartNew();
        while (items.Count < 3 && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            while (((IInputItemSource)source).TryReadItem(out var item))
                items.Add(item.Paste is { } text ? $"paste {text}" : $"key {item.Key.KeyChar}");
            ((IWaitableInputSource)source).WaitForInput(TimeSpan.FromMilliseconds(100));
        }
        Assert.Equal(["key a", "paste line 1\nline 2", "key b"], items);

        ((IInputLifecycle)source).Deactivate();
        Assert.Equal("\x1b[?2004l", pty.ReadEcho());
    }

    [Fact]
    public void MouseCapture_WritesTheMouseModes_AndRestoreTurnsThemOff()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var pty = Pty.Open();
        using var source = new TerminalInputSource(pty.Follower, TimeSpan.FromMilliseconds(50), outputFd: pty.Follower);
        var capture = (IMouseCapture)source;

        ((IInputLifecycle)source).Activate();
        Assert.Equal("\x1b[?2004h", pty.ReadEcho());

        // Reference counted: only the first take and the last release write
        capture.SetMouseCapture(true);
        capture.SetMouseCapture(true);
        Assert.Equal("\x1b[?1000h\x1b[?1006h", pty.ReadEcho());
        capture.SetMouseCapture(false);
        capture.SetMouseCapture(false);
        Assert.Equal("\x1b[?1006l\x1b[?1000l", pty.ReadEcho());

        capture.SetMouseCapture(true);
        Assert.Equal("\x1b[?1000h\x1b[?1006h", pty.ReadEcho());
        pty.Write("\x1b[<0;5;6M");
        var items = new List<MouseEvent>();
        var clock = Stopwatch.StartNew();
        while (items.Count < 1 && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            while (((IInputItemSource)source).TryReadItem(out var item))
                if (item.Mouse is { } mouse) items.Add(mouse);
            ((IWaitableInputSource)source).WaitForInput(TimeSpan.FromMilliseconds(100));
        }
        Assert.Equal([new MouseEvent(MouseAction.Press, MouseButton.Left, 4, 5)], items);

        ((IInputLifecycle)source).Deactivate();
        Assert.Equal("\x1b[?1006l\x1b[?1000l\x1b[?2004l", pty.ReadEcho());
        capture.SetMouseCapture(false);
    }

    [Fact]
    public void MouseCapture_TakenBeforeActivating_TurnsOnWithRawMode()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var pty = Pty.Open();
        using var source = new TerminalInputSource(pty.Follower, TimeSpan.FromMilliseconds(50), outputFd: pty.Follower);

        ((IMouseCapture)source).SetMouseCapture(true);
        Assert.Equal("", pty.ReadEcho());
        ((IInputLifecycle)source).Activate();
        Assert.Equal("\x1b[?2004h\x1b[?1000h\x1b[?1006h", pty.ReadEcho());
        ((IInputLifecycle)source).Deactivate();
        Assert.Equal("\x1b[?1006l\x1b[?1000l\x1b[?2004l", pty.ReadEcho());
    }

    [Fact]
    public void Pause_GivesTheTerminalBack_AndResumeKeepsTheOriginalSavedMode()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var pty = Pty.Open();
        var original = pty.GetMode();
        using var source = new TerminalInputSource(pty.Follower, TimeSpan.FromMilliseconds(50), outputFd: pty.Follower);
        var subscription = InputPump.Subscribe(source, _ => { });
        ((IMouseCapture)source).SetMouseCapture(true);
        Assert.Equal("\x1b[?2004h\x1b[?1000h\x1b[?1006h", pty.ReadEcho());
        var raw = pty.GetMode();
        Assert.NotEqual(original, raw);

        using (InputPump.Pause(source))
        {
            Assert.Equal(original, pty.GetMode());
            Assert.Equal("\x1b[?1006l\x1b[?1000l\x1b[?2004l", pty.ReadEcho());

            // A child that leaves the terminal in another mode mustn't become the mode restored at exit
            pty.SetMode(raw);
        }
        Assert.Equal(raw, pty.GetMode());
        Assert.Equal("\x1b[?2004h\x1b[?1000h\x1b[?1006h", pty.ReadEcho());

        ((IMouseCapture)source).SetMouseCapture(false);
        subscription.Dispose();
        Assert.Equal(original, pty.GetMode());
    }

    [Fact]
    public void RawMode_WithoutATerminalOutput_DoesNotWriteBracketedPaste()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var pty = Pty.Open();
        var path = Path.GetTempFileName();
        try
        {
            using (var file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite))
            {
                var fd = (int)file.DangerousGetHandle();
                using var source = new TerminalInputSource(pty.Follower, TimeSpan.FromMilliseconds(50), outputFd: fd);
                ((IInputLifecycle)source).Activate();
                Assert.True(source.IsRaw);
                ((IInputLifecycle)source).Deactivate();
            }
            Assert.Equal(0, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static List<ConsoleKeyInfo> ReadKeys(TerminalInputSource source, int count)
    {
        var keys = new List<ConsoleKeyInfo>();
        var waitable = (IWaitableInputSource)source;
        var clock = Stopwatch.StartNew();
        while (keys.Count < count && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            while (source.TryRead(out var key)) keys.Add(key);
            if (keys.Count < count) waitable.WaitForInput(TimeSpan.FromMilliseconds(100));
        }
        return keys;
    }

    /// <summary>A pseudo-terminal pair: write to the leader, and the follower reads it like a keyboard.</summary>
    private sealed class Pty : IDisposable
    {
        private const int ORdwr = 2;

        private Pty(int leader, int follower)
        {
            Leader = leader;
            Follower = follower;
        }

        public int Leader { get; }
        public int Follower { get; }

        public static Pty Open()
        {
            var noCtty = OperatingSystem.IsMacOS() ? 0x20000 : 0x100;
            var leader = posix_openpt(ORdwr | noCtty);
            Assert.True(leader >= 0, "posix_openpt failed");
            Assert.Equal(0, grantpt(leader));
            Assert.Equal(0, unlockpt(leader));
            var name = Marshal.PtrToStringAnsi(ptsname(leader))!;
            var follower = open(name, ORdwr | noCtty);
            Assert.True(follower >= 0, $"open {name} failed");
            return new Pty(leader, follower);
        }

        public string GetMode()
        {
            var buffer = new byte[256];
            Assert.Equal(0, tcgetattr(Follower, buffer));

            // PENDIN is status the kernel sets itself (macOS does when leaving raw mode), not a setting to restore.
            // It's in c_lflag: macOS 0x20000000 at offset 24, Linux 0x4000 at offset 12 (little-endian).
            if (OperatingSystem.IsMacOS()) buffer[27] &= unchecked((byte)~0x20);
            else buffer[13] &= unchecked((byte)~0x40);
            return Convert.ToHexString(buffer);
        }

        public void SetMode(string mode) => Assert.Equal(0, tcsetattr(Follower, 0, Convert.FromHexString(mode)));

        public void Write(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            Assert.Equal(bytes.Length, (int)write(Leader, bytes, (nuint)bytes.Length));
        }

        /// <summary>Whatever the terminal echoed back to the leader.</summary>
        /// <summary>Everything written to the follower: waits 100 ms for the first bytes, then reads until 20 ms pass with none.</summary>
        public string ReadEcho()
        {
            var text = new StringBuilder();
            var buffer = new byte[256];
            var timeout = 100;
            while (true)
            {
                var poll = new PollFd { Fd = Leader, Events = 1 };
                if (poll_(ref poll, 1, timeout) <= 0) return text.ToString();
                var n = read(Leader, buffer, (nuint)buffer.Length);
                if (n <= 0) return text.ToString();
                text.Append(Encoding.UTF8.GetString(buffer, 0, (int)n));
                timeout = 20;
            }
        }

        public void Dispose()
        {
            close(Follower);
            close(Leader);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd
        {
            public int Fd;
            public short Events;
            public short Revents;
        }

        [DllImport("libc")] private static extern int posix_openpt(int flags);
        [DllImport("libc")] private static extern int grantpt(int fd);
        [DllImport("libc")] private static extern int unlockpt(int fd);
        [DllImport("libc")] private static extern nint ptsname(int fd);
        [DllImport("libc")] private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);
        [DllImport("libc")] private static extern int close(int fd);
        [DllImport("libc")] private static extern int tcgetattr(int fd, [Out] byte[] termios);
        [DllImport("libc")] private static extern int tcsetattr(int fd, int actions, byte[] termios);
        [DllImport("libc")] private static extern nint write(int fd, byte[] buffer, nuint count);
        [DllImport("libc")] private static extern nint read(int fd, [Out] byte[] buffer, nuint count);
        [DllImport("libc", EntryPoint = "poll")] private static extern int poll_(ref PollFd fds, nuint nfds, int timeout);
    }
}
