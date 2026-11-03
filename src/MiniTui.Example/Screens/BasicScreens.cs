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
    public override bool HandleMouse(MouseEvent mouse) => _selection.HandleMouse(mouse, Fruits.Length);

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        buf.SetString(area.X + 2, area.Y + 1, "Select a fruit:", ctx.Theme.Text.Bold());
        Widget.List().Title("Fruit").Items(Fruits).State(_selection).Focused(ctx.Focused).Hover(ctx.Hover)
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

/// <summary>Text fields, a select and a checkbox in one focus scope: Tab moves between them.</summary>
internal sealed class FormScreen : Screen
{
    private readonly TextInput _name;
    private readonly TextInput _email;
    private readonly SelectInput _plan;
    private readonly SelectInput _country;
    private readonly NotesInput _notes;
    private readonly Toggle _subscribe;

    // A filterable select: typing on the field opens it and narrows the list
    private static readonly string[] Countries =
    [
        "Argentina", "Australia", "Austria", "Belgium", "Brazil", "Canada", "Chile", "China", "Colombia", "Czechia",
        "Denmark", "Egypt", "Estonia", "Finland", "France", "Germany", "Greece", "Hungary", "Iceland", "India",
        "Indonesia", "Ireland", "Israel", "Italy", "Japan", "Kenya", "Latvia", "Lithuania", "Luxembourg", "Malaysia",
        "Mexico", "Morocco", "Netherlands", "New Zealand", "Nigeria", "Norway", "Peru", "Philippines", "Poland",
        "Portugal", "Romania", "Singapore", "South Africa", "South Korea", "Spain", "Sweden", "Switzerland",
        "Thailand", "Turkey", "Ukraine", "United Kingdom", "United States", "Vietnam",
    ];

    public override string Title => "Form";

    public FormScreen()
    {
        _name = AddChild(new TextInput("Name", "Your name"));
        _email = AddChild(new TextInput("Email", "you@example.com"));
        _plan = AddChild(new SelectInput("Plan", "Choose a plan", ["Free", "Pro", "Team", "Enterprise"]));
        _country = AddChild(new SelectInput("Country", "Type to search", Countries));
        _country.State.Filterable = true;
        _notes = AddChild(new NotesInput("Notes", "Anything else? Enter starts a new line."));
        _subscribe = AddChild(new Toggle("Subscribe to newsletter"));
        Focus.Add(_name).Add(_email).Add(_plan).Add(_country).Add(_notes).Add(_subscribe);
        // Ctrl+Enter needs a terminal with a keyboard protocol (kitty, WezTerm, foot, Windows Terminal...); Ctrl+S works everywhere
        Keys.Add("ctrl+s,ctrl+enter", "Submit", Submit);
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var inner = Widget.Block().Title(" Registration ").Padding(new Margin(2, 1)).Draw(buf, area.Inner(new Margin(1)));
        var rows = Layout.Vertical(inner with { Width = Math.Min(inner.Width, 40) })
            .Add("name", Constraint.Length(3))
            .Add("email", Constraint.Length(3))
            .Add("plan", Constraint.Length(3))
            .Add("country", Constraint.Length(3))
            .Add("notes", Constraint.Length(7))
            .Add("gap", Constraint.Length(1))
            .Add("subscribe", Constraint.Length(1))
            .Add("gap2", Constraint.Length(1))
            .Add("help", Constraint.Length(1))
            .Add("select", Constraint.Length(1))
            .Add("clipboard", Constraint.Length(1))
            .Build();
        ctx.Render(_name, buf, rows["name"]);
        ctx.Render(_email, buf, rows["email"]);
        ctx.Render(_plan, buf, rows["plan"]);
        ctx.Render(_country, buf, rows["country"]);
        ctx.Render(_notes, buf, rows["notes"]);
        ctx.Render(_subscribe, buf, rows["subscribe"]);
        buf.SetString(rows["help"].X, rows["help"].Y, "Tab next field · Space toggle · Ctrl+S or Ctrl+Enter submit", ctx.Theme.TextMuted);
        buf.SetString(rows["select"].X, rows["select"].Y, "Shift+arrows select · Ctrl+A select all", ctx.Theme.TextMuted);
        buf.SetString(rows["clipboard"].X, rows["clipboard"].Y, "Ctrl+C/X/V, Ctrl/Shift+Ins · Ctrl+Z undo", ctx.Theme.TextMuted);
    }

