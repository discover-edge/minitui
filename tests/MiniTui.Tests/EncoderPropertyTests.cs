namespace MiniTui.Tests;

/// <summary>
/// §10.1 encoder property test: random frames drawn through Terminal + AnsiBackend, parsed back by
/// the VirtualTerminal, must reproduce the buffer exactly (with downsampled colors) in every color mode.
/// </summary>
public class EncoderPropertyTests
{
    private static readonly string[] Symbols =
    [
        "a", "Z", "0", " ", "~", "é", "e\u0301", "─", "█", "⠿", "日", "한", "１", "👍", "👍🏽", "👨‍👩‍👧", "🇺🇸", "❤️",
        "\x1b", "\t", "\n", "\x7f", "\u0085", "\u200B", "\u0301", "\u200D", "\uFFFD",
    ];

    private static readonly Modifiers[] AllModifiers =
        [Modifiers.Bold, Modifiers.Dim, Modifiers.Italic, Modifiers.Underlined, Modifiers.Reversed, Modifiers.CrossedOut];

    public static TheoryData<int> Batches()
    {
        var data = new TheoryData<int>();
        for (var batch = 0; batch < 20; batch++) data.Add(batch);
        return data;
    }

    [Theory]
    [MemberData(nameof(Batches))]
    public void RandomFrames_RoundTripThroughTheVirtualTerminal(int batch)
    {
        // 20 batches × 100 seeds = 2,000 random sequences
        for (var seed = batch * 100; seed < batch * 100 + 100; seed++)
            RunSequence(seed);
    }

    private static void RunSequence(int seed)
    {
        var random = new Random(seed);
        var mode = (ColorMode)(seed % 4);
        var (width, height) = (random.Next(1, 25), random.Next(1, 7));
        var host = new FakeConsoleHost(width, height);
        var vt = new VirtualTerminal(width, height);
        var fed = 0;
        using var terminal = new Terminal(new AnsiBackend(mode, host));

        var frames = random.Next(1, 5);
        for (var frame = 0; frame < frames; frame++)
        {
            if (random.Next(6) == 0)
            {
                (host.Width, host.Height) = (random.Next(1, 25), random.Next(1, 7));
                if (terminal.CheckResize())
                {
                    // A real terminal reflows on resize; the next frame is a full redraw, so start fresh
                    vt = new VirtualTerminal(host.Width, host.Height);
                    fed = (int)host.Output.Length;
                }
            }
            if (random.Next(8) == 0) terminal.Invalidate();

            Cell[] expected = [];
            (int X, int Y)? cursor = null;
            terminal.Draw(buf =>
            {
                DrawRandom(buf, random);
                cursor = buf.CursorRequest;
                expected = Snapshot(buf);
            });

            var output = host.Output.ToArray();
            vt.Feed(output.AsSpan(fed));
            fed = output.Length;
            AssertMatches(vt, expected, cursor, mode, seed, frame);
        }
    }

    private static void DrawRandom(MiniTui.Buffer buf, Random random)
    {
        var operations = random.Next(0, 12);
        for (var i = 0; i < operations; i++)
        {
            var x = random.Next(-2, buf.Width + 2);
            var y = random.Next(-1, buf.Height + 1);
            switch (random.Next(5))
            {
                case 0 or 1:
                    var text = string.Concat(Enumerable.Range(0, random.Next(1, 10)).Select(_ => Pick(random)));
                    buf.SetString(x, y, text, RandomStyle(random), random.Next(0, 3) == 0 ? random.Next(0, 6) : int.MaxValue);
                    break;
                case 2:
                    buf.Set(x, y, new Cell(Pick(random), RandomColor(random), RandomColor(random), RandomModifiers(random)));
                    break;
                case 3:
                    buf.SetStyle(new Rect(x, y, random.Next(-1, 8), random.Next(-1, 4)), RandomStyle(random));
                    break;
                default:
                    buf.FillRect(new Rect(x, y, random.Next(0, 8), random.Next(0, 3)),
                        new Cell(Pick(random), RandomColor(random), RandomColor(random), RandomModifiers(random)));
                    break;
            }
        }
        if (random.Next(3) == 0) buf.SetCursor(random.Next(buf.Width), random.Next(buf.Height));
    }

    private static void AssertMatches(VirtualTerminal vt, Cell[] expected, (int X, int Y)? cursor, ColorMode mode, int seed, int frame)
    {
        for (var y = 0; y < vt.Height; y++)
        for (var x = 0; x < vt.Width; x++)
        {
            var want = expected[y * vt.Width + x];
            var got = vt.CellAt(x, y);
            var where = $"seed {seed}, frame {frame}, mode {mode}, cell ({x},{y})";

            if (want.IsContinuation)
            {
                Assert.True(got.IsContinuation, $"{where}: expected a continuation, got '{got.Symbol}'");
                continue;
            }
            Assert.True(want.Symbol == got.Symbol, $"{where}: expected '{want.Symbol}', got '{got.Symbol}'");
            Assert.True(want.Fg.ToMode(mode) == got.Fg, $"{where}: fg {want.Fg.ToMode(mode)} != {got.Fg}");
            Assert.True(want.Bg.ToMode(mode) == got.Bg, $"{where}: bg {want.Bg.ToMode(mode)} != {got.Bg}");
            Assert.True(want.Modifiers == got.Modifiers, $"{where}: modifiers {want.Modifiers} != {got.Modifiers}");
        }

        Assert.Equal(cursor is not null, vt.CursorVisible);
        if (cursor is var (cx, cy)) Assert.Equal((cx, cy), (vt.CursorX, vt.CursorY));
        Assert.False(vt.SynchronizedOutput);
    }

    private static Cell[] Snapshot(MiniTui.Buffer buf)
    {
        var cells = new Cell[buf.Width * buf.Height];
        for (var y = 0; y < buf.Height; y++)
        for (var x = 0; x < buf.Width; x++)
            cells[y * buf.Width + x] = buf.Get(x, y);
        return cells;
    }

    private static string Pick(Random random) => Symbols[random.Next(Symbols.Length)];

    private static Style RandomStyle(Random random) => new(
        random.Next(3) == 0 ? null : RandomColor(random),
        random.Next(3) == 0 ? null : RandomColor(random),
        RandomModifiers(random),
        RandomModifiers(random));

    private static Color RandomColor(Random random) => random.Next(4) switch
    {
        0 => Color.Reset,
        1 => (ConsoleColor)random.Next(16),
        2 => Color.Indexed((byte)random.Next(256)),
        _ => Color.Rgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
    };

    private static Modifiers RandomModifiers(Random random)
    {
        var m = Modifiers.None;
        foreach (var modifier in AllModifiers)
            if (random.Next(4) == 0) m |= modifier;
        return m;
    }
}
