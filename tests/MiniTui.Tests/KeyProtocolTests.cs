using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace MiniTui.Tests;

/// <summary>The keyboard protocols: decoding, choosing one, and turning it on and off (1.x spec §12.5).</summary>
public class KeyProtocolTests
{
    private sealed class Sink : IInputSink
    {
        public List<ConsoleKeyInfo> Keys { get; } = [];
        public List<TerminalReply> Replies { get; } = [];
        public void Key(ConsoleKeyInfo key) => Keys.Add(key);
        public void Paste(string text) { }
        public void Mouse(MouseEvent mouse) { }
        public void Reply(TerminalReply reply) => Replies.Add(reply);
    }

    private static Sink Feed(string input, bool expire = true)
    {
        var sink = new Sink();
        var decoder = new AnsiInputDecoder(TimeSpan.FromMilliseconds(50));
        decoder.Feed(Encoding.UTF8.GetBytes(input), TimeSpan.Zero, sink);
        if (expire) decoder.Expire(TimeSpan.FromSeconds(10), sink);
        return sink;
    }

    private static List<ConsoleKeyInfo> Keys(string input) => Feed(input).Keys;

    private static ConsoleKeyInfo Key(ConsoleKey key, char c, ConsoleModifiers mods = 0) =>
        new(c, key, (mods & ConsoleModifiers.Shift) != 0, (mods & ConsoleModifiers.Alt) != 0, (mods & ConsoleModifiers.Control) != 0);

    private const ConsoleModifiers Ctrl = ConsoleModifiers.Control;
    private const ConsoleModifiers Alt = ConsoleModifiers.Alt;
    private const ConsoleModifiers Shift = ConsoleModifiers.Shift;

    // ---- Same keys as the legacy encoding ----

    // Each row: the legacy bytes, then the same key in kitty, modifyOtherKeys and win32-input-mode
    public static TheoryData<string, string, string?, string?> SameAsLegacy => new()
    {
        { "a", "\e[97u", null, "\e[65;30;97;1;0;1_" },
        { "A", "\e[97;2u", null, "\e[65;30;65;1;16;1_" },
        { "\x01", "\e[97;5u", "\e[27;5;97~", "\e[65;30;1;1;8;1_" },
        { "\x03", "\e[99;5u", "\e[27;5;99~", "\e[67;46;3;1;8;1_" },
        { "\x1a", "\e[122;5u", "\e[27;5;122~", "\e[90;44;26;1;4;1_" },
        { "\x01", "\e[97;6u", null, null }, // Ctrl+Shift+a: legacy can't tell the Shift, so only the key is compared below
        { "\ea", "\e[97;3u", "\e[27;3;97~", "\e[65;30;97;1;2;1_" },
        { "\eA", "\e[97;4u", null, "\e[65;30;65;1;18;1_" },
        { "\x1b", "\e[27u", null, "\e[27;1;27;1;0;1_" },
        { "\r", "\e[13u", null, "\e[13;28;13;1;0;1_" },
        { "\e\r", "\e[13;3u", "\e[27;3;13~", "\e[13;28;13;1;2;1_" },
        { "\t", "\e[9u", null, "\e[9;15;9;1;0;1_" },
        { "\e[Z", "\e[9;2u", null, "\e[9;15;9;1;16;1_" },
        { "\x7f", "\e[127u", null, "\e[8;14;8;1;0;1_" },
        { "\x08", "\e[127;5u", null, "\e[8;14;127;1;8;1_" },
        { " ", "\e[32u", null, "\e[32;57;32;1;0;1_" },
        { "\0", "\e[32;5u", "\e[27;5;32~", "\e[32;57;32;1;8;1_" },
        { "5", "\e[53u", null, "\e[53;6;53;1;0;1_" },
        { "\x1c", "\e[92;5u", "\e[27;5;92~", null },
        { "\x1d", "\e[93;5u", "\e[27;5;93~", null },
        { "é", "\e[233u", null, "\e[0;0;233;1;0;1_" },
    };

    [Theory]
    [MemberData(nameof(SameAsLegacy))]
    public void KeysTheLegacyEncodingCanExpress_DecodeTheSameWay(string legacy, string kitty, string? modifyOtherKeys, string? win32)
    {
        var expected = Assert.Single(Keys(legacy));
        var shiftOnlyDiffers = kitty == "\e[97;6u";
        foreach (var form in new[] { kitty, modifyOtherKeys, win32 })
        {
            if (form is null) continue;
            var key = Assert.Single(Keys(form));
            if (shiftOnlyDiffers) Assert.Equal((expected.Key, expected.KeyChar, expected.Modifiers | Shift), (key.Key, key.KeyChar, key.Modifiers));
            else Assert.Equal(expected, key);
        }
    }

