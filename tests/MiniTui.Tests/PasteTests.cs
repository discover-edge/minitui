using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

/// <summary>Bracketed paste routing and handling (1.x spec §3).</summary>
public class PasteTests
{
    /// <summary>A component that records pastes and keys, and handles pastes when told to.</summary>
    private sealed class PasteProbe(string name, List<string> log, bool handles) : Component
    {
        public override bool HandleKey(ConsoleKeyInfo key)
        {
            log.Add($"{name} key {key.KeyChar}");
            return false;
        }

        public override bool HandlePaste(string text)
        {
            log.Add($"{name} paste {text}");
            return handles;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    private sealed class PasteScreen : Screen
    {
        private readonly List<string> _log;

        public PasteScreen(List<string> log, bool childHandles, bool screenHandles = false)
        {
            _log = log;
            ScreenHandles = screenHandles;
            Child = AddChild(new PasteProbe("child", log, childHandles));
            Focus.Add(Child);
        }

        public Component Child { get; }
        private bool ScreenHandles { get; }

        public override bool HandleKey(ConsoleKeyInfo key)
        {
            _log.Add($"screen key {key.KeyChar}");
            return false;
        }

        public override bool HandlePaste(string text)
        {
            _log.Add($"screen paste {text}");
            return ScreenHandles;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    private sealed class PasteOverlay(List<string> log) : Overlay
    {
        public override bool HandlePaste(string text)
        {
            log.Add($"overlay paste {text}");
            return true;
        }

        public override void Render(Buffer buf, Rect area, RenderContext ctx) { }
    }

    private sealed class FieldScreen : Screen
    {
        public TextFieldState State { get; } = new();
        public override bool CapturesText => true;
        public override bool HandleKey(ConsoleKeyInfo key) => State.HandleKey(key);
        public override bool HandlePaste(string text) => State.Paste(text);
        public override void Render(Buffer buf, Rect area, RenderContext ctx) => Widget.TextField().State(State).Draw(buf, area);
    }

    [Fact]
    public void Paste_GoesToTheFocusedComponent_AndStopsThere()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new PasteScreen(log, childHandles: true));

        harness.Paste("hello");

        Assert.Equal(["child paste hello"], log);
    }

    [Fact]
    public void Paste_TheFocusedComponentDeclines_GoesToItsParent()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new PasteScreen(log, childHandles: false, screenHandles: true));

        harness.Paste("hello");

        Assert.Equal(["child paste hello", "screen paste hello"], log);
    }

