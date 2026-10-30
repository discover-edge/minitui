using System.Runtime.CompilerServices;
using System.Text;

namespace MiniTui.Tests;

/// <summary>Windows VT input: record translation and the conhost Ctrl+V paste (1.x spec §4.4–4.5).</summary>
public class WindowsInputTests
{
    private const uint LeftCtrl = 0x8;

    private sealed class Sink : IInputSink
    {
        public List<object> Events { get; } = [];
        public void Key(ConsoleKeyInfo key) => Events.Add(key);
        public void Paste(string text) => Events.Add(text);
    }

    private static InputRecord[] Chars(string text) => [.. text.Select(c => InputRecord.Key(c))];

    private static List<object> Decode(params InputRecord[] records)
    {
        var bytes = new WindowsInputTranslator().Translate(records, out _).ToArray();
        var decoder = new AnsiInputDecoder(TimeSpan.Zero);
        var sink = new Sink();
        decoder.Feed(bytes, TimeSpan.Zero, sink);
        decoder.Expire(TimeSpan.Zero, sink);
        return sink.Events;
    }

    [Fact]
    public void InputRecord_HasTheWin32Layout()
    {
        Assert.Equal(20, Unsafe.SizeOf<InputRecord>());
    }

    [Fact]
    public void KeyDowns_BecomeUtf8_AndKeyUpsAndBareModifiersAreSkipped()
    {
        var records = new[]
        {
            InputRecord.Key('a'),
            InputRecord.Key('a', down: false),
            InputRecord.Key('\0', virtualKey: 0x10), // Shift alone
            InputRecord.Key('é'),
            InputRecord.Key('日'),
        };

        var bytes = new WindowsInputTranslator().Translate(records, out var resized).ToArray();

        Assert.Equal("aé日", Encoding.UTF8.GetString(bytes));
        Assert.False(resized);
    }

    [Fact]
    public void RepeatCounts_Expand()
    {
        var bytes = new WindowsInputTranslator().Translate([InputRecord.Key('x', repeat: 3)], out _).ToArray();

        Assert.Equal("xxx", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void ASurrogatePairSplitAcrossReads_Joins()
    {
        var translator = new WindowsInputTranslator();
        var emoji = "😀";

        var first = translator.Translate([InputRecord.Key(emoji[0])], out _).ToArray();
        var second = translator.Translate([InputRecord.Key(emoji[1])], out _).ToArray();

        Assert.Empty(first);
        Assert.Equal(emoji, Encoding.UTF8.GetString(second));
    }

    [Theory]
    [InlineData(new[] { '\uD83D', 'a' }, "�a")]
    [InlineData(new[] { '\uDE00', 'a' }, "�a")]
    public void LoneSurrogates_BecomeReplacementCharacters(char[] chars, string expected)
    {
        var bytes = new WindowsInputTranslator().Translate([.. chars.Select(c => InputRecord.Key(c))], out _).ToArray();

        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void CtrlSpace_IsNul()
    {
        var bytes = new WindowsInputTranslator().Translate([InputRecord.Key('\0', virtualKey: 0x20, controlKeyState: LeftCtrl)], out _).ToArray();

        Assert.Equal([0], bytes);
    }

    [Fact]
    public void AWindowSizeEvent_ReportsAResize()
    {
        new WindowsInputTranslator().Translate([InputRecord.Key('a'), InputRecord.Resize()], out var resized);

        Assert.True(resized);
    }

    [Fact]
    public void VtSequences_DecodeAsKeys()
    {
        var events = Decode([.. Chars("\e[A"), .. Chars("\ex"), .. Chars("\e[15~"), .. Chars("\e[1;5C"), InputRecord.Key('\x03')]);

        Assert.Equal(
            [
                new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false),
                new ConsoleKeyInfo('x', ConsoleKey.X, false, true, false),
                new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false),
                new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, true),
                new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true),
            ],
            events);
    }

    [Fact]
    public void ABracketedPaste_FromRecords_IsOnePaste()
    {
        var events = Decode(Chars("\e[200~one\rtwo\e[201~"));

        Assert.Equal(["one\ntwo"], events);
    }

    [Fact]
    public void CtrlV_PastesTheClipboard_WithLineBreaksNormalized()
    {
        var sink = new Sink();
        var clipboard = new ClipboardPasteSink(sink, () => "a\r\nb");

        clipboard.Key(new ConsoleKeyInfo('\x16', ConsoleKey.V, false, false, true));

        Assert.Equal(["a\nb"], sink.Events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CtrlV_WithNoClipboardText_IsAKey(string? text)
    {
        var sink = new Sink();
        var clipboard = new ClipboardPasteSink(sink, () => text);
        var ctrlV = new ConsoleKeyInfo('\x16', ConsoleKey.V, false, false, true);

        clipboard.Key(ctrlV);

        Assert.Equal([ctrlV], sink.Events);
    }

    [Fact]
    public void OtherKeys_AndCtrlAltV_DontReadTheClipboard()
    {
        var sink = new Sink();
        var reads = 0;
        var clipboard = new ClipboardPasteSink(sink, () => { reads++; return "clip"; });

        clipboard.Key(new ConsoleKeyInfo('v', ConsoleKey.V, false, false, false));
        clipboard.Key(new ConsoleKeyInfo('\x16', ConsoleKey.V, false, true, true));

        Assert.Equal(0, reads);
        Assert.Equal(2, sink.Events.Count);
    }

    [Fact]
    public void CtrlV_InsideABracketedPaste_IsText()
    {
        var sink = new Sink();
        var decoder = new AnsiInputDecoder(TimeSpan.Zero);

        decoder.Feed("\e[200~a\x16b\e[201~"u8, TimeSpan.Zero, new ClipboardPasteSink(sink, () => "clip"));

        Assert.Equal(["a\x16b"], sink.Events);
    }
}
