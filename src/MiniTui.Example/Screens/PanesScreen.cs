using System.Globalization;
using MiniTui;

namespace MiniTui.Example;

/// <summary>
/// Split panes (1.x spec §14): a fixed-width list of log sources beside a sortable table of entries above the
/// selected entry's details. Drag the dividers and scrollbars, or move the dividers with Alt+Shift+arrows.
/// </summary>
internal sealed class PanesScreen : Screen
{
    private static readonly string[] Sources = ["All", "api", "auth", "billing", "cache", "db", "queue", "web"];
    private static readonly string[] Levels = ["INFO", "INFO", "INFO", "WARN", "ERROR", "DEBUG"];
    private static readonly string[] Messages =
    [
        "Request completed", "Cache miss for key", "Retrying after timeout", "Connection pool exhausted",
        "User signed in", "Invoice generated", "Message requeued", "Slow query detected", "Token refreshed",
    ];
    private static readonly string[] Columns = ["Time", "Level", "Source", "Message"];

    private readonly SplitState _sidebar = SplitState.Fixed(Direction.Horizontal, 24);
    private readonly SplitState _detail = new(Direction.Vertical, 0.6) { MinSecond = 4 };
    private readonly List<LogEntry> _all;
    private readonly ListView _sources;
    private readonly LogTable _table;
    private readonly Detail _details;

    public override string Title => "Panes";

    public PanesScreen()
    {
        var start = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
        _all = [.. Enumerable.Range(0, 200).Select(i => new LogEntry(
            start.AddSeconds(i * 37 % 3600),
            Levels[i * 7 % Levels.Length],
            Sources[1 + i * 5 % (Sources.Length - 1)],
            $"{Messages[i * 11 % Messages.Length]} #{i}"))];

        _sources = AddChild(new ListView(" Sources ", () => Sources) { PassAltKeys = true });
        _table = AddChild(new LogTable(Visible));
        _details = AddChild(new Detail(() => _table.SelectedEntry));
        Focus.Add(_sources).Add(_table).Add(_details);
        // A double-click on a log row moves focus to its details, where ↑ ↓ scroll them
        _table.Selection.Activated += (_, _) => Focus.Focus(_details);

        Keys.Add("s", "Sort", () => _table.Selection.ToggleSort(((_table.Selection.SortColumn ?? -1) + 1) % Columns.Length));
    }

    private IReadOnlyList<LogEntry> Visible()
    {
        var source = Sources[_sources.Selection.Selected];
        var entries = source == "All" ? _all : [.. _all.Where(e => e.Source == source)];
        return _table.Sorted(entries);
    }

    // Alt+Shift+← → moves the sidebar, Alt+Shift+↑ ↓ the details
    public override bool HandleKey(ConsoleKeyInfo key) => _sidebar.HandleKey(key) || _detail.HandleKey(key);

    public override bool HandleMouse(MouseEvent mouse) => _sidebar.HandleMouse(mouse) || _detail.HandleMouse(mouse);

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var body = area with { Height = Math.Max(0, area.Height - 1) };
        var (left, right) = Widget.Split(_sidebar).Draw(buf, body);
        ctx.Render(_sources, buf, left);
        var (top, bottom) = Widget.Split(_detail).Draw(buf, right);
        ctx.Render(_table, buf, top);
        ctx.Render(_details, buf, bottom);
        buf.SetString(area.X, area.Bottom - 1,
            "Drag the dividers and scrollbars · Alt+Shift+arrows move the dividers · click a header or s to sort",
            ctx.Theme.TextMuted, area.Width);
    }

    // The panes leave Alt+arrows to the screen's splits; the list and scroll states take arrows with any modifier
    private static bool IsAlt(ConsoleKeyInfo key) => (key.Modifiers & ConsoleModifiers.Alt) != 0;

    private sealed record LogEntry(DateTime Time, string Level, string Source, string Message);

    /// <summary>The entries, sortable by a header click or s.</summary>
    private sealed class LogTable(Func<IReadOnlyList<LogEntry>> entries) : Component
    {
        public SelectionState Selection { get; } = new();
        public override string KeysGroup => "Log";

        public LogEntry? SelectedEntry => entries() is { Count: > 0 } list ? list[Math.Min(Selection.Selected, list.Count - 1)] : null;

        public IReadOnlyList<LogEntry> Sorted(IReadOnlyList<LogEntry> items)
        {
            if (Selection.SortColumn is not { } column) return items;
            Func<LogEntry, string> key = column switch
            {
                0 => e => e.Time.ToString("O", CultureInfo.InvariantCulture),
                1 => e => e.Level,
                2 => e => e.Source,
                _ => e => e.Message,
            };
            var sorted = Selection.SortDescending ? items.OrderByDescending(key, StringComparer.Ordinal) : items.OrderBy(key, StringComparer.Ordinal);
            return [.. sorted.ThenBy(e => e.Time)];
        }

        public override bool HandleKey(ConsoleKeyInfo key) => !IsAlt(key) && Selection.HandleKey(key, entries().Count);
        public override bool HandleMouse(MouseEvent mouse) => Selection.HandleMouse(mouse, entries().Count);

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            var list = entries();
            Widget.Table()
                .Header([.. Columns.Select(c => new Line(c))])
                .Columns(Constraint.Length(8), Constraint.Length(7), Constraint.Length(9), Constraint.Fill())
                .Rows(list, e => new Row(
                    e.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    new Line(e.Level, e.Level switch { "ERROR" => ctx.Theme.Error, "WARN" => ctx.Theme.Warning, _ => ctx.Theme.TextMuted }),
                    e.Source,
                    e.Message))
                .State(Selection)
                .Sortable()
                .Block(Widget.Block().Title($" Log ({list.Count}) "))
                .Focused(ctx.Focused)
                .Draw(buf, area);
        }
    }

    /// <summary>The selected entry, with a long fake trace to scroll.</summary>
    private sealed class Detail(Func<LogEntry?> entry) : Component
    {
        private readonly ScrollState _scroll = new();
        public override string KeysGroup => "Details";
        public override bool HandleKey(ConsoleKeyInfo key) => !IsAlt(key) && _scroll.HandleKey(key);
        public override bool HandleMouse(MouseEvent mouse) => _scroll.HandleMouse(mouse);

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            var text = entry() is { } e
                ? string.Join("\n", [$"{e.Time:O}  {e.Level}  {e.Source}", e.Message, "", .. Enumerable.Range(1, 30).Select(i => $"  at Example.{e.Source}.Step{i}()")])
                : "No entries";
            Widget.Paragraph().Text(text).State(_scroll).Block(Widget.Block().Title(" Details ").Focused(ctx.Focused)).Draw(buf, area);
        }
    }
}
