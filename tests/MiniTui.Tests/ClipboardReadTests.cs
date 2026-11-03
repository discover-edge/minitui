using System.Text;

namespace MiniTui.Tests;

/// <summary>Reading the clipboard through OSC 52, and the Insert keys (1.x spec §20.3, §20.4).</summary>
[Collection("Console")]
public sealed class ClipboardReadTests : IDisposable
{
    public ClipboardReadTests() => Clipboard.Reset();

    public void Dispose() => Clipboard.Reset();

    private sealed class Sink : IInputSink
    {
        public List<ConsoleKeyInfo> Keys { get; } = [];
        public List<TerminalReply> Replies { get; } = [];
        public void Key(ConsoleKeyInfo key) => Keys.Add(key);
        public void Paste(string text) { }
        public void Mouse(MouseEvent mouse) { }
        public void Reply(TerminalReply reply) => Replies.Add(reply);
    }

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static Sink Decode(string input)
    {
        var sink = new Sink();
        var decoder = new AnsiInputDecoder(TimeSpan.FromMilliseconds(50));
        decoder.Feed(Encoding.UTF8.GetBytes(input), TimeSpan.Zero, sink);
        decoder.Expire(TimeSpan.FromMilliseconds(100), sink);
        return sink;
    }

    private static string Typed(Sink sink) => new([.. sink.Keys.Select(k => k.KeyChar)]);

    // The reply

    [Theory]
    [InlineData("\a")]
    [InlineData("\e\\")]
    public void AnOsc52Reply_IsDecoded_NeverAsKeys(string end)
    {
        var sink = Decode("a\e]52;c;" + B64("héllo\nworld") + end + "b");

        Assert.Equal([new TerminalReply.ClipboardText("héllo\nworld")], sink.Replies);
        Assert.Equal("ab", Typed(sink));
    }

    [Fact]
    public void AReplySplitAtEveryByte_IsReassembled()
    {
        var bytes = Encoding.UTF8.GetBytes("a\e]52;c;" + B64("split") + "\e\\b");
        for (var cut = 1; cut < bytes.Length; cut++)
        {
            var sink = new Sink();
            var decoder = new AnsiInputDecoder(TimeSpan.FromMilliseconds(50));
            decoder.Feed(bytes.AsSpan(0, cut), TimeSpan.Zero, sink);
            decoder.Feed(bytes.AsSpan(cut), TimeSpan.Zero, sink);
            Assert.Equal([new TerminalReply.ClipboardText("split")], sink.Replies);
            Assert.Equal("ab", Typed(sink));
        }
    }

    [Fact]
    public void ALongReply_IsKeptWhole()
    {
        var text = new string('x', 100_000);

        var sink = Decode("\e]52;c;" + B64(text) + "\a");

        Assert.Equal(text, Assert.IsType<TerminalReply.ClipboardText>(Assert.Single(sink.Replies)).Text);
        Assert.Empty(sink.Keys);
    }

    [Fact]
    public void AnEmptyReply_IsEmptyText_AndInvalidBase64_IsDropped()
    {
        Assert.Equal([new TerminalReply.ClipboardText("")], Decode("\e]52;c;\a").Replies);

        var invalid = Decode("\e]52;c;!!!\ax");
        Assert.Empty(invalid.Replies);
        Assert.Equal("x", Typed(invalid));
    }

    [Fact]
    public void ABrokenReply_IsAbandoned_AndWhatFollowsIsTyped()
    {
        // A control character can't be in a reply
        var sink = Decode("\e]52;c;abc\rq");

        Assert.Empty(sink.Replies);
        Assert.Equal("\rq", Typed(sink));
    }

    [Fact]
    public void AnUnterminatedReply_IsAbandonedAfterASecond()
    {
        var sink = new Sink();
        var decoder = new AnsiInputDecoder(TimeSpan.FromMilliseconds(50));
        decoder.Feed("\e]52;c;abc"u8, TimeSpan.Zero, sink);
        decoder.Expire(TimeSpan.FromMilliseconds(500), sink);
        Assert.Equal(TimeSpan.FromSeconds(1), decoder.Deadline);

        decoder.Expire(TimeSpan.FromSeconds(1), sink);
        decoder.Feed("q"u8, TimeSpan.FromSeconds(2), sink);

        Assert.Empty(sink.Replies);
        Assert.Equal("q", Typed(sink));
    }

    // Reading through the app