    private void Submit()
    {
        if (_name.State.Text.Length == 0)
        {
            App.Toasts.Push("Name is required", ToastLevel.Warning);
            return;
        }
        var newsletter = _subscribe.Checked ? " (subscribed)" : "";
        var plan = _plan.State.Value is { } p ? $" on {p}" : "";
        var country = _country.State.Value is { } c ? $" in {c}" : "";
        var notes = _notes.State.Text.Length == 0 ? "" : $", {_notes.State.Lines.Count} lines of notes";
        App.Toasts.Push($"Registered {_name.State.Text}{plan}{country}{newsletter}{notes}", ToastLevel.Success);
    }
}

/// <summary>
/// A layout of panels updated by an async loop. The log follows new lines while it's scrolled to the end, and its
/// text can be selected with the mouse and copied (1.x spec §20.6).
/// </summary>
internal sealed class DashboardScreen : Screen
{
    private static readonly string[] Stats = ["Users: 1,234", "Revenue: $45,678", "Orders: 567", "Growth: +12.3%"];
    private static readonly string[] Messages = ["Request processed", "User logged in", "Data synced", "Cache miss", "API call completed"];

    private readonly List<string> _logs = ["System started", "Connected to database", "Cache initialized"];
    private readonly List<double> _requests = [];
    private long _start;
    private readonly ScrollState _logScroll = new() { Selectable = true, Offset = int.MaxValue };
    private readonly LogView _logView;
    private double _elapsed;

    public override string Title => "Dashboard";

    public DashboardScreen()
    {
        _logView = AddChild(new LogView(this));
        Focus.Add(_logView);
        Focus.Focus(_logView);
    }

