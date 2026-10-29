using MiniTui;

namespace MiniTui.Example;

/// <summary>The smallest screen: a number and three key bindings.</summary>
internal sealed class CounterScreen : Screen
{
    private int _count;

    public override string Title => "Counter";

    public CounterScreen() => Keys
        .Add("k,up", "+1", () => _count++)
        .Add("j,down", "-1", () => _count--)
        .Add("r", "Reset", () => _count = 0);

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var box = Widget.Block().Title(" Counter ").Padding(new Margin(2, 1)).Focused(ctx.Focused)
            .Draw(buf, new Rect(area.X + 2, area.Y + 1, 40, 7));
        buf.SetString(box.X, box.Y + 1, $"Count: {_count}", ctx.Theme.Warning.Bold());
        buf.SetString(box.X, box.Y + 3, "k/↑ +1   j/↓ -1   r reset", ctx.Theme.TextMuted);
    }
}

/// <summary>A list whose selection drives a detail line.</summary>
internal sealed class ListScreen : Screen
{
    private static readonly string[] Fruits =
        ["Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape", "Honeydew", "Kiwi", "Lemon", "Mango", "Nectarine", "Orange", "Papaya", "Quince"];

    private readonly SelectionState _selection = new();

    public override string Title => "List Selection";

    public override bool HandleKey(ConsoleKeyInfo key) => _selection.HandleKey(key, Fruits.Length);

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        buf.SetString(area.X + 2, area.Y + 1, "Select a fruit:", ctx.Theme.Text.Bold());
        Widget.List().Title("Fruit").Items(Fruits).State(_selection).Focused(ctx.Focused)
            .Draw(buf, new Rect(area.X + 2, area.Y + 3, 30, 10));
        buf.SetString(area.X + 35, area.Y + 3, "Selected:", ctx.Theme.TextMuted);
        buf.SetString(area.X + 35, area.Y + 4, Fruits[_selection.Selected], ctx.Theme.Info);
    }
}

/// <summary>Gauges driven by an async loop that stops when the screen is popped.</summary>
internal sealed class ProgressScreen : Screen
{
    private double _progress;
    private bool _running;
    private double _phase;

    public override string Title => "Progress Bars";

    public ProgressScreen() => Keys
        .Add("space", "Start/Pause", () => _running = !_running)
        .Add("r", "Reset", () => (_progress, _running) = (0, false));

    protected override void OnMount() => Animation.Every(App, TimeSpan.FromMilliseconds(30), Lifetime, () =>
    {
        _phase += 0.03;
        if (!_running) return;
        _progress = Math.Min(1, _progress + 0.01);
        if (_progress >= 1) _running = false;
    });

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var inner = Widget.Block().Title(" Progress ").Padding(new Margin(2, 1)).Focused(ctx.Focused).Draw(buf, area.Inner(new Margin(1)));
        var rows = Layout.Vertical(inner).Spacing(1)
            .Add("download", Constraint.Length(2))
            .Add("upload", Constraint.Length(2))
            .Add("cpu", Constraint.Length(2))
            .Build();
        Bar(buf, rows["download"], "Download", _progress, ctx.Theme.GaugeFilled);
        Bar(buf, rows["upload"], "Upload", _progress * 0.7, ctx.Theme.Info);
        Bar(buf, rows["cpu"], "CPU", 0.3 + 0.2 * Math.Sin(_phase), ctx.Theme.Warning);
    }

    private static void Bar(Buffer buf, Rect area, string label, double ratio, Style style)
    {
        buf.SetString(area.X, area.Y, label, Theme.Current.Text.Bold());
        Widget.Gauge().Ratio(ratio).FilledStyle(style).Draw(buf, area with { Y = area.Y + 1, Height = 1 });
    }
}

/// <summary>Text fields and a checkbox in one focus scope: Tab moves between them.</summary>
internal sealed class FormScreen : Screen
{
    private readonly TextInput _name;
    private readonly TextInput _email;
    private readonly Toggle _subscribe;

    public override string Title => "Form";

