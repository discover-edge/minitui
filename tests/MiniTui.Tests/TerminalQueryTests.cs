using System.Text;
using Microsoft.Extensions.Time.Testing;
using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Terminal queries, their replies, and light and dark themes (1.x spec §12.3, §12.4).</summary>
public class TerminalQueryTests
{
    private sealed class Sink : IInputSink
    {
        public List<ConsoleKeyInfo> Keys { get; } = [];
        public List<string> Pastes { get; } = [];
        public List<TerminalReply> Replies { get; } = [];
        public void Key(ConsoleKeyInfo key) => Keys.Add(key);
        public void Paste(string text) => Pastes.Add(text);
        public void Mouse(MouseEvent mouse) { }
        public void Reply(TerminalReply reply) => Replies.Add(reply);
    }

    private const string AllReplies =
        "\e[?1u" + "\e[>4;2m" + "\e]10;rgb:ffff/ffff/ffff\e\\" + "\e]11;rgb:1e1e/1e1e/2e2e\a" + "\eP>|WezTerm 2024\e\\" + "\e[?62;22c";

    private static Sink Decode(string input, TimeSpan? timeout = null)
    {
        var sink = new Sink();
        var decoder = new AnsiInputDecoder(timeout ?? TimeSpan.FromMilliseconds(50));
        decoder.Feed(Encoding.UTF8.GetBytes(input), TimeSpan.Zero, sink);
        decoder.Expire(TimeSpan.FromSeconds(10), sink);
        return sink;
    }

    // ---- Decoding ----

    [Fact]
    public void EveryReplyForm_IsDecoded_AndNeverAKey()
    {
        var sink = Decode(AllReplies);
        Assert.Empty(sink.Keys);
        Assert.Equal(
        [
            new TerminalReply.KittyFlags(1),
            new TerminalReply.ModifyOtherKeys(2),
            new TerminalReply.DefaultColor(10, Color.Rgb(255, 255, 255)),
            new TerminalReply.DefaultColor(11, Color.Rgb(0x1e, 0x1e, 0x2e)),
            new TerminalReply.Version("WezTerm 2024"),
            new TerminalReply.DeviceAttributes(),
        ], sink.Replies);
    }

    [Fact]
    public void Replies_SplitAtEveryByte_AreReassembled()
    {
        var bytes = Encoding.UTF8.GetBytes("a" + AllReplies + "b");
        for (var split = 1; split < bytes.Length; split++)
        {
            var sink = new Sink();
            var decoder = new AnsiInputDecoder(TimeSpan.FromMilliseconds(50));
            decoder.Feed(bytes.AsSpan(0, split), TimeSpan.Zero, sink);
            decoder.Feed(bytes.AsSpan(split), TimeSpan.FromMilliseconds(10), sink);
            Assert.Equal("ab", new string(sink.Keys.Select(k => k.KeyChar).ToArray()));
            Assert.Equal(6, sink.Replies.Count);
        }
    }

    [Theory]
    [InlineData("rgb:f/0/8", 255, 0, 136)]
    [InlineData("rgb:ff/00/80", 255, 0, 128)]
    [InlineData("rgb:fff/000/800", 255, 0, 128)]
    [InlineData("rgb:FFFF/0000/8000", 255, 0, 128)]
    public void OscColors_AcceptOneToFourHexDigits(string spec, int r, int g, int b)
    {
        var sink = Decode($"\e]11;{spec}\a");
        Assert.Equal([new TerminalReply.DefaultColor(11, Color.Rgb((byte)r, (byte)g, (byte)b))], sink.Replies);
    }

    [Theory]
    [InlineData("\e]11;rgb:ff/ff\a")]
    [InlineData("\e]11;rgb:gg/00/00\a")]
    [InlineData("\e]11;#ffffff\a")]
    [InlineData("\e]12;rgb:ff/ff/ff\a")]
    [InlineData("\e]52;c;aGk=\a")]
    [InlineData("\eP>|\e\\")]
    [InlineData("\eP1$r0m\e\\")]
    [InlineData("\e[?1;2$y")]
    [InlineData("\e[>1;10;0c")]
    [InlineData("\e[>5m")]
    public void UnknownOrMalformedReplies_AreDropped(string input)
    {
        var sink = Decode(input + "x");
        Assert.Empty(sink.Replies);
        Assert.Equal("x", new string(sink.Keys.Select(k => k.KeyChar).ToArray()));
    }

    [Fact]
    public void Replies_BetweenKeysAndPastes_KeepTheirOrder()
    {
        var sink = Decode("q\e[?62c\e[200~hi\e[201~\e]11;rgb:00/00/00\aw");
        Assert.Equal("qw", new string(sink.Keys.Select(k => k.KeyChar).ToArray()));
        Assert.Equal(["hi"], sink.Pastes);
        Assert.Equal(2, sink.Replies.Count);
    }

