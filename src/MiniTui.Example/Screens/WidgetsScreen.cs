using MiniTui;

namespace MiniTui.Example;

/// <summary>Every widget on one screen, a theme toggle and a Unicode sample panel (§10.2).</summary>
internal sealed class WidgetsScreen : Screen
{
    private static readonly string[] Fruits =
    [
        "Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape", "Honeydew", "Kiwi", "Lemon",
        "Mango", "Nectarine", "Orange", "Papaya", "Quince", "Raspberry", "Strawberry", "Tangerine",
    ];

    private readonly TabBarState _tabs = new(2);
    private readonly SelectionState _list = new();
    private readonly SelectionState _table = new() { SortColumn = 0 };
    private readonly List<double> _samples = [];
    private readonly Component _tabBar;
    private readonly Component _items;
    private readonly TextInput _name;
    private readonly Toggle _agree;
    private readonly Component _greet;
    private bool _monochrome;
    private double _progress;

    public override string Title => "Widgets";

    public WidgetsScreen()
    {
        _tabBar = AddChild(Component.From(
            (buf, area, ctx) => Widget.TabBar().Tabs("List", "Table").State(_tabs).Focused(ctx.Focused).Draw(buf, area),
            key => _tabs.HandleKey(key)));
        _items = AddChild(Component.From(DrawItems, HandleItemKey));
        _name = AddChild(new TextInput("Name", "Your name (any script: 日本, émoji 😀)"));
        _agree = AddChild(new Toggle("I agree"));
        _greet = AddChild(Component.From(
            (buf, area, ctx) => Widget.Button().Label("Greet").Focused(ctx.Focused).Draw(buf, area),
            key => key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar && Greet()));
        Focus.Add(_tabBar).Add(_items).Add(_name).Add(_agree).Add(_greet);
        Focus.Focus(_items);

        Keys.Add("t", "Theme", ToggleTheme)
            .Add("n", "Toast", () => App.Toasts.Push($"Toast at {DateTime.Now:HH:mm:ss}", (ToastLevel)(_samples.Count % 4)));
    }

    protected override void OnMount() => Animation.Every(App, TimeSpan.FromMilliseconds(200), Lifetime, () =>
    {
        _progress = (_progress + 0.013) % 1.0;
        _samples.Add(50 + 40 * Math.Sin(_samples.Count / 4.0) + Random.Shared.Next(-10, 10));
        if (_samples.Count > 200) _samples.RemoveAt(0);
    });

    // Leave the app's theme as we found it
    protected override void OnUnmount()
    {
        if (_monochrome) App.WithTheme(Theme.Default);
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var rows = Layout.Vertical(area).Add("tabs", Constraint.Length(2)).Add("body", Constraint.Fill()).Build();
        var columns = Layout.Horizontal(rows["body"]).Add("left", Constraint.Percentage(50)).Add("right", Constraint.Fill()).Build();
        var left = Layout.Vertical(columns["left"])
            .Add("items", Constraint.Fill())
            .Add("name", Constraint.Length(3))
            .Add("controls", Constraint.Length(1))
            .Build();
        var right = Layout.Vertical(columns["right"])
            .Add("gauge", Constraint.Length(3))
            .Add("sparkline", Constraint.Length(5))
            .Add("spinner", Constraint.Length(1))
            .Add("unicode", Constraint.Fill())
            .Build();

        _tabBar.Render(buf, rows["tabs"], ctx.For(_tabBar));
        _items.Render(buf, left["items"], ctx.For(_items));
        _name.Render(buf, left["name"], ctx.For(_name));
        var controls = Layout.Split(left["controls"], Direction.Horizontal, Constraint.Fill(), Constraint.Length(12));
        _agree.Render(buf, controls[0], ctx.For(_agree));
        _greet.Render(buf, controls[1], ctx.For(_greet));

        Widget.Gauge().Ratio(_progress).Block(Widget.Block().Title(" Gauge ")).Draw(buf, right["gauge"]);
        Widget.Sparkline().Data(_samples).Max(100).Block(Widget.Block().Title(" Sparkline ")).Draw(buf, right["sparkline"]);
        var spinners = Layout.Split(right["spinner"], Direction.Horizontal, Constraint.Fill(), Constraint.Fill(), Constraint.Fill());
        Widget.Spinner().Frames(SpinnerFrames.Braille).Label("braille").Draw(buf, spinners[0]);
        Widget.Spinner().Frames(SpinnerFrames.Line).Label("line").Draw(buf, spinners[1]);
        Widget.Spinner().Frames(SpinnerFrames.Dots).Label("dots").Draw(buf, spinners[2]);

        Widget.Paragraph()
            .Text(new Text([
                new Line("CJK:      日本語 中文 한국어", ctx.Theme.Info),
                "Emoji:    😀 👍🏽 👨‍👩‍👧 🇯🇵",
                "Combining: é ñ ä (é ñ ä)",
                "Box:      ┌─┬─┐ ╭─╮ ═══",
                new Line("Long lines wrap at word boundaries, and words wider than the panel are broken.", ctx.Theme.TextMuted),
                "",
                new Line($"Theme: {(_monochrome ? "monochrome" : "default")} (t toggles)", ctx.Theme.TextMuted),
            ]))
            .Wrap(WrapMode.Word)
            .Block(Widget.Block().Title(" Unicode ").BorderType(BoxStyle.Rounded))
            .Draw(buf, right["unicode"]);
    }

    private void DrawItems(Buffer buf, Rect area, RenderContext ctx)
    {
        if (_tabs.Selected == 0)
        {
            Widget.List().Title(" Fruit ").Items(Fruits).State(_list).HighlightSymbol("> ").Focused(ctx.Focused).Draw(buf, area);
            return;
        }

        IReadOnlyList<string> data = _table.SortDescending ? [.. Fruits.Reverse()] : Fruits;
        Widget.Table()
            .Block(Widget.Block().Title(" Fruit table (s sorts) ").Focused(ctx.Focused))
            .Header("Name", "Letters", "Long")
            .Columns(Constraint.Fill(), Constraint.Length(7), Constraint.Length(6))
            .Rows(data, f => new Row(f, new Line($"{f.Length}") with { Alignment = Alignment.Right }, f.Length > 6 ? "yes" : "no"))
            .State(_table)
            .Focused(ctx.Focused)
            .Draw(buf, area);
    }

    private bool HandleItemKey(ConsoleKeyInfo key)
    {
        if (_tabs.Selected == 0) return _list.HandleKey(key, Fruits.Length);
        if (key.KeyChar == 's')
        {
            _table.SortDescending = !_table.SortDescending;
            return true;
        }
        return _table.HandleKey(key, Fruits.Length);
    }

    private bool Greet()
    {
        var who = _name.State.Text.Length > 0 ? _name.State.Text : "stranger";
        App.Toasts.Push(_agree.Checked ? $"Hello, {who}!" : "Tick the checkbox first", _agree.Checked ? ToastLevel.Success : ToastLevel.Warning);
        return true;
    }

    private void ToggleTheme()
    {
        _monochrome = !_monochrome;
        App.WithTheme(_monochrome ? Theme.Monochrome : Theme.Default);
    }
}
