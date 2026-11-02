using System.Globalization;

namespace MiniTui;

/// <summary>One frame's numbers (1.x spec §9.4).</summary>
/// <param name="Frame">The frame's number: 1 for the first frame drawn by this run.</param>
/// <param name="RenderTime">Drawing the frame (the render callback or screen, overlays and toasts), without the debug overlay.</param>
/// <param name="FlushTime">Comparing the buffers and the backend writing the changes.</param>
/// <param name="CellsChanged">The cells sent to the backend; every cell on a full redraw.</param>
/// <param name="BytesWritten">The bytes written to the terminal, or null on backends that don't write bytes (conhost, tests).</param>
/// <param name="FullRedraw">True if the frame redrew the whole screen.</param>
public readonly record struct FrameStats(
    long Frame,
    TimeSpan RenderTime,
    TimeSpan FlushTime,
    int CellsChanged,
    long? BytesWritten,
    bool FullRedraw);

// The debug overlay (1.x spec §9)
public partial class App
{
    private const int PanelWidth = 44;
    private const int RenderSamples = 60;
    private static readonly Style DebugOutline = new(Bg: Color.DarkBlue);
    private static readonly Style DebugChain = new(Bg: Color.DarkMagenta);
    private static readonly Style DebugFocused = new(Color.Black, Color.Yellow);
    private static readonly Style DebugPanel = new(Color.White, Color.DarkGray);

    private KeyBinding? _debugKey;
    private bool _showDebug;
    private FrameStats _lastStats;
    private readonly Queue<double> _renderSamples = new();
    private readonly Queue<long> _frameTimes = new();
    private (int X, int Y)? _pointer;
    private ColorMode? _activeColorMode;

    /// <summary>Read environment variables; replaced in tests.</summary>
    internal Func<string, string?> GetEnvironment { get; set; } = Environment.GetEnvironmentVariable;

    /// <summary>
    /// Make <paramref name="key"/> toggle the debug overlay (1.x spec §9). Off by default. The key is checked right
    /// after <see cref="OnKey"/>, before Ctrl+C, overlays and the focus chain, so it works while a dialog is open or
    /// a text field has focus. Null turns the key off again.
    /// </summary>
    /// <param name="key">A key in the <see cref="KeyMap"/> syntax, such as <c>"f12"</c> or <c>"ctrl+shift+d"</c>.</param>
    /// <exception cref="FormatException"><paramref name="key"/> isn't a valid key.</exception>
    public App WithDebugKey(string? key = "f12")
    {
        _debugKey = key is null ? null : KeyBinding.Parse(key);
        return this;
    }

    /// <summary>
    /// True while the debug overlay is drawn: component outlines and a panel of frame numbers (1.x spec §9.3).
    /// Set it from code, or with the key from <see cref="WithDebugKey"/>.
    /// </summary>
    public bool ShowDebug
    {
        get => _showDebug;
        set
        {
            if (_showDebug == value) return;
            _showDebug = value;
            RequestRedraw();
        }
    }

    /// <summary>Numbers about the last frame drawn; default before the first one.</summary>
    public FrameStats LastFrame => _lastStats;

    /// <summary><c>MINITUI_DEBUG=1</c> turns on the F12 key (unless another key was chosen) and shows the overlay.</summary>
    private void StartDebug()
    {
        _lastStats = default;
        _renderSamples.Clear();
        _frameTimes.Clear();
        _pointer = null;
        if (GetEnvironment("MINITUI_DEBUG") != "1") return;
        _debugKey ??= KeyBinding.Parse("f12");
        _showDebug = true;
    }

    private bool HandleDebugKey(ConsoleKeyInfo key)
    {
        if (_debugKey is not { } binding || !binding.Matches(key)) return false;
        ShowDebug = !ShowDebug;
        return true;
    }

    /// <summary>Record a drawn frame's numbers. <paramref name="renderTime"/> excludes the overlay.</summary>
    private void RecordFrame(Terminal terminal, TimeSpan renderTime, TimeSpan flushTime)
    {
        var frame = _lastStats.Frame + 1;
        _renderSamples.Enqueue(renderTime.TotalMilliseconds);
        if (_renderSamples.Count > RenderSamples) _renderSamples.Dequeue();
        var now = _time.GetTimestamp();
        _frameTimes.Enqueue(now);
        while (_frameTimes.Count > 0 && _time.GetElapsedTime(_frameTimes.Peek(), now) > TimeSpan.FromSeconds(1)) _frameTimes.Dequeue();
        _lastStats = new FrameStats(frame, renderTime, flushTime, terminal.LastCellsChanged, terminal.LastBytesWritten, terminal.LastFullRedraw);
    }

