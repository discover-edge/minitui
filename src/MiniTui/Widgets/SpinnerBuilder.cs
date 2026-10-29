namespace MiniTui;

/// <summary>
/// The animation frames of a <see cref="SpinnerBuilder"/>.
/// </summary>
public enum SpinnerFrames
{
    /// <summary><c>- \ | /</c></summary>
    Line,
    /// <summary><c>⠋ ⠙ ⠹ ⠸ ⠼ ⠴ ⠦ ⠧ ⠇ ⠏</c></summary>
    Braille,
    /// <summary><c>. .. ...</c></summary>
    Dots,
}

/// <summary>
/// An activity indicator. It's stateless: the frame comes from the app's clock, and while a spinner is drawn
/// the app ticks every 100 ms so it animates.
/// </summary>
public sealed class SpinnerBuilder
{
    internal static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(100);

    private static readonly string[] LineFrames = ["-", "\\", "|", "/"];
    private static readonly string[] BrailleFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private static readonly string[] DotFrames = [".  ", ".. ", "..."];

    private SpinnerFrames _frames = SpinnerFrames.Braille;
    private string? _label;
    private Style? _style;
    private TimeProvider? _time;

    /// <summary>The animation. The default is <see cref="SpinnerFrames.Braille"/>, drawn as <see cref="SpinnerFrames.Line"/> in conhost, whose fonts lack braille.</summary>
    public SpinnerBuilder Frames(SpinnerFrames frames) { _frames = frames; return this; }

    /// <summary>Text after the spinner.</summary>
    public SpinnerBuilder Label(string? label) { _label = label; return this; }

    /// <summary>A style patched over the theme's info style for the spinner glyph.</summary>
    public SpinnerBuilder Style(Style style) { _style = style; return this; }

    internal SpinnerBuilder Time(TimeProvider time) { _time = time; return this; }

    /// <summary>Draw the spinner on the first row of <paramref name="area"/>.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        var app = App.Current;
        app?.RequestTicks(FrameTime);
        if (area.IsEmpty) return;

        var frames = _frames switch
        {
            SpinnerFrames.Line => LineFrames,
            SpinnerFrames.Dots => DotFrames,
            _ => Glyphs.Basic ? LineFrames : BrailleFrames,
        };
        var time = _time ?? app?.Time ?? TimeProvider.System;
        var frame = frames[(int)(time.GetUtcNow().ToUnixTimeMilliseconds() / (long)FrameTime.TotalMilliseconds % frames.Length)];

        var theme = Theme.Current;
        var used = WidgetText.DrawString(buf, area.X, area.Y, frame, area.Width, theme.Info.Patch(_style ?? default));
        if (!string.IsNullOrEmpty(_label) && used + 1 < area.Width)
            WidgetText.DrawString(buf, area.X + used + 1, area.Y, _label, area.Width - used - 1, theme.Text);
    }
}
