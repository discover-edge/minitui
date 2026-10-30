using System.Runtime.CompilerServices;

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
        public void Mouse(MouseEvent mouse) => Events.Add(mouse);
    }

    private static InputRecord[] Chars(string text) => [.. text.Select(c => InputRecord.Key(c))];

    private static WindowsInputTranslator Translator(Vec2 origin = default) => new(() => origin);

    private static List<object> Decode(params InputRecord[] records) => Decode(Translator(), records);

    private static List<object> Decode(WindowsInputTranslator translator, params InputRecord[] records)
    {
        var decoder = new AnsiInputDecoder(TimeSpan.Zero);
        var sink = new Sink();
        translator.Translate(records, decoder, TimeSpan.Zero, sink, out _);
        decoder.Expire(TimeSpan.Zero, sink);
        return sink.Events;
    }

    private static string Text(List<object> events) => string.Concat(events.Cast<ConsoleKeyInfo>().Select(k => k.KeyChar));

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

        Assert.Equal("aé日", Text(Decode(records)));
    }

    [Fact]
    public void RepeatCounts_Expand()
    {
        Assert.Equal("xxx", Text(Decode(InputRecord.Key('x', repeat: 3))));
    }

    [Fact]
    public void ASurrogatePairSplitAcrossReads_Joins()
    {
        var translator = Translator();
        var emoji = "😀";

        var first = Decode(translator, InputRecord.Key(emoji[0]));
        var second = Decode(translator, InputRecord.Key(emoji[1]));

        Assert.Empty(first);
        Assert.Equal(emoji, Text(second));
    }

    [Theory]
    [InlineData(new[] { '\uD83D', 'a' }, "�a")]
    [InlineData(new[] { '\uDE00', 'a' }, "�a")]
    public void LoneSurrogates_BecomeReplacementCharacters(char[] chars, string expected)
    {
        Assert.Equal(expected, Text(Decode([.. chars.Select(c => InputRecord.Key(c))])));
    }

    [Fact]
    public void CtrlSpace_IsNul()
    {
        var events = Decode(InputRecord.Key('\0', virtualKey: 0x20, controlKeyState: LeftCtrl));

        Assert.Equal([new ConsoleKeyInfo('\0', ConsoleKey.Spacebar, false, false, true)], events);
    }

    [Fact]
    public void AWindowSizeEvent_ReportsAResize()
    {
        Translator().Translate([InputRecord.Key('a'), InputRecord.Resize()], new AnsiInputDecoder(TimeSpan.Zero), TimeSpan.Zero, new Sink(), out var resized);
        Translator().Translate([InputRecord.Key('a')], new AnsiInputDecoder(TimeSpan.Zero), TimeSpan.Zero, new Sink(), out var notResized);

        Assert.True(resized);
        Assert.False(notResized);
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

    [Fact]
    public void MouseRecords_GivePressesAndReleasesFromButtonChanges()
    {
        var events = Decode(
            InputRecord.Mouse(3, 4, buttons: 0x1),
            InputRecord.Mouse(3, 4, buttons: 0x1 | 0x2),
            InputRecord.Mouse(3, 4, buttons: 0x2),
            InputRecord.Mouse(3, 4, buttons: 0));

        Assert.Equal(
            [
                new MouseEvent(MouseAction.Press, MouseButton.Left, 3, 4),
                new MouseEvent(MouseAction.Press, MouseButton.Right, 3, 4),
                new MouseEvent(MouseAction.Release, MouseButton.Left, 3, 4),
                new MouseEvent(MouseAction.Release, MouseButton.Right, 3, 4),
            ],
            events);
    }

    [Fact]
    public void MouseRecords_WheelDirection_ComesFromTheHighWord()
    {
        var events = Decode(
            InputRecord.Mouse(1, 1, buttons: 120u << 16, flags: 0x4),
            InputRecord.Mouse(1, 1, buttons: unchecked((uint)(-120 << 16)), flags: 0x4),
            InputRecord.Mouse(1, 1, buttons: 120u << 16, flags: 0x8),
            InputRecord.Mouse(1, 1, buttons: unchecked((uint)(-120 << 16)), flags: 0x8));

        Assert.Equal(
            [MouseAction.ScrollUp, MouseAction.ScrollDown, MouseAction.ScrollRight, MouseAction.ScrollLeft],
            events.Cast<MouseEvent>().Select(m => m.Action));
    }

    [Fact]
    public void MouseRecords_AreWindowRelative_WithModifiers_AndMovesAreDropped()
    {
        var events = Decode(Translator(new Vec2(0, 100)),
            InputRecord.Mouse(5, 101, buttons: 0x1, flags: 0x1), // a move with the button down: dropped
            InputRecord.Mouse(5, 102, buttons: 0x4, controlKeyState: 0x10 | 0x8));

        Assert.Equal([new MouseEvent(MouseAction.Press, MouseButton.Middle, 5, 2, ConsoleModifiers.Shift | ConsoleModifiers.Control)], events);
    }

    [Fact]
    public void MouseRecords_StayInOrderWithKeys()
    {
        var events = Decode(InputRecord.Key('a'), InputRecord.Mouse(0, 0, buttons: 0x1, flags: 0x2), InputRecord.Key('b'));

        Assert.Equal('a', ((ConsoleKeyInfo)events[0]).KeyChar);
        Assert.Equal(new MouseEvent(MouseAction.Press, MouseButton.Left, 0, 0), events[1]); // a double click is a press
        Assert.Equal('b', ((ConsoleKeyInfo)events[2]).KeyChar);
    }
}
