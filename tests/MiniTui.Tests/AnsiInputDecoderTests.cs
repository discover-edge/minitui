using System.Text;

namespace MiniTui.Tests;

public class AnsiInputDecoderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private sealed class Sink : IInputSink
    {
        public List<ConsoleKeyInfo> Keys { get; } = [];
        public void Key(ConsoleKeyInfo key) => Keys.Add(key);
    }

    private sealed class CountingSink : IInputSink
    {
        public int Count;
        public void Key(ConsoleKeyInfo key) => Count++;
    }

    private static ConsoleKeyInfo K(char c, ConsoleKey key, ConsoleModifiers mods = 0) =>
        new(c, key, (mods & ConsoleModifiers.Shift) != 0, (mods & ConsoleModifiers.Alt) != 0, (mods & ConsoleModifiers.Control) != 0);

    private static ConsoleKeyInfo N(ConsoleKey key, ConsoleModifiers mods = 0) => K(key == ConsoleKey.Tab ? '\t' : '\0', key, mods);

    private const ConsoleModifiers Shift = ConsoleModifiers.Shift;
    private const ConsoleModifiers Alt = ConsoleModifiers.Alt;
    private const ConsoleModifiers Ctrl = ConsoleModifiers.Control;

    /// <summary>Feed everything at once, then let the escape timeout pass.</summary>
    private static List<ConsoleKeyInfo> Decode(string input) => Decode(Encoding.UTF8.GetBytes(input));

    private static List<ConsoleKeyInfo> Decode(byte[] input)
    {
        var decoder = new AnsiInputDecoder(Timeout);
        var sink = new Sink();
        decoder.Feed(input, TimeSpan.Zero, sink);
        decoder.Expire(TimeSpan.FromSeconds(1), sink);
        return sink.Keys;
    }

    public static TheoryData<string, ConsoleKeyInfo> PlainBytes => new()
    {
        { "a", K('a', ConsoleKey.A) },
        { "Z", K('Z', ConsoleKey.Z, Shift) },
        { "7", K('7', ConsoleKey.D7) },
        { " ", K(' ', ConsoleKey.Spacebar) },
        { "~", K('~', 0) },
        { "é", K('é', 0) },
        { "日", K('日', 0) },
        { "\r", K('\r', ConsoleKey.Enter) },
        { "\n", K('\r', ConsoleKey.Enter) },
        { "\t", K('\t', ConsoleKey.Tab) },
        { "\x7f", K('\b', ConsoleKey.Backspace) },
        { "\b", K('\b', ConsoleKey.Backspace, Ctrl) },
        { "\0", K('\0', ConsoleKey.Spacebar, Ctrl) },
        { "\x01", K('\x01', ConsoleKey.A, Ctrl) },
        { "\x03", K('\x03', ConsoleKey.C, Ctrl) },
        { "\x1a", K('\x1a', ConsoleKey.Z, Ctrl) },
        { "\x1c", K('\x1c', 0, Ctrl) },
        { "\x1f", K('\x1f', 0, Ctrl) },
    };

    [Theory]
    [MemberData(nameof(PlainBytes))]
    public void PlainBytes_DecodeToOneKey(string input, ConsoleKeyInfo expected)
    {
        Assert.Equal([expected], Decode(input));
    }

    [Fact]
    public void NonBmpCharacter_BecomesASurrogatePair()
    {
        Assert.Equal([K('\uD83D', 0), K('\uDE00', 0)], Decode("😀"));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0xC3, 0x41 })]
    [InlineData(new byte[] { 0x80 })]
    public void InvalidUtf8_BecomesReplacementCharacters(byte[] input)
    {
        var keys = Decode(input);

        Assert.Equal('�', keys[0].KeyChar);
        Assert.DoesNotContain(keys, k => k.Key == ConsoleKey.Escape);
    }

    [Fact]
    public void IncompleteUtf8_IsHeldUntilTheRestArrives()
    {
        var decoder = new AnsiInputDecoder(Timeout);
        var sink = new Sink();
        var bytes = Encoding.UTF8.GetBytes("日");

        decoder.Feed(bytes.AsSpan(0, 1), TimeSpan.Zero, sink);
        Assert.Empty(sink.Keys);
        decoder.Feed(bytes.AsSpan(1), TimeSpan.FromMilliseconds(10), sink);

        Assert.Equal([K('日', 0)], sink.Keys);
    }

    [Fact]
    public void IncompleteUtf8_AtTheTimeout_IsAReplacementCharacter()
    {
        Assert.Equal([K('�', 0)], Decode([0xE6, 0x97]));
    }

    public static TheoryData<string, ConsoleKeyInfo> AltKeys => new()
    {
        { "\x1b" + "a", K('a', ConsoleKey.A, Alt) },
        { "\x1b" + "A", K('A', ConsoleKey.A, Alt | Shift) },
        { "\x1b" + "\x01", K('\x01', ConsoleKey.A, Alt | Ctrl) },
        { "\x1b" + "\x7f", K('\b', ConsoleKey.Backspace, Alt) },
        { "\x1b" + "\r", K('\r', ConsoleKey.Enter, Alt) },
        { "\x1b" + "é", K('é', 0, Alt) },
        { "\x1b" + "[", K('[', 0, Alt) },
        { "\x1b" + "O", K('O', ConsoleKey.O, Alt | Shift) },
        { "\x1b" + "]", K(']', 0, Alt) },
        { "\x1b" + "P", K('P', ConsoleKey.P, Alt | Shift) },
        { "\x1b\x1b[A", N(ConsoleKey.UpArrow, Alt) },
        { "\x1b\x1bOB", N(ConsoleKey.DownArrow, Alt) },
    };

    [Theory]
    [MemberData(nameof(AltKeys))]
    public void EscapePrefix_AddsAlt(string input, ConsoleKeyInfo expected)
    {
        Assert.Equal([expected], Decode(input));
    }

    public static TheoryData<string, ConsoleKeyInfo> Sequences => new()
    {
        { "\x1b[A", N(ConsoleKey.UpArrow) },
        { "\x1b[B", N(ConsoleKey.DownArrow) },
        { "\x1b[C", N(ConsoleKey.RightArrow) },
        { "\x1b[D", N(ConsoleKey.LeftArrow) },
        { "\x1bOA", N(ConsoleKey.UpArrow) },
        { "\x1bOD", N(ConsoleKey.LeftArrow) },
        { "\x1b[H", N(ConsoleKey.Home) },
        { "\x1b[F", N(ConsoleKey.End) },
        { "\x1bOH", N(ConsoleKey.Home) },
        { "\x1bOF", N(ConsoleKey.End) },
        { "\x1bOP", N(ConsoleKey.F1) },
        { "\x1bOS", N(ConsoleKey.F4) },
        { "\x1b[1;2P", N(ConsoleKey.F1, Shift) },
        { "\x1b[1;5R", N(ConsoleKey.F3, Ctrl) },
        { "\x1b[1~", N(ConsoleKey.Home) },
        { "\x1b[7~", N(ConsoleKey.Home) },
        { "\x1b[4~", N(ConsoleKey.End) },
        { "\x1b[8~", N(ConsoleKey.End) },
        { "\x1b[2~", N(ConsoleKey.Insert) },
        { "\x1b[3~", N(ConsoleKey.Delete) },
        { "\x1b[5~", N(ConsoleKey.PageUp) },
        { "\x1b[6~", N(ConsoleKey.PageDown) },
        { "\x1b[11~", N(ConsoleKey.F1) },
        { "\x1b[15~", N(ConsoleKey.F5) },
        { "\x1b[17~", N(ConsoleKey.F6) },
        { "\x1b[21~", N(ConsoleKey.F10) },
        { "\x1b[23~", N(ConsoleKey.F11) },
        { "\x1b[24~", N(ConsoleKey.F12) },
        { "\x1b[[A", N(ConsoleKey.F1) },
        { "\x1b[[E", N(ConsoleKey.F5) },
        { "\x1b[Z", N(ConsoleKey.Tab, Shift) },
        { "\x1b[1;2A", N(ConsoleKey.UpArrow, Shift) },
        { "\x1b[1;3B", N(ConsoleKey.DownArrow, Alt) },
        { "\x1b[1;5C", N(ConsoleKey.RightArrow, Ctrl) },
        { "\x1b[1;6D", N(ConsoleKey.LeftArrow, Ctrl | Shift) },
        { "\x1b[1;7H", N(ConsoleKey.Home, Ctrl | Alt) },
        { "\x1b[1;8F", N(ConsoleKey.End, Ctrl | Alt | Shift) },
        { "\x1b[1;9A", N(ConsoleKey.UpArrow, Alt) },
        { "\x1b[3;5~", N(ConsoleKey.Delete, Ctrl) },
        { "\x1b[5;3~", N(ConsoleKey.PageUp, Alt) },
        { "\x1b[24;2~", N(ConsoleKey.F12, Shift) },
    };

    [Theory]
    [MemberData(nameof(Sequences))]
    public void Sequences_DecodeToNamedKeys(string input, ConsoleKeyInfo expected)
    {
        Assert.Equal([expected], Decode(input));
    }

    [Fact]
    public void NamedKeys_MatchKeyBindings()
    {
        Assert.True(KeyBinding.Parse("ctrl+right").Matches(Decode("\x1b[1;5C")[0]));
        Assert.True(KeyBinding.Parse("shift+tab").Matches(Decode("\x1b[Z")[0]));
        Assert.True(KeyBinding.Parse("alt+x").Matches(Decode("\x1bx")[0]));
        Assert.True(KeyBinding.Parse("ctrl+c").Matches(Decode("\x03")[0]));
        Assert.True(KeyBinding.Parse("esc").Matches(Decode("\x1b")[0]));
        Assert.True(KeyBinding.Parse("f5").Matches(Decode("\x1b[15~")[0]));
    }

    [Theory]
    [InlineData("\x1b[99~")]          // unknown tilde code
    [InlineData("\x1b[200~")]         // paste markers (Phase 9)
    [InlineData("\x1b[201~")]
    [InlineData("\x1b[1;2;3A")]       // too many parameters
    [InlineData("\x1b[q")]            // unknown final
    [InlineData("\x1b[ q")]           // intermediate byte
    [InlineData("\x1b[<0;10;5M")]     // SGR mouse (Phase 10)
    [InlineData("\x1b[<64;1;1m")]
    [InlineData("\x1b[?1;2c")]        // a device attributes reply
    [InlineData("\x1bOx")]            // unknown SS3
    [InlineData("\x1b]11;rgb:0000/0000/0000\x07")] // OSC ended by BEL
    [InlineData("\x1b]0;title\x1b\\")]              // OSC ended by ST
    [InlineData("\x1bPdata\x1b\\")]                 // DCS
    [InlineData("\x1b[I")]            // focus in
    public void UnknownSequences_AreDroppedWhole(string sequence)
    {
        Assert.Equal([K('x', ConsoleKey.X)], Decode(sequence + "x"));
    }

    [Fact]
    public void OverlongCsi_IsDropped_AndDecodingResumes()
    {
        var keys = Decode("\x1b[" + new string('1', 100) + "Ax");

        Assert.DoesNotContain(keys, k => k.Key == ConsoleKey.Escape);
        Assert.Equal(K('x', ConsoleKey.X), keys[^1]);
    }

    [Fact]
    public void MalformedCsi_ResumesAtTheBadByte()
    {
        Assert.Equal([K('é', 0)], Decode("\x1b[1é"));
    }

    [Fact]
    public void LoneEscape_ResolvesExactlyAtTheDeadline()
    {
        var decoder = new AnsiInputDecoder(Timeout);
        var sink = new Sink();

        decoder.Feed("\x1b"u8, TimeSpan.FromMilliseconds(100), sink);
        Assert.Equal(TimeSpan.FromMilliseconds(150), decoder.Deadline);
        decoder.Expire(TimeSpan.FromMilliseconds(149), sink);
        Assert.Empty(sink.Keys);
        decoder.Expire(TimeSpan.FromMilliseconds(150), sink);

        Assert.Equal([K('\x1b', ConsoleKey.Escape)], sink.Keys);
        Assert.Null(decoder.Deadline);
    }

    [Fact]
    public void ByteBeforeTheDeadline_ContinuesTheSequence()
    {
        var decoder = new AnsiInputDecoder(Timeout);
        var sink = new Sink();

        decoder.Feed("\x1b"u8, TimeSpan.Zero, sink);
        decoder.Feed("["u8, TimeSpan.FromMilliseconds(40), sink);
        decoder.Expire(TimeSpan.FromMilliseconds(60), sink);   // 20 ms after the last byte: still held
        decoder.Feed("A"u8, TimeSpan.FromMilliseconds(80), sink);

        Assert.Equal([N(ConsoleKey.UpArrow)], sink.Keys);
    }

    [Fact]
    public void IncompleteCsi_AtTheTimeout_IsEscapeThenItsBytes()
    {
        Assert.Equal(
            [K('\x1b', ConsoleKey.Escape), K('[', 0), K('1', ConsoleKey.D1), K(';', 0)],
            Decode("\x1b[1;"));
    }

    [Fact]
    public void DoubleEscape_IsTwoEscapes()
    {
        Assert.Equal([K('\x1b', ConsoleKey.Escape), K('\x1b', ConsoleKey.Escape)], Decode("\x1b\x1b"));
        Assert.Equal([K('\x1b', ConsoleKey.Escape), K('a', ConsoleKey.A, Alt)], Decode("\x1b\x1b" + "a"));
    }

    [Fact]
    public void ZeroTimeout_ResolvesAtTheEndOfEachRead()
    {
        var decoder = new AnsiInputDecoder(TimeSpan.Zero);
        var sink = new Sink();

        decoder.Feed("\x1b"u8, TimeSpan.Zero, sink);

        Assert.Equal([K('\x1b', ConsoleKey.Escape)], sink.Keys);
        Assert.Null(decoder.Deadline);
    }

    [Fact]
    public void ArrowBurst_SplitAtEveryOffset_HasNoStrayEscape()
    {
        var burst = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("\x1b[A", 1000)));
        var up = N(ConsoleKey.UpArrow);

        for (var split = 0; split <= burst.Length; split++)
        {
            var decoder = new AnsiInputDecoder(Timeout);
            var sink = new CountingUpSink(up);
            decoder.Feed(burst.AsSpan(0, split), TimeSpan.Zero, sink);
            decoder.Feed(burst.AsSpan(split), TimeSpan.FromMilliseconds(1), sink);
            decoder.Expire(TimeSpan.FromSeconds(1), sink);

            Assert.True(sink.Ups == 1000 && sink.Others == 0, $"split at {split}: {sink.Ups} arrows, {sink.Others} other keys");
        }
    }

    private sealed class CountingUpSink(ConsoleKeyInfo up) : IInputSink
    {
        public int Ups, Others;
        public void Key(ConsoleKeyInfo key)
        {
            if (key == up) Ups++;
            else Others++;
        }
    }

    [Fact]
    public void RoundTrip_RandomKeysAndRandomSplits()
    {
        var random = new Random(8_2026);
        for (var c = 0; c < 10_000; c++)
        {
            var expected = new List<ConsoleKeyInfo>();
            var bytes = new List<byte>();
            var count = random.Next(1, 20);
            for (var i = 0; i < count; i++) XtermEncoder.AddRandomKey(random, expected, bytes);

            var decoder = new AnsiInputDecoder(Timeout);
            var sink = new Sink();
            var data = bytes.ToArray();
            var offset = 0;
            var now = TimeSpan.Zero;
            while (offset < data.Length)
            {
                var length = random.Next(1, Math.Min(8, data.Length - offset) + 1);
                decoder.Feed(data.AsSpan(offset, length), now, sink);
                offset += length;
                now += TimeSpan.FromMilliseconds(random.Next(0, 49));
                decoder.Expire(now, sink);
            }
            decoder.Expire(now + TimeSpan.FromSeconds(1), sink);

            Assert.True(expected.SequenceEqual(sink.Keys),
                $"case {c}: {Convert.ToHexString(data)}\nexpected {string.Join(", ", expected.Select(Describe))}\nactual   {string.Join(", ", sink.Keys.Select(Describe))}");
        }
    }

    private static string Describe(ConsoleKeyInfo k) => $"{k.Key}/U+{(int)k.KeyChar:X4}/{k.Modifiers}";

    [Fact]
    public void Decoding_AllocatesNothingPerKey()
    {
        var random = new Random(42);
        var bytes = new List<byte>();
        var keys = new List<ConsoleKeyInfo>();
        while (bytes.Count < 1_000_000) XtermEncoder.AddRandomKey(random, keys, bytes);
        var data = bytes.ToArray();
        var decoder = new AnsiInputDecoder(Timeout);
        var sink = new CountingSink();
        FeedAll(); // the first pass JIT-compiles the decoder, which allocates
        sink.Count = 0;

        var before = GC.GetAllocatedBytesForCurrentThread();
        FeedAll();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(keys.Count, sink.Count);
        Assert.True(allocated < 1024, $"allocated {allocated} bytes for {sink.Count} keys");

        void FeedAll()
        {
            for (var offset = 0; offset < data.Length; offset += 4096)
                decoder.Feed(data.AsSpan(offset, Math.Min(4096, data.Length - offset)), TimeSpan.Zero, sink);
        }
    }
}