    [Fact]
    public void ModifyOtherKeysReply_WithoutAValue_IsZero()
    {
        Assert.Equal([new TerminalReply.ModifyOtherKeys(0)], Decode("\e[>4m").Replies);
    }

    [Fact]
    public void VersionReply_IsSanitized()
    {
        Assert.Equal([new TerminalReply.Version("xterm(390)")], Decode("\eP>|xterm(390)\e\\").Replies);
        var reply = Assert.IsType<TerminalReply.Version>(Assert.Single(Decode("\eP>|a\tb\e\\").Replies));
        Assert.Equal("a b", reply.Text);
    }

    // ---- Light and dark ----

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(0x1e, 0x1e, 0x2e, true)]
    [InlineData(0x80, 0x80, 0x80, true)]
    [InlineData(0xc0, 0xc0, 0xc0, false)]
    [InlineData(0xff, 0xff, 0xff, false)]
    [InlineData(0xfd, 0xf6, 0xe3, false)] // Solarized light
    [InlineData(0x00, 0x2b, 0x36, true)] // Solarized dark
    public void IsDark_FromLuminance(int r, int g, int b, bool dark) =>
        Assert.Equal(dark, TerminalQueries.IsDark(Color.Rgb((byte)r, (byte)g, (byte)b)));

    [Theory]
    [InlineData("15;0", true)]
    [InlineData("0;15", false)]
    [InlineData("0;7", false)]
    [InlineData("15;8", true)]
    [InlineData("12;default;0", true)]
    [InlineData("default;default", null)]
    [InlineData("15;16", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void IsDark_FromColorFgBg(string? value, bool? dark) =>
        Assert.Equal(dark, TerminalQueries.IsDarkFromColorFgBg(value));

    [Fact]
    public void QueryBytes_EndWithDeviceAttributes()
    {
        var text = Encoding.ASCII.GetString(TerminalQueries.Bytes);
        Assert.Equal("\e[?u\e[?4m\e]10;?\e\\\e]11;?\e\\\e[>0q\e[c", text);
    }

    [Fact]
    public void AnsiBackend_WritesQueries_OnlyWhileEntered()
    {
        var host = new FakeConsoleHost();
        var backend = new AnsiBackend(ColorMode.TrueColor, host);
        backend.WriteQueries();
        Assert.Equal(0, host.Output.Length);

        backend.Enter();
        var before = (int)host.Output.Length;
        backend.WriteQueries();
        Assert.Equal(TerminalQueries.Bytes.ToArray(), host.Output.ToArray()[before..]);

        backend.Leave();
        var after = host.Output.Length;
        backend.WriteQueries();
        Assert.Equal(after, host.Output.Length);
        backend.Dispose();
    }

    [Fact]
    public void WithoutAWriter_TheTestBackend_GetsNoQueries()
    {
        using var run = new Run(new FakeTimeProvider(), queries: false);
        Assert.Equal(0, run.Sent);
        Assert.False(run.App.TerminalInfo.Answered);
    }

    // ---- The app ----

    private sealed class Run : IDisposable
    {
        public Run(FakeTimeProvider time, Action<App>? configure = null, string? colorFgBg = null, bool queries = true, ITerminalBackend? backend = null)
        {
            Backend = backend ?? new TestBackend(20, 3);
            App = App.Create().WithBackend(Backend).WithInput(new ScriptedInputSource()).WithTickRate(null)
                .CaptureConsoleOutput(false).WithTimeProvider(time);
            App.GetEnvironment = name => name == "COLORFGBG" ? colorFgBg : null;
            if (queries) App.QueryWriter = () => Sent++;
            App.OnEvent(e =>
            {
                if (e is TerminalInfoEvent info) Events.Add(info.Info);
            });
            configure?.Invoke(App);
            App.Render((buf, _) =>
            {
                Frames++;
                ThemeSeen = Theme.Current;
            });
            App.StartRun(CancellationToken.None);
            using var ui = App.EnterUi();
            App.Setup();
            App.Step(wait: false);
        }

        public App App { get; }
        public ITerminalBackend Backend { get; }
        public int Sent { get; private set; }
        public int Frames { get; private set; }
        public Theme? ThemeSeen { get; private set; }
        public List<TerminalInfo> Events { get; } = [];

        public void Reply(TerminalReply reply)
        {
            App.InjectReply(reply);
            Step();
        }

        public void Step()
        {
            using var ui = App.EnterUi();
            App.Step(wait: false);
        }

        public void Dispose() => App.EndRun();
    }

    [Fact]
    public void Queries_AreSentOnceAtStart_AndRepliesUpdateTerminalInfo()
    {
        using var run = new Run(new FakeTimeProvider());
        Assert.Equal(1, run.Sent);
        Assert.False(run.App.TerminalInfo.Answered);

        run.Reply(new TerminalReply.DefaultColor(11, Color.Rgb(250, 250, 250)));
        run.Reply(new TerminalReply.Version("foot 1.16"));
        run.Reply(new TerminalReply.DeviceAttributes());
        var info = run.App.TerminalInfo;
        Assert.Equal((true, false, "foot 1.16"), (info.Answered, info.IsDark, info.Name));
        Assert.Equal(Color.Rgb(250, 250, 250), info.Background);
        Assert.Equal(3, run.Events.Count);
        Assert.Same(info, run.Events[^1]);
    }

    [Fact]
    public void ARepeatedReply_RaisesNoEvent()
    {
        using var run = new Run(new FakeTimeProvider());
        run.Reply(new TerminalReply.DeviceAttributes());
        run.Reply(new TerminalReply.DeviceAttributes());
        run.Reply(new TerminalReply.KittyFlags(0)); // used by the keyboard protocol, not TerminalInfo
        Assert.Single(run.Events);
    }

    [Fact]
    public void QueriesOff_WritesNothing()
    {
        using var run = new Run(new FakeTimeProvider(), app => app.WithTerminalQueries(false));
        Assert.Equal(0, run.Sent);

        using var env = new Run(new FakeTimeProvider(), app => app.GetEnvironment = n => n == "MINITUI_QUERIES" ? "0" : null);
        Assert.Equal(0, env.Sent);
    }

    [Fact]
    public void ColorFgBg_GivesIsDark_BeforeAnyReply()
    {
        using var run = new Run(new FakeTimeProvider(), colorFgBg: "0;15");
        Assert.False(run.App.TerminalInfo.IsDark);
    }

    [Fact]
    public void Resume_AsksAgain()
    {
        using var run = new Run(new FakeTimeProvider());
        using (run.App.EnterUi()) run.App.Suspend(() => { });
        Assert.Equal(2, run.Sent);
    }

    // ---- Theme pairs ----

    [Fact]
    public void ThemePair_WaitsForTheBackground_ThenDrawsWithTheLightTheme()
    {
        var time = new FakeTimeProvider();
        using var run = new Run(time, app => app.WithTheme(Theme.Default, Theme.Light));
        Assert.Equal(0, run.Frames);

        run.Reply(new TerminalReply.DefaultColor(11, Color.Rgb(255, 255, 255)));
        Assert.Equal(1, run.Frames);
        Assert.Same(Theme.Light, run.ThemeSeen);
    }

    [Fact]
    public void ThemePair_StopsWaitingAtDeviceAttributes_UsingDark()
    {
        using var run = new Run(new FakeTimeProvider(), app => app.WithTheme(Theme.Default, Theme.Light));
        run.Reply(new TerminalReply.DeviceAttributes());
        Assert.Equal(1, run.Frames);
        Assert.Same(Theme.Default, run.ThemeSeen);
    }

    [Fact]
    public void ThemePair_StopsWaitingAfter150ms_AndSwitchesWhenTheReplyComesLate()
    {
        var time = new FakeTimeProvider();
        using var run = new Run(time, app => app.WithTheme(Theme.Default, Theme.Light));
        time.Advance(TimeSpan.FromMilliseconds(149));
        run.Step();
        Assert.Equal(0, run.Frames);
        time.Advance(TimeSpan.FromMilliseconds(1));
        run.Step();
        Assert.Equal(1, run.Frames);
        Assert.Same(Theme.Default, run.ThemeSeen);

        run.Reply(new TerminalReply.DefaultColor(11, Color.Rgb(255, 255, 255)));
        Assert.Same(Theme.Light, run.ThemeSeen);

        // Picked once: a later reply doesn't switch back
        run.Reply(new TerminalReply.DefaultColor(11, Color.Rgb(0, 0, 0)));
        Assert.Same(Theme.Light, run.ThemeSeen);
    }

    [Fact]
    public void ThemePair_WithoutQueries_UsesColorFgBg_AndDoesntWait()
    {
        using var run = new Run(new FakeTimeProvider(), app => app.WithTheme(Theme.Default, Theme.Light), colorFgBg: "0;15", queries: false);
        Assert.Equal(1, run.Frames);
        Assert.Same(Theme.Light, run.ThemeSeen);
    }

    [Fact]
    public void ASingleTheme_DoesntWait_AndIgnoresTheBackground()
    {
        using var run = new Run(new FakeTimeProvider(), app => app.WithTheme(Theme.Monochrome));
        Assert.Equal(1, run.Frames);
        run.Reply(new TerminalReply.DefaultColor(11, Color.Rgb(255, 255, 255)));
        Assert.Same(Theme.Monochrome, run.ThemeSeen);
    }

    [Fact]
    public void WithThemeSingle_ReplacesAPair()
    {
        using var run = new Run(new FakeTimeProvider(), app => app.WithTheme(Theme.Default, Theme.Light).WithTheme(Theme.Monochrome));
        Assert.Equal(1, run.Frames);
        Assert.Same(Theme.Monochrome, run.ThemeSeen);
    }

    public static TheoryData<string> FixedThemes => ["Dark", "Light"];

    private static Theme Named(string name) => name == "Dark" ? Theme.Dark : Theme.Light;

    private static IEnumerable<Style> Slots(Theme theme) =>
        typeof(Theme).GetProperties().Where(p => p.PropertyType == typeof(Style)).Select(p => (Style)p.GetValue(theme)!)
            .Concat(theme.ChartSeries);

    [Theory]
    [MemberData(nameof(FixedThemes))]
    public void FixedThemes_UseOnlyRgbColors_AndSetABackground(string name)
    {
        var theme = Named(name);
        Assert.NotNull(theme.Text.Fg);
        Assert.NotNull(theme.Text.Bg);
        Assert.All(Slots(theme), s =>
        {
            Assert.True(s.Fg is null || s.Fg.Value.Kind == ColorKind.Rgb);
            Assert.True(s.Bg is null || s.Bg.Value.Kind == ColorKind.Rgb);
        });
    }

    [Theory]
    [MemberData(nameof(FixedThemes))]
    public void FixedThemes_HaveReadableText(string name)
    {
        var theme = Named(name);
        var bg = theme.Text.Bg!.Value;
        Assert.True(Contrast(theme.Text.Fg!.Value, bg) >= 7);
        foreach (var pair in new[] { theme.Selection, theme.SelectionUnfocused, theme.StatusBar, theme.Button, theme.ButtonFocused, theme.TabActive })
            Assert.True(Contrast(pair.Fg ?? theme.Text.Fg!.Value, pair.Bg!.Value) >= 4.5, pair.ToString());
        foreach (var accent in new[] { theme.TextMuted, theme.Title, theme.TitleFocused, theme.TableHeader, theme.Success, theme.Warning, theme.Error, theme.Info })
            Assert.True(Contrast(accent.Fg!.Value, bg) >= 4, accent.ToString());
        Assert.All(theme.ChartSeries, s => Assert.True(Contrast(s.Fg!.Value, bg) >= 4));
    }

    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            var (r, g, b) = c.RgbValue;
            static double Lin(byte v) => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
        }
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    [Fact]
    public void ThemePair_PicksDarkOrLight_ByLuminance()
    {
        Assert.True(TerminalQueries.IsDark(Theme.Dark.Text.Bg!.Value));
        Assert.False(TerminalQueries.IsDark(Theme.Light.Text.Bg!.Value));
    }

    [Fact]
    public void AThemeWithABackground_FillsTheScreen_AndClearedAreas()
    {
        using var run = new Run(new FakeTimeProvider(), app => app.WithTheme(Theme.Dark), queries: false);
        var backend = (TestBackend)run.Backend;
        var bg = Theme.Dark.Text.Bg;
        Assert.All(Enumerable.Range(0, 20), x => Assert.Equal(bg, backend.CellAt(x, 2).Bg));

        var buf = new Buffer(6, 2);
        using (Theme.Use(Theme.Light)) WidgetText.Clear(buf, buf.Area);
        Assert.Equal(Theme.Light.Text.Bg, buf.Get(5, 1).Bg);
        Assert.Equal(Theme.Light.Text.Fg, buf.Get(5, 1).Fg);
    }

    [Fact]
    public void TheDefaultTheme_LeavesTheBackgroundToTheTerminal()
    {
        using var run = new Run(new FakeTimeProvider(), queries: false);
        var backend = (TestBackend)run.Backend;
        Assert.Equal(Color.Reset, backend.CellAt(19, 2).Bg);
        Assert.Null(Theme.Default.Text.Bg);
    }

    [Theory]
    [MemberData(nameof(FixedThemes))]
    public void FixedThemes_UnderNoColor_AreMonochrome(string name)
    {
        using var run = new Run(new FakeTimeProvider(), app => app.WithTheme(Named(name)), queries: false,
            backend: new AnsiBackend(ColorMode.None, new FakeConsoleHost(20, 3)));
        Assert.Same(Theme.Monochrome, run.ThemeSeen);
    }
}
