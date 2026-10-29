namespace MiniTui;

/// <summary>
/// One line in a <see cref="LogBuffer"/>.
/// </summary>
/// <param name="Timestamp">When the line was completed.</param>
/// <param name="IsError">True if it was written to <see cref="Console.Error"/>.</param>
/// <param name="Text">The line, without its line break.</param>
public sealed record LogEntry(DateTimeOffset Timestamp, bool IsError, string Text);

/// <summary>
/// A thread-safe ring buffer of log lines. While an app runs with console capture on (the default),
/// everything written to <see cref="Console.Out"/> and <see cref="Console.Error"/> lands here (§6.6).
/// </summary>
public sealed class LogBuffer
{
    private readonly Lock _lock = new();
    private readonly LogEntry[] _entries;
    private int _start;
    private int _count;

    /// <summary>Create a buffer that keeps the last <paramref name="capacity"/> lines.</summary>
    public LogBuffer(int capacity = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _entries = new LogEntry[capacity];
    }

    /// <summary>The maximum number of lines kept. Older lines are dropped.</summary>
    public int Capacity => _entries.Length;

    /// <summary>The number of lines currently held.</summary>
    public int Count
    {
        get { lock (_lock) return _count; }
    }

    /// <summary>The clock used for timestamps. The app sets it to its <see cref="TimeProvider"/>.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Append a line, timestamped now.</summary>
    public void Add(string text, bool isError = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entry = new LogEntry(Time.GetUtcNow(), isError, text);
        lock (_lock)
        {
            _entries[(_start + _count) % _entries.Length] = entry;
            if (_count < _entries.Length) _count++;
            else _start = (_start + 1) % _entries.Length;
        }
    }

    /// <summary>A copy of the held lines, oldest first.</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
        {
            var copy = new LogEntry[_count];
            for (var i = 0; i < _count; i++) copy[i] = _entries[(_start + i) % _entries.Length];
            return copy;
        }
    }

    /// <summary>Remove all lines.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_entries);
            _start = _count = 0;
        }
    }
}