    [Fact]
    public void Paste_NobodyHandlesIt_IsDropped_AndFiresNoBindings()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new PasteScreen(log, childHandles: false));
        var fired = 0;
        harness.App.Keys.Add("x", "X", () => fired++);

        harness.Paste("x?q\n");

        Assert.Equal(["child paste x?q\n", "screen paste x?q\n"], log);
        Assert.Equal(0, fired);
        Assert.False(harness.App.Overlays.Any); // ? would open help
        Assert.True(harness.App.IsRunning);
    }

    [Fact]
    public void OnPaste_ReturningTrue_StopsThePaste_AndOnEventSeesIt()
    {
        var log = new List<string>();
        var events = new List<Event>();
        using var harness = new ComponentHarness(new PasteScreen(log, childHandles: true));
        harness.App.OnEvent(events.Add).OnPaste(text =>
        {
            log.Add($"app paste {text}");
            return true;
        });

        harness.Paste("hi");

        Assert.Equal(["app paste hi"], log);
        Assert.Equal([new PasteEvent("hi")], events.OfType<PasteEvent>());
    }

    [Fact]
    public void OnPaste_ReturningFalse_LetsTheFocusChainHaveIt()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new PasteScreen(log, childHandles: true));
        harness.App.OnPaste(_ => false);

        harness.Paste("hi");

        Assert.Equal(["child paste hi"], log);
    }

    [Fact]
    public void Paste_WithAnOverlayOpen_GoesOnlyToTheOverlay()
    {
        var log = new List<string>();
        using var harness = new ComponentHarness(new PasteScreen(log, childHandles: true));
        harness.App.Overlays.Show(new PasteOverlay(log));
        harness.RunUntilIdle();

        harness.Paste("hi");

        Assert.Equal(["overlay paste hi"], log);
    }

    [Fact]
    public void Paste_KeepsItsPlaceBetweenKeys()
    {
        var screen = new FieldScreen();
        using var harness = new ComponentHarness(screen);

        harness.Type("ab");
        harness.Paste("XY");
        harness.Press("left");
        harness.Paste("-");

        Assert.Equal("abX-Y", screen.State.Text);
    }

    [Fact]
    public void TextField_PasteWithLineBreaks_InsertsOneLine_WithoutSubmitting()
    {
        var screen = new FieldScreen();
        using var harness = new ComponentHarness(screen);

        harness.Paste("\nfirst line\nsecond\tline\n");

        Assert.Equal("first line second line", screen.State.Text);
        Assert.True(harness.App.IsRunning);
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("\nab\r\ncd\tef\n", "ab cd ef")]
    [InlineData("\r\n\r\nab\r\n\r\n", "ab")]
    [InlineData("a\r\rb", "a  b")]
    [InlineData("a\u001b[Ab\u0007c\u007f", "a[Abc")]
    [InlineData("\n\n", "")]
    [InlineData("日本😀", "日本😀")]
    public void TextFieldState_Paste_MakesOneLine(string pasted, string expected)
    {
        var state = new TextFieldState();

        Assert.True(state.Paste(pasted));

        Assert.Equal(expected, state.Text);
        Assert.Equal(expected.Length, state.Cursor);
    }

    [Fact]
    public void TextFieldState_Paste_InsertsAtTheCursor()
    {
        var state = new TextFieldState("hello") { Cursor = 2 };

        state.Paste("XY");

        Assert.Equal("heXYllo", state.Text);
        Assert.Equal(4, state.Cursor);
    }

    [Fact]
    public void CommandLineOverlay_InsertsAPaste_InsteadOfRunningAtItsLineBreak()
    {
        var added = new List<string>();
        var registry = CommandRegistry.Create()
            .Add("add").Handler(cmd => { added.Add(cmd.ArgsAsString); return CommandResult.Ok("Added"); })
            .Build();
        using var harness = new ComponentHarness(new FieldScreen());
        harness.App.Keys.Add("ctrl+o", "Command", () => harness.App.Overlays.Show(new CommandLineOverlay(registry)));

        harness.Press("ctrl+o");
        harness.Paste("add milk\neggs\n");
        Assert.Empty(added);
        Assert.True(harness.App.Overlays.Any);
        harness.Press("enter");

        Assert.Equal(["milk eggs"], added);
    }

    [Fact]
    public void ScriptedInput_PastesAndKeys_ArriveInOrder()
    {
        var input = new ScriptedInputSource()
            .EnqueueText("a").EnqueuePaste("one").EnqueuePaste("").Enqueue(AppHarness.Key('b')).EnqueuePaste("two");

        var items = new List<string>();
        while (((IInputItemSource)input).TryReadItem(out var item))
            items.Add(item.Paste is { } text ? $"paste {text}" : $"key {item.Key.KeyChar}");

        Assert.Equal(["key a", "paste one", "key b", "paste two"], items);
    }

    [Fact]
    public void ScriptedInput_TryRead_SkipsPastes()
    {
        var input = new ScriptedInputSource().EnqueuePaste("p").EnqueueText("k");

        Assert.True(input.TryRead(out var key));
        Assert.Equal('k', key.KeyChar);
        Assert.False(input.TryRead(out _));
    }

    [Fact]
    public async Task App_GetsPastesFromItsInput_InOrderWithKeys()
    {
        var input = new ScriptedInputSource()
            .EnqueueText("a").EnqueuePaste("p\nq").EnqueueText("b").Enqueue(AppHarness.Key('q'));
        var events = new List<string>();
        var app = AppHarness.Create(new TestBackend(20, 5), input).Render((_, _) => { });
        app.OnEvent(e =>
        {
            if (e is PasteEvent paste) events.Add($"paste {paste.Text}");
            if (e is KeyEvent key) events.Add($"key {key.Key.KeyChar}");
        });
        app.OnKey(key =>
        {
            if (key.KeyChar == 'q') app.Quit();
            return true;
        });

        await AppHarness.Run(app);

        Assert.Equal(["key a", "paste p\nq", "key b", "key q"], events);
    }

    [Fact]
    public async Task NestedApp_HandsAnUnhandledPasteBackAsAPaste()
    {
        var input = new ScriptedInputSource()
            .EnqueueText("\r").EnqueuePaste("inner").Enqueue(AppHarness.Key('q')).EnqueuePaste("outer").Enqueue(AppHarness.Key('q'));
        var backend = new TestBackend(20, 5);
        var outer = new List<string>();
        var inner = new List<string>();

        var app = AppHarness.Create(backend, input).Render((_, _) => { });
        app.OnPaste(text =>
        {
            outer.Add(text);
            return true;
        });
        app.OnKey(key =>
        {
            if (key.KeyChar == 'q')
            {
                app.Quit();
                return true;
            }
            if (key.Key != ConsoleKey.Enter) return true;

            // The nested app quits at the first q, leaving the "outer" paste queued for the outer app
            var nested = AppHarness.Create(backend, input).Render((_, _) => { });
            nested.OnPaste(text =>
            {
                inner.Add(text);
                return true;
            });
            nested.OnKey(k =>
            {
                if (k.KeyChar == 'q') nested.Quit();
                return true;
            }).Run();
            return true;
        });
        await AppHarness.Run(app);

        Assert.Equal(["inner"], inner);
        Assert.Equal(["outer"], outer);
    }
}