    private sealed class Field : Screen
    {
        public TextFieldState State { get; } = new();
        public override bool CapturesText => true;
        public override bool HandleKey(ConsoleKeyInfo key) => State.HandleKey(key);
        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) => Widget.TextField().State(State).Focused().Draw(buf, area);
    }

    private static ConsoleKeyInfo Key(string binding) => KeyBinding.Parse(binding).ToKeyInfo();

    [Theory]
    [InlineData("ctrl+v")]
    [InlineData("shift+insert")]
    public void Paste_WaitsForTheTerminalsReply(string key)
    {
        var field = new Field();
        using var harness = new ComponentHarness(field, 20, 3);
        harness.RunUntilIdle();
        var queries = 0;
        harness.App.UseTerminalClipboard(() => { queries++; return true; });
        field.State.Text = "ab";

        harness.App.InjectKeys([Key(key)]);
        harness.App.InjectReply(new TerminalReply.ClipboardText("XY"));
        harness.RunUntilIdle();

        Assert.Equal(1, queries);
        Assert.Equal("abXY", field.State.Text);
    }

    [Fact]
    public void ARefusal_PastesTheInAppText_AndTheAppKeepsAsking()
    {
        var field = new Field();
        using var harness = new ComponentHarness(field, 20, 3);
        harness.RunUntilIdle();
        var queries = 0;
        harness.App.UseTerminalClipboard(() => { queries++; return true; });
        Clipboard.SetText("mine");

        for (var i = 0; i < 2; i++)
        {
            harness.App.InjectKeys([Key("ctrl+v")]);
            harness.App.InjectReply(new TerminalReply.ClipboardText(""));
            harness.RunUntilIdle();
        }

        Assert.Equal(2, queries);
        Assert.Equal("minemine", field.State.Text);
    }

    [Fact]
    public void NoReply_FallsBackAfterTheWait_ThenStopsAsking()
    {
        var field = new Field();
        using var harness = new ComponentHarness(field, 20, 3);
        harness.RunUntilIdle();
        var queries = 0;
        harness.App.UseTerminalClipboard(() => { queries++; return true; });
        Clipboard.SetText("in-app");

        harness.Press("ctrl+v");
        harness.Press("ctrl+v");

        Assert.Equal(1, queries);
        Assert.Equal("in-appin-app", field.State.Text);
    }

    [Fact]
    public async Task WithoutATerminalRead_GetTextAsync_IsGetText()
    {
        Clipboard.SetText("plain");

        Assert.Equal("plain", await Clipboard.GetTextAsync());
    }

    // The Insert keys

    [Fact]
    public void InATextField_CtrlInsertCopies_ShiftInsertPastes_AndShiftDeleteCuts()
    {
        var state = new TextFieldState { Text = "hello" };
        state.Select(0, 2);

        Assert.True(state.HandleKey(Key("ctrl+insert")));
        Assert.Equal("he", Clipboard.GetText());
        Assert.True(state.HandleKey(Key("shift+delete")));
        Assert.Equal("llo", state.Text);
        state.Cursor = 3;
        Assert.True(state.HandleKey(Key("shift+insert")));
        Assert.Equal("llohe", state.Text);
    }

    [Fact]
    public void CtrlInsert_WithoutASelection_IsntHandled_AndShiftDelete_DeletesAsDelete()
    {
        var state = new TextFieldState { Text = "abc" };
        state.Cursor = 1;

        Assert.False(state.HandleKey(Key("ctrl+insert")));
        Assert.True(state.HandleKey(Key("shift+delete")));
        Assert.Equal("ac", state.Text);
    }

    [Fact]
    public void InATextArea_TheInsertKeysWorkToo()
    {
        var state = new TextAreaState("one\ntwo");
        state.Select(new TextPosition(0, 0), new TextPosition(1, 1));

        Assert.True(state.HandleKey(Key("ctrl+insert")));
        Assert.Equal("one\nt", Clipboard.GetText());
        Assert.True(state.HandleKey(Key("shift+delete")));
        Assert.Equal("wo", state.Text);
        Assert.True(state.HandleKey(Key("shift+insert")));
        Assert.Equal("one\ntwo", state.Text);
    }

    [Fact]
    public void CtrlInsert_NeverQuits()
    {
        var field = new Field();
        using var harness = new ComponentHarness(field, 20, 3);
        harness.RunUntilIdle();

        harness.Press("ctrl+insert");

        Assert.True(harness.App.IsRunning);
    }
}