    public FormScreen()
    {
        _name = AddChild(new TextInput("Name", "Your name"));
        _email = AddChild(new TextInput("Email", "you@example.com"));
        _subscribe = AddChild(new Toggle("Subscribe to newsletter"));
        Focus.Add(_name).Add(_email).Add(_subscribe);
        Keys.Add("ctrl+s", "Submit", Submit);
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var inner = Widget.Block().Title(" Registration ").Padding(new Margin(2, 1)).Draw(buf, area.Inner(new Margin(1)));
        var rows = Layout.Vertical(inner with { Width = Math.Min(inner.Width, 40) })
            .Add("name", Constraint.Length(3))
            .Add("email", Constraint.Length(3))
            .Add("gap", Constraint.Length(1))
            .Add("subscribe", Constraint.Length(1))
            .Add("gap2", Constraint.Length(1))
            .Add("help", Constraint.Length(1))
            .Build();
        _name.Render(buf, rows["name"], ctx.For(_name));
        _email.Render(buf, rows["email"], ctx.For(_email));
        _subscribe.Render(buf, rows["subscribe"], ctx.For(_subscribe));
        buf.SetString(rows["help"].X, rows["help"].Y, "Tab next field · Space toggle · Ctrl+S submit", ctx.Theme.TextMuted);
    }

    private void Submit()
    {
        if (_name.State.Text.Length == 0)
        {
            App.Toasts.Push("Name is required", ToastLevel.Warning);
            return;
        }
        var newsletter = _subscribe.Checked ? " (subscribed)" : "";
        App.Toasts.Push($"Registered {_name.State.Text}{newsletter}", ToastLevel.Success);
    }
}

/// <summary>A layout of panels updated by an async loop.</summary>
internal sealed class DashboardScreen : Screen
{
    private static readonly string[] Stats = ["Users: 1,234", "Revenue: $45,678", "Orders: 567", "Growth: +12.3%"];
    private static readonly string[] Messages = ["Request processed", "User logged in", "Data synced", "Cache miss", "API call completed"];

    private readonly List<string> _logs = ["System started", "Connected to database", "Cache initialized"];
    private readonly List<double> _requests = [];
    private readonly System.Diagnostics.Stopwatch _uptime = new();
    private double _elapsed;

    public override string Title => "Dashboard";

    protected override void OnMount()
    {
        _uptime.Start();
        Animation.Every(App, TimeSpan.FromMilliseconds(100), Lifetime, () =>
        {
            _elapsed = _uptime.Elapsed.TotalSeconds;
            _requests.Add(20 + 15 * Math.Sin(_elapsed) + Random.Shared.Next(10));
            if (_requests.Count > 200) _requests.RemoveAt(0);
            if (Random.Shared.NextDouble() < 0.1)
            {
                _logs.Add($"{DateTime.Now:HH:mm:ss} - {Messages[Random.Shared.Next(Messages.Length)]}");
                if (_logs.Count > 50) _logs.RemoveAt(0);
            }
        });
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var layout = Layout.Vertical(area)
            .Add("stats", Constraint.Length(5))
            .Add("panels", Constraint.Fill())
            .Add("requests", Constraint.Length(5))
            .Build();

        var statsInner = Widget.Block().Title(" Statistics ").Draw(buf, layout["stats"]);
        var boxes = Layout.Split(statsInner, Direction.Horizontal, [.. Stats.Select(_ => Constraint.Fill())]);
        for (var i = 0; i < Stats.Length; i++)
        {
            var box = Widget.Block().BorderType(BoxStyle.Rounded).Draw(buf, boxes[i]);
            Widget.Paragraph().Text(new Line(Stats[i], ctx.Theme.Warning)).Alignment(Alignment.Center).Draw(buf, box);
        }

        var panels = Layout.Horizontal(layout["panels"]).Add("logs", Constraint.Fill()).Add("activity", Constraint.Fill()).Build();
        var logs = Widget.Block().Title(" Logs ").Draw(buf, panels["logs"]);
        var visible = _logs.Skip(Math.Max(0, _logs.Count - logs.Height)).ToList();
        Widget.Paragraph().Text(string.Join("\n", visible)).Draw(buf, logs);

        var activity = Widget.Block().Title(" Activity ").Padding(new Margin(1, 0)).Draw(buf, panels["activity"]);
        for (var i = 0; i < 5 && i * 2 < activity.Height; i++)
        {
            buf.SetString(activity.X, activity.Y + i * 2, $"Svc {i + 1}", ctx.Theme.TextMuted);
            Widget.Gauge().Ratio(0.5 + 0.45 * Math.Sin(_elapsed / 2 + i)).FilledStyle(ctx.Theme.Info)
                .Draw(buf, new Rect(activity.X + 7, activity.Y + i * 2, activity.Width - 7, 1));
        }

        Widget.Sparkline().Data(_requests).Block(Widget.Block().Title($" Requests/s · up {TimeSpan.FromSeconds((int)_elapsed):mm\\:ss} "))
            .Draw(buf, layout["requests"]);
    }
}