/// <summary>Encodes keys the way xterm sends them, for the decoder's round-trip tests.</summary>
internal static class XtermEncoder
{
    private static readonly (ConsoleKey Key, char Final)[] LetterKeys =
    [
        (ConsoleKey.UpArrow, 'A'), (ConsoleKey.DownArrow, 'B'), (ConsoleKey.RightArrow, 'C'), (ConsoleKey.LeftArrow, 'D'),
        (ConsoleKey.Home, 'H'), (ConsoleKey.End, 'F'),
    ];

    private static readonly (ConsoleKey Key, int Code)[] TildeKeys =
    [
        (ConsoleKey.Insert, 2), (ConsoleKey.Delete, 3), (ConsoleKey.PageUp, 5), (ConsoleKey.PageDown, 6),
        (ConsoleKey.F5, 15), (ConsoleKey.F6, 17), (ConsoleKey.F7, 18), (ConsoleKey.F8, 19), (ConsoleKey.F9, 20),
        (ConsoleKey.F10, 21), (ConsoleKey.F11, 23), (ConsoleKey.F12, 24),
    ];

    private static readonly string[] Texts = ["é", "日", "ü", "😀", "👍", "€"];

    public static void AddRandomKey(Random random, List<ConsoleKeyInfo> keys, List<byte> bytes)
    {
        var alt = random.Next(4) == 0;
        switch (random.Next(8))
        {
            case 0:
            case 1:
            {
                // Printable ASCII. Alt+[, Alt+O, Alt+] and Alt+P start sequences, so they're only unambiguous alone.
                var c = (char)random.Next(0x20, 0x7F);
                if (c is '[' or 'O' or ']' or 'P') alt = false;
                if (alt) bytes.Add(0x1B);
                bytes.Add((byte)c);
                keys.Add(Ascii(c, alt));
                break;
            }
            case 2:
            {
                // UTF-8 text, one key per UTF-16 unit
                var text = Texts[random.Next(Texts.Length)];
                if (alt && text.Length == 1) bytes.Add(0x1B);
                else alt = false;
                bytes.AddRange(Encoding.UTF8.GetBytes(text));
                foreach (var c in text) keys.Add(new ConsoleKeyInfo(c, 0, false, alt, false));
                break;
            }
            case 3:
            {
                // Ctrl+letter, skipping the letters that are Tab, Enter and Ctrl+Backspace
                char letter;
                do letter = (char)random.Next('a', 'z' + 1); while (letter is 'h' or 'i' or 'j' or 'm');
                if (alt) bytes.Add(0x1B);
                var b = (byte)(letter - 'a' + 1);
                bytes.Add(b);
                keys.Add(new ConsoleKeyInfo((char)b, ConsoleKey.A + (letter - 'a'), false, alt, true));
                break;
            }
            case 4:
            {
                var (key, ch, b) = random.Next(4) switch
                {
                    0 => (ConsoleKey.Enter, '\r', (byte)0x0D),
                    1 => (ConsoleKey.Tab, '\t', (byte)0x09),
                    2 => (ConsoleKey.Backspace, '\b', (byte)0x7F),
                    _ => (ConsoleKey.Spacebar, ' ', (byte)0x20),
                };
                if (alt) bytes.Add(0x1B);
                bytes.Add(b);
                keys.Add(new ConsoleKeyInfo(ch, key, false, alt, false));
                break;
            }
            case 5:
            {
                var (key, final) = LetterKeys[random.Next(LetterKeys.Length)];
                var mods = random.Next(8);
                bytes.AddRange(Encoding.ASCII.GetBytes(mods == 0 ? $"\x1b[{final}" : $"\x1b[1;{mods + 1}{final}"));
                keys.Add(Named(key, '\0', mods));
                break;
            }
            case 6:
            {
                var (key, code) = TildeKeys[random.Next(TildeKeys.Length)];
                var mods = random.Next(8);
                bytes.AddRange(Encoding.ASCII.GetBytes(mods == 0 ? $"\x1b[{code}~" : $"\x1b[{code};{mods + 1}~"));
                keys.Add(Named(key, '\0', mods));
                break;
            }
            default:
            {
                var n = random.Next(4);
                var mods = random.Next(8);
                bytes.AddRange(Encoding.ASCII.GetBytes(mods == 0 ? $"\x1bO{(char)('P' + n)}" : $"\x1b[1;{mods + 1}{(char)('P' + n)}"));
                keys.Add(Named(ConsoleKey.F1 + n, '\0', mods));
                break;
            }
        }
    }

    private static ConsoleKeyInfo Named(ConsoleKey key, char c, int mods) =>
        new(c, key, (mods & 1) != 0, (mods & 2) != 0, (mods & 4) != 0);

    private static ConsoleKeyInfo Ascii(char c, bool alt) => c switch
    {
        ' ' => new(' ', ConsoleKey.Spacebar, false, alt, false),
        >= 'a' and <= 'z' => new(c, ConsoleKey.A + (c - 'a'), false, alt, false),
        >= 'A' and <= 'Z' => new(c, ConsoleKey.A + (c - 'A'), true, alt, false),
        >= '0' and <= '9' => new(c, ConsoleKey.D0 + (c - '0'), false, alt, false),
        _ => new(c, 0, false, alt, false),
    };
}