    protected override void OnMount()
    {
        _start = Demo.Time.GetTimestamp();
        Animation.Every(App, TimeSpan.FromMilliseconds(100), Lifetime, () =>
        {
            _elapsed = Demo.Time.GetElapsedTime(_start).TotalSeconds;
            _requests.Add(20 + 15 * Math.Sin(_elapsed) + Demo.Random.Next(10));
            if (_requests.Count > 200) _requests.RemoveAt(0);
            if (Demo.Random.NextDouble() < 0.1)
            {
                // At the end, stay there as the line arrives; the draw clamps the offset
                if (_logScroll.Offset >= _logScroll.ContentHeight - _logScroll.ViewportHeight) _logScroll.Offset = int.MaxValue;
                _logs.Add($"{Demo.Now:HH:mm:ss} - {Messages[Demo.Random.Next(Messages.Length)]}");
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
        ctx.Render(_logView, buf, panels["logs"]);

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

    private sealed class LogView(DashboardScreen screen) : Component
    {
        public override string KeysGroup => "Logs";
        public override bool HasSelection => screen._logScroll.HasSelection;
        public override bool HandleKey(ConsoleKeyInfo key) => screen._logScroll.HandleKey(key);
        public override bool HandleMouse(MouseEvent mouse) => screen._logScroll.HandleMouse(mouse);

        public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
            Widget.Paragraph().Text(string.Join("\n", screen._logs)).State(screen._logScroll)
                .Block(Widget.Block().Title(" Logs ").TitleBottom(" drag to select · Ctrl+C copies ").Focused(ctx.Focused))
                .Draw(buf, area);
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
                    _notes.Insert(0, $"[{Demo.Now:HH:mm}] {cmd.ArgsAsString}");
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

        // Ctrl+P here shadows the global palette, adding this screen's commands to the bindings
        Keys.Add(":", "Command", () => App.Overlays.Show(new CommandLineOverlay(_commands)))
            .Add("ctrl+p", "Commands", () => App.Overlays.ShowPalette(_commands))
            .Add("x", "Delete selected", DeleteSelected)
            .Add("shift+f10", "Menu", () => ShowMenu(SelectedRow()));
    }

    public override bool HandleKey(ConsoleKeyInfo key) => _selection.HandleKey(key, _notes.Count);

    // A right-click selects the note under the pointer, then opens the menu there
    public override bool HandleMouse(MouseEvent mouse)
    {
        if (mouse is not { Action: MouseAction.Press, Button: MouseButton.Right })
            return _selection.HandleMouse(mouse, _notes.Count);
        _selection.HandleMouse(mouse with { Button = MouseButton.Left }, _notes.Count);
        ShowMenu(new Rect(mouse.X, mouse.Y, 1, 1));
        return true;
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        IReadOnlyList<string> items = _notes.Count > 0 ? _notes : ["No notes yet. Press : and type add Hello world"];
        Widget.List().Title("Notes").Items(items).State(_selection).Focused(ctx.Focused).Hover(ctx.Hover).Draw(buf, area);
    }

    // The selected note's row inside the list's border
    private Rect SelectedRow() =>
        new(Area.X + 1, Area.Y + 1 + _selection.Selected - _selection.Offset, Math.Max(0, Area.Width - 2), 1);

    // Mnemonics (the underlined letters choose at once), a Sort submenu of radio items, and Run actions
    private void ShowMenu(Rect anchor)
    {
        var any = _notes.Count > 0;
        MenuItem[] items =
        [
            new("&Delete") { Hint = "x", Enabled = any, Run = DeleteSelected },
            new("Delete &all") { Enabled = any, Run = () => { _notes.Clear(); App.Toasts.Push("Notes cleared"); } },
            new("&Sort")
            {
                Enabled = _notes.Count > 1,
                Submenu =
                [
                    SortItem("&Newest first", NoteOrder.Newest),
                    SortItem("&Oldest first", NoteOrder.Oldest),
                    SortItem("&Alphabetical", NoteOrder.Alphabetical),
                ],
            },
            MenuItem.Separator,
            new("&Help") { Hint = ":help", Run = () => _ = App.Overlays.ShowMessageAsync("Commands", string.Join("\n", _commands.GetHelpLines())) },
        ];
        App.Overlays.Show(new PopupMenu(items, anchor));
    }

    private MenuItem SortItem(string label, NoteOrder order) =>
        new(label) { Radio = true, Checked = _order == order, Run = () => Sort(order) };

    private void Sort(NoteOrder order)
    {
        _order = order;
        // Notes start with their [HH:mm] time, so ordinal order is oldest first
        _notes.Sort(StringComparer.Ordinal);
        if (order == NoteOrder.Newest) _notes.Reverse();
        else if (order == NoteOrder.Alphabetical) _notes.Sort((a, b) => string.Compare(a[(a.IndexOf(']') + 1)..], b[(b.IndexOf(']') + 1)..], StringComparison.OrdinalIgnoreCase));
    }

    private enum NoteOrder { Newest, Oldest, Alphabetical }

    private NoteOrder _order;

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
            (buf, area, ctx) => Widget.TabBar().Tabs("Daily", "Weekly", "All").State(_tabs).Focused(ctx.Focused).Hover(ctx.Hover).Draw(buf, area),
            key => _tabs.HandleKey(key)));
        _list = AddChild(new ListView(" Tasks ", () => Items[_tabs.Selected]));
        Focus.Add(_bar).Add(_list);
    }

    // The tab bar is an inline component, so its clicks come here
    public override bool HandleMouse(MouseEvent mouse) => _tabs.HandleMouse(mouse);

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var rows = Layout.Vertical(area).Add("tabs", Constraint.Length(2)).Add("list", Constraint.Fill()).Build();
        ctx.Render(_bar, buf, rows["tabs"]);
        ctx.Render(_list, buf, rows["list"]);
    }
}