    /// <summary>Outline the recorded rects and draw the panel, over the frame in progress.</summary>
    private void DrawDebug(Buffer buf, Rect area, TimeSpan renderTime)
    {
        var chain = KeyRouter.Chain(this);
        var focused = chain.Count > 0 ? chain[^1] : null;
        Style Plain(Style color, Style mono) => _noColor ? mono : color;

        foreach (var hit in _hits) Outline(buf, hit.Rect, Plain(DebugOutline, Style.Default.Underlined()));
        foreach (var hit in _hits.Where(h => chain.Contains(h.Component) && !ReferenceEquals(h.Component, focused)))
            Outline(buf, hit.Rect, Plain(DebugChain, Style.Default.Bold().Underlined()));
        foreach (var hit in _hits.Where(h => ReferenceEquals(h.Component, focused)))
            Outline(buf, hit.Rect, Plain(DebugFocused, Style.Default.Reversed()));

        DrawPanel(buf, area, PanelLines(area, chain, renderTime), Plain(DebugPanel, Style.Default.Reversed()));
    }

    private static void Outline(Buffer buf, Rect r, Style style)
    {
        if (r.IsEmpty) return;
        buf.SetStyle(r with { Height = 1 }, style);
        buf.SetStyle(new Rect(r.X, r.Bottom - 1, r.Width, 1), style);
        buf.SetStyle(r with { Width = 1 }, style);
        buf.SetStyle(new Rect(r.Right - 1, r.Y, 1, r.Height), style);
    }

    private List<string> PanelLines(Rect area, List<Component> chain, TimeSpan renderTime)
    {
        var frame = _lastStats.Frame + 1;
        // This frame and the 59 before it
        var samples = _renderSamples.Skip(Math.Max(0, _renderSamples.Count - (RenderSamples - 1))).Append(renderTime.TotalMilliseconds).ToList();
        var bytes = _lastStats.BytesWritten is { } b ? Bytes(b) : "–";

        var lines = new List<string>
        {
            $"frame {frame}  render {Ms(renderTime.TotalMilliseconds)}  flush {Ms(_lastStats.FlushTime.TotalMilliseconds)}",
            $"avg {Ms(samples.Average())}  max {Ms(samples.Max())}  fps {_frameTimes.Count}",
            $"cells {_lastStats.CellsChanged}  bytes {bytes}{(_lastStats.FullRedraw ? "  full" : "")}",
            $"{area.Width}×{area.Height}  {ColorName(_activeColorMode)}  {(_basicGlyphs ? "basic" : "braille")}{(_mouse ? "  mouse" : "")}{TerminalSummary()}",
            "focus: " + WidgetText.TruncateStart(string.Join(" › ", chain.Select(c => c.GetType().Name)), Math.Min(PanelWidth, area.Width) - 4 - 7),
        };
        if (_mouse)
        {
            var under = _pointer is { } p ? _hits.LastOrDefault(h => h.Rect.Contains(p.X, p.Y)) : default;
            lines.Add(under.Component is { } c
                ? $"under pointer: {c.GetType().Name} ({under.Rect.X}, {under.Rect.Y}, {under.Rect.Width}×{under.Rect.Height})"
                : "under pointer: –");
        }
        lines.Add($"screens {Screens.Count}  overlays {Overlays.All.Count}  toasts {Toasts.Count}");
        return lines;
    }

    // Top-right, or bottom-left while the pointer is over it; nothing when the screen is too small
    private void DrawPanel(Buffer buf, Rect area, List<string> lines, Style style)
    {
        var width = Math.Min(Math.Min(PanelWidth, lines.Max(l => UnicodeWidth.Of(l)) + 4), area.Width);
        var height = lines.Count + 2;
        if (width < 12 || height > area.Height) return;

        var box = new Rect(area.Right - width, area.Y, width, height);
        if (_pointer is { } p && box.Contains(p.X, p.Y)) box = new Rect(area.X, area.Bottom - height, width, height);

        WidgetText.Clear(buf, box);
        buf.SetStyle(box, style);
        var inner = Widget.Block().BorderStyle(style).Padding(new Margin(1, 0)).Draw(buf, box);
        for (var i = 0; i < lines.Count; i++) WidgetText.DrawString(buf, inner.X, inner.Y + i, lines[i], inner.Width, style);
    }

    private static string Ms(double ms) => ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

    private static string Bytes(long bytes) => bytes < 1024
        ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
        : (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";

    // The keyboard protocol, light or dark, and the terminal's name, where known (1.x spec §12.7)
    private string TerminalSummary()
    {
        var info = _terminalInfo;
        var parts = new List<string>();
        if (info.Keyboard != KeyboardProtocol.Legacy) parts.Add(ProtocolName(info.Keyboard));
        if (info.IsDark is { } dark) parts.Add(dark ? "dark" : "light");
        if (info.Name is { } name) parts.Add(name);
        return parts.Count == 0 ? "" : "  " + string.Join("  ", parts);
    }

    internal static string ProtocolName(KeyboardProtocol protocol) => protocol switch
    {
        KeyboardProtocol.Kitty => "kitty",
        KeyboardProtocol.ModifyOtherKeys => "modifyOtherKeys",
        KeyboardProtocol.Win32InputMode => "win32-input",
        _ => "legacy",
    };

    private static string ColorName(ColorMode? mode) => mode switch
    {
        ColorMode.None => "no color",
        ColorMode.Ansi16 => "16 colors",
        ColorMode.Ansi256 => "256 colors",
        ColorMode.TrueColor => "truecolor",
        _ => "colors unknown",
    };
}