    [Fact]
    public void CtrlCAndCtrlZ_StillQuitAndSuspend()
    {
        Assert.True(KeyInput.IsCtrlC(Assert.Single(Keys("\e[99;5u"))));
        Assert.True(KeyInput.IsCtrlZ(Assert.Single(Keys("\e[122;5u"))));
        Assert.True(KeyInput.IsCtrlC(Assert.Single(Keys("\e[67;46;3;1;8;1_"))));
    }

    [Fact]
    public void Escape_InKittyForm_NeedsNoTimeout()
    {
        var sink = Feed("\e[27u", expire: false);
        Assert.Equal([Key(ConsoleKey.Escape, '\x1b')], sink.Keys);
    }

    // ---- New keys ----

    public static TheoryData<string, string> NewKeys => new()
    {
        { "\e[13;5u", "ctrl+enter" },
        { "\e[27;5;13~", "ctrl+enter" },
        { "\e[13;28;10;1;8;1_", "ctrl+enter" },
        { "\e[13;2u", "shift+enter" },
        { "\e[13;28;13;1;16;1_", "shift+enter" },
        { "\e[9;5u", "ctrl+tab" },
        { "\e[9;6u", "ctrl+shift+tab" },
        { "\e[9;15;9;1;8;1_", "ctrl+tab" },
        { "\e[105;5u", "ctrl+i" },
        { "\e[27;5;105~", "ctrl+i" },
        { "\e[109;5u", "ctrl+m" },
        { "\e[49;5u", "ctrl+1" },
        { "\e[49;31;49;1;8;1_", "ctrl+1" },
        { "\e[46;5u", "ctrl+." },
        { "\e[47;5u", "ctrl+/" },
        { "\e[91;5u", "ctrl+[" },
        { "\e[127;3u", "alt+backspace" },
    };

    [Theory]
    [MemberData(nameof(NewKeys))]
    public void NewKeys_MatchTheirBindings(string input, string binding)
    {
        var key = Assert.Single(Keys(input));
        Assert.True(KeyBinding.Parse(binding).Matches(key), $"{binding} vs {key.Key} '{key.KeyChar}' {key.Modifiers}");
    }

    [Fact]
    public void CtrlI_IsntTab_AndTabIsntCtrlI()
    {
        var ctrlI = Assert.Single(Keys("\e[105;5u"));
        var tab = Assert.Single(Keys("\t"));
        Assert.False(KeyBinding.Parse("tab").Matches(ctrlI));
        Assert.False(KeyBinding.Parse("ctrl+i").Matches(tab));
        Assert.False(KeyBinding.Parse("enter").Matches(Assert.Single(Keys("\e[13;5u"))));
        Assert.False(KeyBinding.Parse("ctrl+m").Matches(Assert.Single(Keys("\r"))));

        // Ctrl+letter as a bare control character still matches
        Assert.True(KeyBinding.Parse("ctrl+r").Matches(new ConsoleKeyInfo('\x12', 0, false, false, true)));
    }

    [Theory]
    [InlineData("\e[97;9u")] // Super
    [InlineData("\e[97;17u")] // Hyper
    [InlineData("\e[57441u")] // Left Shift alone
    [InlineData("\e[57428u")] // a media key
    [InlineData("\e[u")]
    [InlineData("\e[1;2;3;4u")]
    public void UnsupportedKittyKeys_AreDropped(string input) => Assert.Empty(Keys(input + "") );

    [Fact]
    public void KittyModifiers_LocksIgnored_MetaIsAlt_SubParametersSkipped()
    {
        Assert.Equal([Key(ConsoleKey.A, 'a')], Keys("\e[97;65u")); // Caps Lock
        Assert.Equal([Key(ConsoleKey.A, 'a')], Keys("\e[97;129u")); // Num Lock
        Assert.Equal([Key(ConsoleKey.A, 'a', Alt)], Keys("\e[97;33u")); // Meta
        Assert.Equal([Key(ConsoleKey.A, '\x01', Ctrl)], Keys("\e[97:65;5:1u")); // alternate key and event type
        Assert.Equal([Key(ConsoleKey.A, 'a')], Keys("\e[97;;97u")); // empty modifiers, text
    }

