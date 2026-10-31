using MiniTui;

namespace MiniTui.Example;

/// <summary>A live line chart, a bar chart and a canvas, animated by an async loop.</summary>
internal sealed class ChartsScreen : Screen
{
    private const double Window = 10;
    private static readonly string[] Services = ["api", "auth", "db", "cache", "queue", "web"];
    private static readonly Marker[] Markers = [Marker.Braille, Marker.HalfBlock, Marker.Dot];

    private readonly List<(double X, double Y)> _sin = [];
    private readonly List<(double X, double Y)> _cos = [];
    private readonly double[] _requests = new double[Services.Length];
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private double _time;
    private int _marker;
    private bool _scatter;
    private bool _horizontal;

    public override string Title => "Charts";

    public ChartsScreen()
    {
        Keys.Add("m", "Marker", () => _marker = (_marker + 1) % Markers.Length)
            .Add("s", "Line/scatter", () => _scatter = !_scatter)
            .Add("d", "Bar direction", () => _horizontal = !_horizontal);
    }

    protected override void OnMount()
    {
        _clock.Start();
        Animation.Every(App, TimeSpan.FromMilliseconds(100), Lifetime, () =>
        {
            _time = _clock.Elapsed.TotalSeconds;
            _sin.Add((_time, Math.Sin(_time) + Noise()));
            _cos.Add((_time, Math.Cos(_time * 0.7) * 0.8 + Noise()));
            _sin.RemoveAll(p => p.X < _time - Window);
            _cos.RemoveAll(p => p.X < _time - Window);
            for (var i = 0; i < _requests.Length; i++)
                _requests[i] = Math.Max(0, 50 + 40 * Math.Sin(_time / 2 + i) + Random.Shared.Next(-5, 6));
        });
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var rows = Layout.Vertical(area).Add("chart", Constraint.Percentage(55)).Add("bottom", Constraint.Fill()).Build();
        var bottom = Layout.Horizontal(rows["bottom"]).Add("bars", Constraint.Percentage(55)).Add("canvas", Constraint.Fill()).Build();

        var marker = Markers[_marker];
        var type = _scatter ? GraphType.Scatter : GraphType.Line;
        var start = Math.Max(0, _time - Window);
        Widget.Chart()
            .Datasets(
                new Dataset("sin", _sin) { Marker = marker, Type = type },
                new Dataset("cos", _cos) { Marker = marker, Type = type })
            .XAxis(new Axis { Title = "seconds", Min = start, Max = start + Window, LabelFormat = "0" })
            .YAxis(new Axis { Title = "value", LabelFormat = "0.0" })
            .Block(Widget.Block().Title($" Line chart · {marker} (m), {type} (s) "))
            .Draw(buf, rows["chart"]);

        Widget.BarChart()
            .Bars([.. Services.Select((name, i) => new Bar(name, Math.Round(_requests[i])))])
            .Direction(_horizontal ? Direction.Horizontal : Direction.Vertical)
            .BarWidth(5)
            .Max(100)
            .Block(Widget.Block().Title($" Requests · {(_horizontal ? "horizontal" : "vertical")} (d) "))
            .Draw(buf, bottom["bars"]);

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

    private static double Noise() => (Random.Shared.NextDouble() - 0.5) * 0.1;
}
