namespace MiniTui;

/// <summary>
/// The kind of a toast, which sets its border color.
/// </summary>
public enum ToastLevel
{
    /// <summary>Information.</summary>
    Info,
    /// <summary>Something succeeded.</summary>
    Success,
    /// <summary>A warning.</summary>
    Warning,
    /// <summary>An error.</summary>
    Error,
}

/// <summary>
/// Short messages that pop up in the top-right corner and disappear on their own.
/// Draw them with <see cref="Widget.Toasts"/>. Thread-safe.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name from the spec (§8.3); it is a queue of toasts.")]
public sealed class ToastQueue
{
    internal const int MaxVisible = 3;
    internal static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    // While a toast is showing, the app ticks this often so it disappears on time
    internal static readonly TimeSpan ExpiryCheck = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private readonly List<Toast> _toasts = [];
    private readonly TimeProvider? _time;

    /// <summary>Create a queue that uses the app's clock.</summary>
    public ToastQueue() { }

    /// <summary>Create a queue that uses <paramref name="time"/>, for example a fake clock in tests.</summary>
    public ToastQueue(TimeProvider time) => _time = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>The toasts waiting or showing.</summary>
    public int Count
    {
        get { lock (_lock) return _toasts.Count; }
    }

    /// <summary>
    /// Add a toast. Up to three show at a time; the rest wait. Each one shows for <paramref name="duration"/>
    /// (default 4 seconds) from when it first appears.
    /// </summary>
    public void Push(string text, ToastLevel level = ToastLevel.Info, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_lock) _toasts.Add(new Toast(text, level, duration ?? DefaultDuration));
    }

    /// <summary>Remove all toasts.</summary>
    public void Clear()
    {
        lock (_lock) _toasts.Clear();
    }

    /// <summary>Drop expired toasts and return the ones to show now, starting the clock on newly shown ones.</summary>
    internal List<Toast> Visible(TimeProvider fallback)
    {
        var now = (_time ?? fallback).GetUtcNow();
        lock (_lock)
        {
            _toasts.RemoveAll(t => t.ShownAt is { } shown && now - shown >= t.Duration);
            var visible = _toasts.Take(MaxVisible).ToList();
            foreach (var toast in visible) toast.ShownAt ??= now;
            return visible;
        }
    }

    internal sealed class Toast(string text, ToastLevel level, TimeSpan duration)
    {
        public string Text { get; } = text;
        public ToastLevel Level { get; } = level;
        public TimeSpan Duration { get; } = duration;
        public DateTimeOffset? ShownAt { get; set; }
    }
}

/// <summary>
/// Draws a <see cref="ToastQueue"/> in the top-right corner of the area, usually the whole screen.
/// </summary>
public sealed class ToastsBuilder
{
    private const int MaxWidth = 60;

    private ToastQueue? _queue;

    /// <summary>The toasts to draw.</summary>
    public ToastsBuilder Queue(ToastQueue queue) { _queue = queue; return this; }

    /// <summary>Draw the visible toasts over whatever is in <paramref name="area"/>.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        if (_queue is null) return;

        var app = App.Current;
        var visible = _queue.Visible(app?.Time ?? TimeProvider.System);
        if (visible.Count == 0) return;
        app?.RequestTicks(ToastQueue.ExpiryCheck);

        var theme = Theme.Current;
        var y = area.Y;
        foreach (var toast in visible)
        {
            var width = Math.Min(UnicodeWidth.Of(Sanitizer.SingleLine(toast.Text)) + 4, Math.Min(MaxWidth, area.Width - 1));
            if (width < 5 || y + 3 > area.Bottom) break;

            var box = new Rect(area.Right - width - 1, y, width, 3);
            WidgetText.Clear(buf, box);
            var border = toast.Level switch
            {
                ToastLevel.Success => theme.Success,
                ToastLevel.Warning => theme.Warning,
                ToastLevel.Error => theme.Error,
                _ => theme.Info,
            };
            var inner = new BlockBuilder().BorderType(BoxStyle.Rounded).BorderStyle(border).Padding(new Margin(1, 0)).Draw(buf, box);
            WidgetText.DrawString(buf, inner.X, inner.Y, toast.Text, inner.Width, theme.Text);
            y += 3;
        }
    }
}