    [Fact]
    public void KeypadAndHighFunctionKeys()
    {
        Assert.Equal([Key(ConsoleKey.NumPad7, '7')], Keys("\e[57406u"));
        Assert.Equal([Key(ConsoleKey.Enter, '\r', Ctrl)], Keys("\e[57414;5u"));
        Assert.Equal([Key(ConsoleKey.UpArrow, '\0', Shift)], Keys("\e[57419;2u"));
        Assert.Equal([Key(ConsoleKey.F13, '\0')], Keys("\e[57376u"));
    }

    [Fact]
    public void ModifyOtherKeys_UppercaseCodeIsShift()
    {
        Assert.Equal([Key(ConsoleKey.A, '\x01', Ctrl | Shift)], Keys("\e[27;6;65~"));
        Assert.Equal([Key(ConsoleKey.Delete, '\0')], Keys("\e[3~")); // legacy tilde keys unchanged
    }

    // ---- win32-input-mode ----

    [Fact]
    public void Win32_KeyUpsAndModifierKeys_AreDropped_AndRepeatsRepeat()
    {
        Assert.Empty(Keys("\e[65;30;97;0;0;1_"));
        Assert.Empty(Keys("\e[16;42;0;1;16;1_"));
        Assert.Empty(Keys("\e[17;29;0;1;8;1_"));
        Assert.Equal(3, Keys("\e[65;30;97;1;0;3_").Count);
        Assert.Equal(32, Keys("\e[65;30;97;1;0;500_").Count);
    }

    [Fact]
    public void Win32_AltGr_TypesTheCharacter()
    {
        Assert.Equal([Key(0, '@')], Keys("\e[81;16;64;1;9;1_"));
        Assert.Equal([Key(0, '€')], Keys("\e[69;18;8364;1;9;1_"));
    }

    [Fact]
    public void Win32_ArrowsAndFunctionKeys()
    {
        Assert.Equal([Key(ConsoleKey.UpArrow, '\0', Ctrl)], Keys("\e[38;72;0;1;8;1_"));
        Assert.Equal([Key(ConsoleKey.F5, '\0')], Keys("\e[116;63;0;1;0;1_"));
    }

    [Fact]
    public void Win32_SurrogatesArriveOneCodeUnitAtATime_AsLegacy()
    {
        var keys = Keys("\e[0;0;55357;1;0;1_\e[0;0;56832;1;0;1_");
        Assert.Equal("😀", new string(keys.Select(k => k.KeyChar).ToArray()));
    }

    [Fact]
    public void Win32_ReportsTheMode()
    {
        var sink = Feed("\e[65;30;97;1;0;1_\e[65;30;97;0;0;1_");
        Assert.All(sink.Replies, r => Assert.IsType<TerminalReply.Win32InputMode>(r));
        Assert.Equal(2, sink.Replies.Count);
    }

    [Fact]
    public void ProtocolForms_SplitAtEveryByte_AreReassembled()
    {
        var bytes = Encoding.ASCII.GetBytes("\e[13;5u\e[27;5;97~\e[65;30;97;1;0;1_");
        for (var split = 1; split < bytes.Length; split++)
        {
            var sink = new Sink();
            var decoder = new AnsiInputDecoder(TimeSpan.FromMilliseconds(50));
            decoder.Feed(bytes.AsSpan(0, split), TimeSpan.Zero, sink);
            decoder.Feed(bytes.AsSpan(split), TimeSpan.FromMilliseconds(10), sink);
            Assert.Equal([Key(ConsoleKey.Enter, '\r', Ctrl), Key(ConsoleKey.A, '\x01', Ctrl), Key(ConsoleKey.A, 'a')], sink.Keys);
        }
    }

    // ---- Built-in handling of the new keys ----

    [Fact]
    public void CommandLine_SubmitsOnEnterAndShiftEnter_NotCtrlEnter()
    {
        var state = new CommandLineState { Text = ":go" };
        Assert.False(state.HandleKey(Key(ConsoleKey.Enter, '\r', Ctrl), out var none));
        Assert.Null(none);
        Assert.True(state.HandleKey(Key(ConsoleKey.Enter, '\r', Shift), out var submitted));
        Assert.Equal("go", submitted?.Name);
    }

    [Fact]
    public void TextArea_ShiftEnterIsALineBreak_CtrlEnterIsLeftToTheApp()
    {
        var area = new TextAreaState();
        Assert.True(area.HandleKey(Key(ConsoleKey.Enter, '\r', Shift)));
        Assert.False(area.HandleKey(Key(ConsoleKey.Enter, '\r', Ctrl)));
        Assert.Equal("\n", area.Text);
    }

