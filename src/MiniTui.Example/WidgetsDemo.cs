using MiniTui;

// Every Phase 5 widget on one screen, a theme toggle and a Unicode sample panel (§10.2).
internal static class WidgetsDemo
{
    private enum Focus { Tabs, Items, Name, Agree, Button }

    private static readonly string[] Fruits =
    [
        "Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape", "Honeydew", "Kiwi", "Lemon",
        "Mango", "Nectarine", "Orange", "Papaya", "Quince", "Raspberry", "Strawberry", "Tangerine",
    ];

    public static void Run()
    {
        var tabs = new TabBarState(2);
        var list = new SelectionState();
        var table = new SelectionState { SortColumn = 0 };
        var name = new TextFieldState();
        var toasts = new ToastQueue();
        var agree = false;
        var focus = Focus.Items;
        var monochrome = false;
        var progress = 0.0;
        var samples = new List<double>();
        var app = App.Create().WithTickRate(TimeSpan.FromMilliseconds(200));

        var keys = new KeyMap()
            .Add("tab", "Focus", () => focus = (Focus)(((int)focus + 1) % 5))
            .Add("shift+tab", "Focus back", () => focus = (Focus)(((int)focus + 4) % 5), showInHints: false)
            .Add("t", "Theme", () => app.WithTheme((monochrome = !monochrome) ? Theme.Monochrome : Theme.Default))
            .Add("n", "Toast", () => toasts.Push($"Toast at {DateTime.Now:HH:mm:ss}", (ToastLevel)(samples.Count % 4)))
            .Add("q,esc", "Back", () => app.Quit());

        IReadOnlyList<string> sorted() => table.SortDescending ? [.. Fruits.Reverse()] : Fruits;

        bool HandleFocused(ConsoleKeyInfo key)
        {
            switch (focus)
            {
                case Focus.Tabs:
                    return tabs.HandleKey(key);
                case Focus.Items when tabs.Selected == 0:
                    return list.HandleKey(key, Fruits.Length);
                case Focus.Items:
                    if (key.KeyChar == 's') { table.SortDescending = !table.SortDescending; return true; }
                    return table.HandleKey(key, Fruits.Length);
                case Focus.Name:
                    return key.Key != ConsoleKey.Tab && key.Key != ConsoleKey.Escape && name.HandleKey(key);
                case Focus.Agree when key.Key is ConsoleKey.Spacebar or ConsoleKey.Enter:
                    agree = !agree;
                    return true;
                case Focus.Button when key.Key is ConsoleKey.Spacebar or ConsoleKey.Enter:
                    var who = name.Text.Length > 0 ? name.Text : "stranger";
                    toasts.Push(agree ? $"Hello, {who}!" : "Tick the checkbox first", agree ? ToastLevel.Success : ToastLevel.Warning);
                    return true;
                default:
                    return false;
            }
        }

        app.OnKey(key => HandleFocused(key) || keys.Handle(key))
            .OnEvent(e =>
            {
                if (e is not TickEvent) return;
                progress = (progress + 0.013) % 1.0;
                samples.Add(50 + 40 * Math.Sin(samples.Count / 4.0) + Random.Shared.Next(-10, 10));
                if (samples.Count > 200) samples.RemoveAt(0);
            })
            .Render((buf, screen) =>
            {
                var rows = Layout.Vertical(screen)
                    .Add("tabs", Constraint.Length(2))
                    .Add("body", Constraint.Fill())
                    .Add("status", Constraint.Length(1))
                    .Build();
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

                Widget.TabBar().Tabs("List", "Table").State(tabs).Focused(focus == Focus.Tabs).Draw(buf, rows["tabs"]);

                if (tabs.Selected == 0)
                {
                    Widget.List().Title(" Fruit ").Items(Fruits).State(list).HighlightSymbol("> ")
                        .Focused(focus == Focus.Items).Draw(buf, left["items"]);
                }
                else
                {
                    var data = sorted();
                    Widget.Table()
                        .Block(Widget.Block().Title(" Fruit table (s sorts) ").Focused(focus == Focus.Items))
                        .Header("Name", "Letters", "Wide")
                        .Columns(Constraint.Fill(), Constraint.Length(7), Constraint.Length(6))
                        .Rows(data, f => new Row(f, new Line($"{f.Length}") with { Alignment = Alignment.Right }, f.Length > 6 ? "yes" : "no"))
                        .State(table)
                        .Focused(focus == Focus.Items)
                        .Draw(buf, left["items"]);
                }

                Widget.TextField().State(name).Placeholder("Your name (any script: 日本, émoji 😀)")
                    .Block(Widget.Block().Title(" Name ")).Focused(focus == Focus.Name).Draw(buf, left["name"]);
                var controls = Layout.Split(left["controls"], Direction.Horizontal, Constraint.Fill(), Constraint.Length(12));
                Widget.Checkbox().Label("I agree").Checked(agree).Focused(focus == Focus.Agree).Draw(buf, controls[0]);
                Widget.Button().Label("Greet").Focused(focus == Focus.Button).Draw(buf, controls[1]);

                Widget.Gauge().Ratio(progress).Block(Widget.Block().Title(" Gauge ")).Draw(buf, right["gauge"]);
                Widget.Sparkline().Data(samples).Max(100).Block(Widget.Block().Title(" Sparkline ")).Draw(buf, right["sparkline"]);
                var spinners = Layout.Split(right["spinner"], Direction.Horizontal, Constraint.Fill(), Constraint.Fill(), Constraint.Fill());
                Widget.Spinner().Frames(SpinnerFrames.Braille).Label("braille").Draw(buf, spinners[0]);
                Widget.Spinner().Frames(SpinnerFrames.Line).Label("line").Draw(buf, spinners[1]);
                Widget.Spinner().Frames(SpinnerFrames.Dots).Label("dots").Draw(buf, spinners[2]);

                Widget.Paragraph()
                    .Text(new Text([
                        new Line("CJK:      日本語 中文 한국어", Theme.Current.Info),
                        "Emoji:    😀 👍🏽 👨‍👩‍👧 🇯🇵",
                        "Combining: é ñ ä (é ñ ä)",
                        "Box:      ┌─┬─┐ ╭─╮ ═══",
                        new Line("Long lines wrap at word boundaries, and words wider than the panel are broken.", Theme.Current.TextMuted),
                    ]))
                    .Wrap(WrapMode.Word)
                    .Block(Widget.Block().Title(" Unicode ").BorderType(BoxStyle.Rounded))
                    .Draw(buf, right["unicode"]);

                var theme = monochrome ? "monochrome" : "default";
                Widget.StatusBar().Left(keys.Hints(screen.Width - 24)).Right($"theme: {theme}").Draw(buf, rows["status"]);
                Widget.Toasts().Queue(toasts).Draw(buf, screen);
            })
            .Run();
    }
}
