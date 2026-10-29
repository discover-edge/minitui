using MiniTui;

namespace MiniTui.Example;

/// <summary>The first screen: pick a demo to push.</summary>
internal sealed class MenuScreen : Screen
{
    private static readonly (string Name, string Description, Func<Screen> Create)[] Demos =
    [
        ("Counter", "The smallest screen: state and key bindings", () => new CounterScreen()),
        ("List Selection", "A list with a selection that scrolls", () => new ListScreen()),
        ("Progress Bars", "Gauges animated from an async loop", () => new ProgressScreen()),
        ("Form", "Text fields and a checkbox; Tab moves focus", () => new FormScreen()),
        ("Dashboard", "Layout, live logs, gauges and a sparkline", () => new DashboardScreen()),
        ("Command Registry", "A : command line over a list of notes", () => new CommandScreen()),
        ("Tabs", "A tab bar and a list in one focus scope", () => new TabsScreen()),
        ("Services (async)", "Loading, refresh, detail screens and confirm dialogs over a fake HTTP API", () => new ServicesScreen()),
        ("Widgets", "Every widget, a theme toggle and a Unicode panel", () => new WidgetsScreen()),
    ];

    private readonly SelectionState _selection = new();

    public override string Title => "MiniTui Demos";

    public MenuScreen() => Keys.Add("enter", "Open", () => App.Screens.Push(Demos[_selection.Selected].Create()));

    public override bool HandleKey(ConsoleKeyInfo key) => _selection.HandleKey(key, Demos.Length);

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var inner = Widget.Block().Title(" MiniTui Demos ").Padding(new Margin(1, 0)).Draw(buf, area);
        var layout = Layout.Vertical(inner)
            .Add("heading", Constraint.Length(2))
            .Add("body", Constraint.Fill())
            .Build();
        buf.SetString(inner.X, inner.Y, "Select a demo:", ctx.Theme.Text.Bold());

        var columns = Layout.Horizontal(layout["body"]).Spacing(2)
            .Add("list", Constraint.Length(30))
            .Add("about", Constraint.Fill())
            .Build();
        Widget.List().Items(Demos, d => d.Name).State(_selection).HighlightSymbol("> ").Focused(ctx.Focused)
            .Draw(buf, columns["list"] with { Height = Math.Min(columns["list"].Height, Demos.Length + 2) });
        Widget.Paragraph()
            .Text(new Text([new Line(Demos[_selection.Selected].Description, ctx.Theme.TextMuted)]))
            .Wrap(WrapMode.Word)
            .Draw(buf, columns["about"] with { Y = columns["about"].Y + 1 });
    }
}
