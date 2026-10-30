namespace MiniTui.Tests;

// Keeps the README's app-testing example honest.
public class ReadmeSnippetTests
{
    [Fact]
    public void TestingAWholeApp()
    {
        var backend = new TestBackend(40, 10);
        var input = new ScriptedInputSource().EnqueueText("kkq");
        var count = 0;

        var app = App.Create().WithBackend(backend).WithInput(input).WithTickRate(null).CaptureConsoleOutput(false);
        app.OnKey(key =>
            {
                if (key.KeyChar == 'k') count++;
                if (key.KeyChar == 'q') app.Quit();
                return true;
            })
            .Render((buf, _) => buf.SetString(0, 0, $"Count: {count}", Style.Default))
            .Run();

        Assert.Equal(2, count);
    }

    [Fact]
    public void KeyMapHints()
    {
        var keys = new KeyMap()
            .Add("r", "Refresh", () => { })
            .Add("j,down", "Down", () => { }, group: "Navigation")
            .Add("ctrl+s", "Save", () => { })
            .Add("q,esc", "Quit", () => { });

        Assert.Equal("r Refresh  j Down  ctrl+s Save  q Quit", keys.Hints(40));
    }

    [Fact]
    public void UsagePattern_Counter()
    {
        var backend = new TestBackend(40, 8);
        var count = 0;
        var app = App.Create().WithBackend(backend).WithInput(new ScriptedInputSource().EnqueueText("kkjkq"))
            .WithTickRate(null).CaptureConsoleOutput(false);
        var keys = new KeyMap()
            .Add("k,up", "+1", () => count++)
            .Add("j,down", "-1", () => count--)
            .Add("q", "Quit", app.Quit);

        app.OnKey(keys.Handle)
            .Render((buf, screen) =>
            {
                var inner = Widget.Block().Title(" Counter ").Draw(buf, new Rect(2, 1, 30, 5));
                buf.SetString(inner.X + 1, inner.Y + 1, $"Count: {count}", Style.Default.Bold());
                Widget.StatusBar().Left(keys.Hints(screen.Width)).Draw(buf, screen);
            })
            .Run();

        Assert.Equal(2, count);
    }

    private sealed class SearchBox : Component
    {
        public readonly TextFieldState text = new();
        public override bool CapturesText => true;
        public override bool HandleKey(ConsoleKeyInfo key) => text.HandleKey(key);
        public override void Render(MiniTui.Buffer buf, Rect area, RenderContext ctx) =>
            Widget.TextField().State(text).Focused(ctx.Focused).Block(Widget.Block().Title(" Search ")).Draw(buf, area);
    }

    private sealed class FilesScreen : Screen
    {
        public readonly SearchBox search;
        readonly Component list;
        public int Reloads;

        public FilesScreen()
        {
            search = AddChild(new SearchBox());
            list = AddChild(Component.From((buf, area, ctx) => { /* draw */ }, key => false));
            Focus.Add(list).Add(search);
            Keys.Add("ctrl+r", "Reload", Reload);
        }

        public override void Render(MiniTui.Buffer buf, Rect area, RenderContext ctx)
        {
            var rows = Layout.Split(area, Direction.Vertical, Constraint.Length(3), Constraint.Fill());
            search.Render(buf, rows[0], ctx.For(search));
            list.Render(buf, rows[1], ctx.For(list));
        }

        void Reload() => Reloads++;
    }

    [Fact]
    public void ScreensAndComponents()
    {
        var screen = new FilesScreen();
        using var harness = new ComponentHarness(screen, width: 80, height: 24);
        harness.App.Keys.Add("q", "Quit", harness.App.Quit);

        harness.Press("tab");
        harness.Type("hello q");
        harness.Press("ctrl+r");

        Assert.Equal("hello q", screen.search.text.Text);
        Assert.Contains(harness.Lines, l => l.Contains("hello", StringComparison.Ordinal));
        Assert.Equal(1, screen.Reloads);
        Assert.True(harness.App.IsRunning);
    }
}

// Keeps docs/MIGRATION-0.7-to-1.0.md's form example honest.
public class MigrationSnippetTests
{
    private sealed class FormScreen : Screen
    {
        readonly TextFieldState name = new();
        readonly Component nameField, subscribeBox;
        bool subscribe;

        public FormScreen()
        {
            nameField = AddChild(Component.From(
                (buf, area, ctx) => Widget.TextField().State(name).Focused(ctx.Focused).Draw(buf, area),
                name.HandleKey, capturesText: true));
            subscribeBox = AddChild(Component.From(
                (buf, area, ctx) => Widget.Checkbox().Label("Subscribe").Checked(subscribe).Focused(ctx.Focused).Draw(buf, area),
                key =>
                {
                    if (key.Key != ConsoleKey.Spacebar) return false;
                    subscribe = !subscribe;
                    return true;
                }));
            Focus.Add(nameField).Add(subscribeBox);
        }

        public override void Render(MiniTui.Buffer buf, Rect area, RenderContext ctx)
        {
            var rows = Layout.Split(area, Direction.Vertical, Constraint.Length(1), Constraint.Length(1));
            nameField.Render(buf, rows[0], ctx.For(nameField));
            subscribeBox.Render(buf, rows[1], ctx.For(subscribeBox));
        }
    }

    [Fact]
    public void FocusAndModals_FormScreen()
    {
        using var harness = new ComponentHarness(new FormScreen(), 30, 5);

        harness.Type("Ann q");
        harness.Press("tab space");

        Assert.Equal("Ann q", harness.Lines[0].TrimEnd());
        Assert.StartsWith("[✓] Subscribe", harness.Lines[1], StringComparison.Ordinal);
    }
}
