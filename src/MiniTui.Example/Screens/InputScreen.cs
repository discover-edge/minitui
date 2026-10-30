using System.Diagnostics;
using System.Text;
using MiniTui;

namespace MiniTui.Example;

/// <summary>
/// Shows every key, paste and mouse event as MiniTui decoded it (1.x spec §2.8, §3.8, §4.10), for checking a terminal
/// and for bug reports. Ctrl+E opens a file in the user's editor through <see cref="App.Suspend"/> (§5.8).
/// Esc is just another key here; q goes back.
/// </summary>
internal sealed class InputScreen : Screen
{
    private const int MaxEntries = 20;
    private static readonly KeyBinding Back = KeyBinding.Parse("q");
    private static readonly KeyBinding Quit = KeyBinding.Parse("ctrl+c");
    private static readonly KeyBinding RunChild = KeyBinding.Parse("ctrl+r");
    private static readonly KeyBinding Edit = KeyBinding.Parse("ctrl+e");
    private static readonly KeyBinding Suspend = KeyBinding.Parse("ctrl+z");

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
        if (Edit.Matches(key)) EditFile();

        // Let Ctrl+C reach the global binding, so it still quits, and Ctrl+Z the app, so it suspends
        return !Quit.Matches(key) && !Suspend.Matches(key);
    }

    public override bool HandlePaste(string text)
    {
        var lines = text.Count(c => c == '\n') + 1;
        var preview = string.Concat(text.EnumerateRunes().Take(40).Select(r => Rune.IsControl(r) ? " " : r.ToString()));
        Add(new Line([
            new Span($"{"Paste",-12}", Theme.Current.Info),
            new Span($" {text.Length} chars, {lines} {(lines == 1 ? "line" : "lines")}: ", Theme.Current.Text),
            new Span(preview, Theme.Current.TextMuted),
        ]));
        return true;
    }

    public override bool HandleMouse(MouseEvent mouse)
    {
        var modifiers = mouse.Modifiers == 0 ? "" : mouse.Modifiers.ToString().Replace(", ", "+", StringComparison.Ordinal);
        var button = mouse.Button == MouseButton.None ? "" : $" {mouse.Button}";
        Add(new Line([
            new Span($"{"Mouse",-12}", Theme.Current.Info),
            new Span($" {mouse.Action}{button}", Theme.Current.Text),
            new Span($"  ({mouse.X}, {mouse.Y})", Theme.Current.Text),
            new Span($"  {modifiers}", Theme.Current.Warning),
        ]));
        return true;
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
                new Line("Press any key, paste or use the mouse to see how it was decoded. q goes back, Ctrl+C quits.", ctx.Theme.Text),
                new Line("Ctrl+R runs a child process, then keys should still decode (QA check 6). Ctrl+E edits a file in "
                         + "$VISUAL or $EDITOR.", ctx.Theme.TextMuted),
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

    // The editor gets the real terminal while it runs, then the app takes it back and redraws
    private void EditFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "minitui-notes.txt");
        if (!File.Exists(path)) File.WriteAllText(path, "Edit this file, save it and quit the editor.\n");

        string? error = null;
        App.Suspend(() =>
        {
            try
            {
                using var editor = Process.Start(EditorFor(path));
                editor?.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                error = ex.Message;
            }
        });

        var text = error is null ? File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd('\n') : $"The editor didn't start: {error}";
        App.Overlays.Show(new MessageDialog("Edited file", new Text([.. text.Split('\n').Select(l => new Line(l))])));
    }

    // $VISUAL or $EDITOR may include arguments ("code --wait"), so a shell runs it, as git does
    private static ProcessStartInfo EditorFor(string path)
    {
        var editor = Environment.GetEnvironmentVariable("VISUAL") is { Length: > 0 } visual ? visual
            : Environment.GetEnvironmentVariable("EDITOR") is { Length: > 0 } set ? set
            : OperatingSystem.IsWindows() ? "notepad" : "vi";
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c {editor} \"{path}\"")
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", editor + " \"$1\"", "sh", path } };
        start.UseShellExecute = false;
        return start;
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
