using System.Diagnostics;
using MiniTui;

namespace MiniTui.Example;

/// <summary>
/// Shows every key as MiniTui decoded it (1.x spec §2.8), for checking a terminal and for bug reports.
/// Esc is just another key here; q goes back.
/// </summary>
internal sealed class InputScreen : Screen
{
    private const int MaxEntries = 20;
    private static readonly KeyBinding Back = KeyBinding.Parse("q");
    private static readonly KeyBinding Quit = KeyBinding.Parse("ctrl+c");
    private static readonly KeyBinding RunChild = KeyBinding.Parse("ctrl+r");

    private readonly List<Line> _entries = [];

    public override string Title => "Input";

    public override bool PopOnEscape => false;

    public override bool CapturesText => true;

    public override bool HandleKey(ConsoleKeyInfo key)
    {
        if (Back.Matches(key))
        {
            App.Screens.Pop();
            return true;
        }

        Add(Describe(key));
        if (RunChild.Matches(key)) App.RunTask(RunChildProcessAsync);

        // Let Ctrl+C reach the global binding, so it still quits
        return !Quit.Matches(key);
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var inner = Widget.Block().Title(" Input ").Padding(new Margin(1, 0)).Draw(buf, area);
        var rows = Layout.Vertical(inner)
            .Add("help", Constraint.Length(3))
            .Add("keys", Constraint.Fill())
            .Build();

        Widget.Paragraph()
            .Text(new Text([
                new Line("Press any key to see how it was decoded. q goes back, Ctrl+C quits.", ctx.Theme.Text),
                new Line("Ctrl+R runs a child process, then keys should still decode (QA check 6).", ctx.Theme.TextMuted),
            ]))
            .Wrap(WrapMode.Word)
            .Draw(buf, rows["help"]);

        Widget.Paragraph().Text(new Text(_entries)).Draw(buf, rows["keys"]);
    }

    private void Add(Line line)
    {
        _entries.Insert(0, line);
        if (_entries.Count > MaxEntries) _entries.RemoveAt(_entries.Count - 1);
    }

    private static Line Describe(ConsoleKeyInfo key)
    {
        var modifiers = key.Modifiers == 0 ? "" : key.Modifiers.ToString().Replace(", ", "+", StringComparison.Ordinal);
        var shown = char.IsControl(key.KeyChar) || char.IsSurrogate(key.KeyChar) ? "" : $" '{key.KeyChar}'";
        return new Line([
            new Span($"{key.Key,-12}", Theme.Current.Info),
            new Span($" U+{(int)key.KeyChar:X4}{shown,-4}", Theme.Current.Text),
            new Span($"  {modifiers}", Theme.Current.Warning),
        ]);
    }

    private async Task RunChildProcessAsync(CancellationToken cancellationToken)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c exit 0")
            : new ProcessStartInfo("true");
        start.UseShellExecute = false;
        using var process = Process.Start(start);
        if (process is not null) await process.WaitForExitAsync(cancellationToken);
        Add(new Line($"Child process exited with code {process?.ExitCode}", Theme.Current.Success));
    }
}
