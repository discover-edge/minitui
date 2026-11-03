using MiniTui;

namespace MiniTui.Example;

/// <summary>A live line chart, a bar chart and a canvas, animated by an async loop.</summary>
internal sealed class ChartsScreen : Screen
{
    private const double Window = 10;
    private static readonly string[] Services = ["api", "auth", "db", "cache", "queue", "web"];
    private static readonly Marker[] Markers = [Marker.Braille, Marker.HalfBlock, Marker.Dot];
    private static readonly string[] BarModes = ["single", "grouped", "stacked"];

    private readonly List<(double X, double Y)> _sin = [];
    private readonly List<(double X, double Y)> _cos = [];
    private readonly double[] _requests = new double[Services.Length];
    private long _start;
    private double _time;
    private int _marker;
    private bool _scatter;
    private bool _area;
    private bool _horizontal;
    private int _barMode;

    public override string Title => "Charts";

    public ChartsScreen()
    {
        Keys.Add("m", "Marker", () => _marker = (_marker + 1) % Markers.Length)
            .Add("s", "Line/scatter", () => _scatter = !_scatter)
            .Add("a", "Line/area", () => _area = !_area)
            .Add("d", "Bar direction", () => _horizontal = !_horizontal)
            .Add("g", "Bar groups", () => _barMode = (_barMode + 1) % BarModes.Length);
    }

    protected override void OnMount()
    {
        _start = Demo.Time.GetTimestamp();
        Animation.Every(App, TimeSpan.FromMilliseconds(100), Lifetime, () =>
        {
            _time = Demo.Time.GetElapsedTime(_start).TotalSeconds;
            _sin.Add((_time, Math.Sin(_time) + Noise()));
            _cos.Add((_time, Math.Cos(_time * 0.7) * 0.8 + Noise()));
            _sin.RemoveAll(p => p.X < _time - Window);
            _cos.RemoveAll(p => p.X < _time - Window);
            for (var i = 0; i < _requests.Length; i++)
                _requests[i] = Math.Max(0, 50 + 40 * Math.Sin(_time / 2 + i) + Demo.Random.Next(-5, 6));
        });
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var rows = Layout.Vertical(area).Add("chart", Constraint.Percentage(55)).Add("bottom", Constraint.Fill()).Build();
        var bottom = Layout.Horizontal(rows["bottom"]).Add("bars", Constraint.Percentage(55)).Add("canvas", Constraint.Fill()).Build();

        var marker = Markers[_marker];
        var type = _scatter ? GraphType.Scatter : _area ? GraphType.Area : GraphType.Line;
        // 1.x spec §19.4, §19.5: the last 10 s as a duration axis ending now, with ticks and a grid
        List<(double X, double Y)> Ago(List<(double X, double Y)> data) => [.. data.Select(p => (p.X - _time, p.Y))];
        Widget.Chart()
            .Datasets(
                new Dataset("sin", Ago(_sin)) { Marker = marker, Type = type },
                new Dataset("cos", Ago(_cos)) { Marker = marker, Type = type })
            .XAxis(Axis.Duration() with { Title = "ago", Min = -Window, Max = 0, LabelCount = 6, Ticks = true })
            .YAxis(new Axis { Title = "value", Min = -1.5, Max = 1.5, LabelFormat = "0.0", LabelCount = 5, Ticks = true, Grid = true })
            .Block(Widget.Block().Title($" Line chart · {marker} (m), {type} (s, a) "))
            .Draw(buf, rows["chart"]);

        // 1.x spec §19.3: the same requests split into GET, POST and DELETE, side by side or stacked
        var bars = Widget.BarChart()
            .Direction(_horizontal ? Direction.Horizontal : Direction.Vertical)
            .Block(Widget.Block().Title($" Requests · {(_horizontal ? "horizontal" : "vertical")} (d), {BarModes[_barMode]} (g) "));
        if (_barMode == 0)
        {
            bars.Bars([.. Services.Select((name, i) => new Bar(name, Math.Round(_requests[i])))]).BarWidth(5).Max(100);
        }
        else
        {
            var groups = Services.Select((name, i) => new BarGroup(name,
            [
                new Bar("GET", Math.Round(_requests[i] * 0.6)) { ValueText = "" },
                new Bar("POST", Math.Round(_requests[i] * 0.3)) { ValueText = "" },
                new Bar("DELETE", Math.Round(_requests[i] * 0.1)) { ValueText = "" },
            ])).ToList();
            bars.Groups(groups).Stacked(_barMode == 2).BarWidth(_barMode == 2 ? 5 : 1).BarGap(_barMode == 2 ? 1 : 0).GroupGap(1)
                .Max(_barMode == 2 ? 100 : 60).Legend(LegendPosition.TopRight);
        }
        bars.Draw(buf, bottom["bars"]);

        var angle = _time;
        Widget.Canvas()
            .XBounds(-1.6, 1.6).YBounds(-1, 1)
            .Paint(p =>
            {
                p.Rectangle(-1.5, -0.95, 3, 1.9, ctx.Theme.TextMuted);
                p.Circle(0, 0, 0.8, ctx.Theme.Info);
                p.Line(0, 0, 0.8 * Math.Cos(angle), 0.8 * Math.Sin(angle), ctx.Theme.Warning);
                p.Points([(-1.2, 0.6), (1.2, 0.6), (-1.2, -0.6), (1.2, -0.6)], ctx.Theme.Success);
                p.Print(-1.4, 0.85, new Line($"{angle % (2 * Math.PI):0.0} rad", ctx.Theme.TextMuted));
            })
            .Block(Widget.Block().Title(" Canvas "))
            .Draw(buf, bottom["canvas"]);
    }

    private static double Noise() => (Demo.Random.NextDouble() - 0.5) * 0.1;
}
