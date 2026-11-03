using MiniTui;
using MiniTui.Example;
using SiteMedia;
using Buffer = MiniTui.Buffer;

// The site's recordings and screenshots (1.x spec §21.4), generated from the example app so they never drift from the
// code. Usage: dotnet run --project tools/SiteMedia -- <output directory, default site/media>
var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("site", "media"));
Directory.CreateDirectory(output);

// The Tree demo shows the directory it runs in: the repository
var root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "src", "MiniTui.sln"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Run from inside the repository");
Environment.CurrentDirectory = root;

// The landing page's tour: the menu, the Dashboard, the Editor's find, the Tree and the Widgets
using (var r = new Recorder(() => new MenuScreen()))
{
    r.Wait(1.2).Press("down down down down", 0.4).Press("enter", 4.5)
        .Press("esc", 0.5).Press("down down", 0.3).Press("enter", 1.2)
        .Press("ctrl+f", 0.3).Type("port", 0.5).Press("enter", 1.0).Press("esc esc esc", 0.5)
        .Press("down down down", 0.3).Press("enter", 1.0).Type("src", 0.4).Press("right", 0.8).Press("down down right", 1.5)
        .Press("esc", 0.5).Press("down down down", 0.3).Press("enter", 2.5).Press("t", 2.5);
    r.SaveCast(Path.Combine(output, "tour.cast"));
}

Shot("widgets.svg", "Widgets", () => new WidgetsScreen(), r => r.Wait(6));
Shot("screens.svg", "Command palette", () => new MenuScreen(), r => r.Press("ctrl+p", 0.2).Type("tre", 0.2));
Shot("layout.svg", "Split panes", () => new PanesScreen(), r => r.Wait(0.5));
Shot("async.svg", "Services", () => new ServicesScreen(), r => r.Wait(2), seed: 1);
Shot("terminal.svg", "Editor", () => new EditorScreen(), r => r.Press("ctrl+f", 0.2).Type("port", 0.2).Press("enter", 0.2));
QuickStart(Path.Combine(output, "counter.svg"));
Console.WriteLine($"Wrote the site's media to {output}");

void Shot(string file, string title, Func<Screen> screen, Func<Recorder, Recorder> script, int width = 100, int height = 28, bool demoKeys = true, int seed = 27)
{
    using var r = new Recorder(screen, width, height, demoKeys, seed);
    script(r).SaveSvg(Path.Combine(output, file), title);
}

// The Quick Start's counter (README, guides/getting-started.md) after three presses of k: its render callback as is,
// drawn into a buffer, without an app around it
static void QuickStart(string path)
{
    var count = 3;
    var keys = new KeyMap()
        .Add("k,up", "+1", () => count++)
        .Add("j,down", "-1", () => count--)
        .Add("q", "Quit", () => { });
    var buf = new Buffer(40, 8);
    var screen = buf.Area;
    var inner = Widget.Block().Title(" Counter ").Draw(buf, new Rect(2, 1, 30, 5));
    buf.SetString(inner.X + 1, inner.Y + 1, $"Count: {count}", Style.Default.Bold());
    Widget.StatusBar().Left(keys.Hints(screen.Width)).Draw(buf, screen);
    File.WriteAllText(path, Svg.Draw(buf.Get, buf.Width, buf.Height, "Counter"));
}
