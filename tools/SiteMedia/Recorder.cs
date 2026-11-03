using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using MiniTui;
using MiniTui.Example;

namespace SiteMedia;

/// <summary>
/// Runs a demo screen on a fake clock and records what it draws (1.x spec §21.4): each frame's changes, encoded by
/// MiniTui's own ANSI encoder, as an asciicast; or the last frame as an SVG.
/// </summary>
internal sealed class Recorder : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 9, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly AnsiEncoder _encoder = new(ColorMode.TrueColor);
    private readonly List<string> _events = [];
    private readonly Cell[] _shown;
    private (int X, int Y)? _cursor;
    private bool _drawn;

    public Recorder(Func<Screen> root, int width = 100, int height = 30, bool demoKeys = true, int seed = 27)
    {
        Width = width;
        Height = height;
        _shown = new Cell[width * height];
        _encoder.ScreenWidth = width;

        // Every run draws the same frames: the demos' clock, random numbers and time of day are fixed
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Demo.Time = _time;
        Demo.Random = new Random(seed);

        Harness = new ComponentHarness(root(), width, height, _time);
        if (demoKeys) Demo.Configure(Harness.App);
        Harness.App.WithTheme(Theme.Dark);
        Harness.RunUntilIdle();
        Frame();
    }

    public int Width { get; }
    public int Height { get; }
    public ComponentHarness Harness { get; }

    private double Seconds => (_time.GetUtcNow() - Start).TotalSeconds;

    /// <summary>Press each key, a moment apart, then pause.</summary>
    public Recorder Press(string keys, double pause = 0.6)
    {
        foreach (var key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            Harness.Press(key);
            Frame();
            Wait(0.3);
        }
        return Wait(pause);
    }

    /// <summary>Type the text a character at a time, as a person would.</summary>
    public Recorder Type(string text, double pause = 0.6)
    {
        foreach (var c in text)
        {
            Harness.Type(c.ToString());
            Frame();
            Advance(TimeSpan.FromMilliseconds(90));
        }
        return Wait(pause);
    }

    /// <summary>Let the app run (animations, timers) for a while, recording each frame.</summary>
    public Recorder Wait(double seconds)
    {
        var end = _time.GetUtcNow() + TimeSpan.FromSeconds(seconds);
        while (_time.GetUtcNow() < end) Advance(Step);
        return this;
    }

    private void Advance(TimeSpan by)
    {
        _time.Advance(by);
        Harness.RunUntilIdle();
        Frame();
    }

    // The cells that changed since the last frame, encoded as the app would send them
    private void Frame()
    {
        _encoder.Clear();
        var any = false;
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var cell = Harness.CellAt(x, y);
            var i = y * Width + x;
            if (_drawn && cell == _shown[i]) continue;
            _shown[i] = cell;
            if (cell.IsContinuation) continue;
            if (!any) _encoder.BeginFrame(fullRedraw: !_drawn);
            any = true;
            _encoder.WriteCell(x, y, cell);
        }
        var cursor = Harness.Backend.CursorPosition;
        if (!any && cursor == _cursor) return;
        if (!any) _encoder.BeginFrame(fullRedraw: false);
        _encoder.EndFrame(cursor);
        _cursor = cursor;
        _drawn = true;
        var text = Encoding.UTF8.GetString(_encoder.Written);
        _events.Add($"[{Seconds.ToString("0.000", CultureInfo.InvariantCulture)}, \"o\", {Json(text)}]");
    }

    /// <summary>Write the recording as an asciicast v2 file.</summary>
    public void SaveCast(string path)
    {
        var (fg, bg) = DefaultColors();
        var header = $"{{\"version\": 2, \"width\": {Width}, \"height\": {Height}, " +
            $"\"env\": {{\"TERM\": \"xterm-256color\"}}, \"theme\": {{\"fg\": \"{fg}\", \"bg\": \"{bg}\", \"palette\": \"{Palette()}\"}}}}";
        File.WriteAllText(path, header + "\n" + string.Join("\n", _events) + "\n");
    }

    /// <summary>Write the last frame as an SVG.</summary>
    public void SaveSvg(string path, string title) => File.WriteAllText(path, Svg.Draw(Harness.CellAt, Width, Height, title));

    internal static (string Fg, string Bg) DefaultColors() =>
        (Svg.Hex(Theme.Dark.Text.Fg ?? Color.Reset, foreground: true), Svg.Hex(Theme.Dark.Text.Bg ?? Color.Reset, foreground: false));

    private static string Palette() =>
        string.Join(":", Enumerable.Range(0, 16).Select(i => Svg.Hex(Color.Indexed((byte)i), foreground: true)));

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Json(string text) => JsonSerializer.Serialize(text, JsonOptions);

    public void Dispose()
    {
        Harness.Dispose();
        _encoder.Dispose();
        Demo.Time = TimeProvider.System;
        Demo.Random = Random.Shared;
    }
}