/// <summary>Notes edited through a : command line built on CommandRegistry.</summary>
internal sealed class CommandScreen : Screen
{
    private readonly List<string> _notes = [];
    private readonly SelectionState _selection = new();
    private readonly CommandRegistry _commands;

    public override string Title => "Command Registry";

    public CommandScreen()
    {
        _commands = CommandRegistry.Create()
            .Add("add", "a").Usage("<text>").Description("Add a note")
                .Handler(cmd =>
                {
                    if (string.IsNullOrWhiteSpace(cmd.ArgsAsString)) return CommandResult.Error("Usage: :add <text>");
                    _notes.Insert(0, $"[{DateTime.Now:HH:mm}] {cmd.ArgsAsString}");
                    return CommandResult.Ok("Note added");
                })
            .Add("delete", "d", "rm").Usage("<index>").Description("Delete note by index")
                .Handler(cmd =>
                {
                    if (!int.TryParse(cmd.GetArg(0), out var index) || index < 1 || index > _notes.Count)
                        return CommandResult.Error($"Invalid index. Use 1-{_notes.Count}");
                    _notes.RemoveAt(index - 1);
                    return CommandResult.Ok($"Deleted note {index}");
                })
            .Add("clear", "c").Description("Clear all notes")
                .Handler(_ =>
                {
                    _notes.Clear();
                    return CommandResult.Ok("Notes cleared");
                })
            .Add("help", "h").Description("Show the commands")
                .Handler(cmd =>
                {
                    _ = App.Overlays.ShowMessageAsync("Commands", string.Join("\n", _commands!.GetHelpLines()));
                    return CommandResult.Ok();
                })
            .Build();

        Keys.Add(":", "Command", () => App.Overlays.Show(new CommandLineOverlay(_commands)))
            .Add("x", "Delete selected", DeleteSelected);
    }

    public override bool HandleKey(ConsoleKeyInfo key) => _selection.HandleKey(key, _notes.Count);

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        IReadOnlyList<string> items = _notes.Count > 0 ? _notes : ["No notes yet. Press : and type add Hello world"];
        Widget.List().Title("Notes").Items(items).State(_selection).Focused(ctx.Focused).Draw(buf, area);
    }

    private void DeleteSelected()
    {
        if (_notes.Count == 0) return;
        _notes.RemoveAt(_selection.Selected);
        App.Toasts.Push("Note deleted");
    }
}

/// <summary>A tab bar and a list in one focus scope.</summary>
internal sealed class TabsScreen : Screen
{
    private static readonly string[][] Items =
    [
        ["Morning standup", "Code review", "Lunch break"],
        ["Sprint planning", "Retrospective", "Demo day", "1-on-1 meetings"],
        ["Q1 Goals", "Annual review", "Team offsite", "Conference", "Training"],
    ];

    private readonly TabBarState _tabs = new(3);
    private readonly Component _bar;
    private readonly ListView _list;

    public override string Title => "Tabs";

    public TabsScreen()
    {
        _bar = AddChild(Component.From(
            (buf, area, ctx) => Widget.TabBar().Tabs("Daily", "Weekly", "All").State(_tabs).Focused(ctx.Focused).Draw(buf, area),
            key => _tabs.HandleKey(key)));
        _list = AddChild(new ListView(" Tasks ", () => Items[_tabs.Selected]));
        Focus.Add(_bar).Add(_list);
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var rows = Layout.Vertical(area).Add("tabs", Constraint.Length(2)).Add("list", Constraint.Fill()).Build();
        _bar.Render(buf, rows["tabs"], ctx.For(_bar));
        _list.Render(buf, rows["list"], ctx.For(_list));
    }
}