    [Fact]
    public void Tree_IgnoresCtrlEnter()
    {
        var tree = new TreeState<string>(_ => [], s => s) { Roots = ["a"] };
        var activated = 0;
        tree.Activated += (_, _) => activated++;
        Assert.False(tree.HandleKey(Key(ConsoleKey.Enter, '\r', Ctrl)));
        Assert.True(tree.HandleKey(Key(ConsoleKey.Enter, '\r')));
        Assert.Equal(1, activated);
    }

    // ---- Choosing a protocol ----

    private sealed class Run : IDisposable
    {
        public Run(Action<App>? configure = null, bool windows = false, string? keyboardEnv = null)
        {
            App = App.Create().WithBackend(new TestBackend(20, 3)).WithInput(new ScriptedInputSource()).WithTickRate(null)
                .CaptureConsoleOutput(false).WithTimeProvider(new FakeTimeProvider());
            App.GetEnvironment = name => name == "MINITUI_KEYBOARD" ? keyboardEnv : null;
            App.QueryWriter = () => { };
            App.KeyboardWriter = Set.Add;
            App.WindowsOverride = windows;
            configure?.Invoke(App);
            App.Render((_, _) => { });
            App.StartRun(CancellationToken.None);
            using var ui = App.EnterUi();
            App.Setup();
            App.Step(wait: false);
        }

        public App App { get; }
        public List<KeyboardProtocol> Set { get; } = [];

        public void Reply(TerminalReply reply)
        {
            App.InjectReply(reply);
            using var ui = App.EnterUi();
            App.Step(wait: false);
        }

        public void Dispose() => App.EndRun();
    }

    [Fact]
    public void KittyReply_TurnsOnKitty_AndModifyOtherKeysAfterItIsIgnored()
    {
        using var run = new Run();
        Assert.Empty(run.Set);
        run.Reply(new TerminalReply.KittyFlags(0));
        run.Reply(new TerminalReply.ModifyOtherKeys(0));
        run.Reply(new TerminalReply.DeviceAttributes());
        Assert.Equal([KeyboardProtocol.Kitty], run.Set);
        Assert.Equal(KeyboardProtocol.Kitty, run.App.TerminalInfo.Keyboard);
    }

    [Fact]
    public void ModifyOtherKeysReply_WithoutKitty_TurnsItOn()
    {
        using var run = new Run();
        run.Reply(new TerminalReply.ModifyOtherKeys(0));
        Assert.Equal([KeyboardProtocol.ModifyOtherKeys], run.Set);
        Assert.Equal(KeyboardProtocol.ModifyOtherKeys, run.App.TerminalInfo.Keyboard);
    }

    [Fact]
    public void NoReplies_StayLegacy()
    {
        using var run = new Run();
        run.Reply(new TerminalReply.DeviceAttributes());
        Assert.Empty(run.Set);
        Assert.Equal(KeyboardProtocol.Legacy, run.App.TerminalInfo.Keyboard);
    }

    [Fact]
    public void OptingOut_KeepsLegacy()
    {
        using var api = new Run(app => app.WithKeyboardProtocol(false));
        api.Reply(new TerminalReply.KittyFlags(0));
        Assert.Empty(api.Set);

        using var env = new Run(keyboardEnv: "legacy", windows: true);
        env.Reply(new TerminalReply.KittyFlags(0));
        Assert.Empty(env.Set);
    }

    [Fact]
    public void Windows_AsksForWin32InputModeAtOnce_AndReportsItWhenAKeyArrives()
    {
        using var run = new Run(windows: true);
        Assert.Equal([KeyboardProtocol.Win32InputMode], run.Set);
        Assert.Equal(KeyboardProtocol.Legacy, run.App.TerminalInfo.Keyboard);
        run.Reply(new TerminalReply.Win32InputMode());
        Assert.Equal(KeyboardProtocol.Win32InputMode, run.App.TerminalInfo.Keyboard);
    }

    [Fact]
    public void Win32Keys_OutsideWindows_DontChangeTheProtocol()
    {
        using var run = new Run();
        run.Reply(new TerminalReply.Win32InputMode());
        Assert.Equal(KeyboardProtocol.Legacy, run.App.TerminalInfo.Keyboard);
    }

    [Fact]
    public void Resume_GoesBackToLegacy_UntilTheRepliesComeAgain()
    {
        using var run = new Run();
        run.Reply(new TerminalReply.KittyFlags(0));
        using (run.App.EnterUi()) run.App.Suspend(() => { });
        Assert.Equal(KeyboardProtocol.Legacy, run.App.TerminalInfo.Keyboard);
        run.Reply(new TerminalReply.KittyFlags(0));
        Assert.Equal([KeyboardProtocol.Kitty, KeyboardProtocol.Kitty], run.Set);
    }
}
