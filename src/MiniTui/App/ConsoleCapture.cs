using System.Text;

namespace MiniTui;

/// <summary>
/// Redirects <see cref="Console.Out"/> and <see cref="Console.Error"/> into a <see cref="LogBuffer"/>
/// while an app runs, so stray writes can't corrupt the screen (§6.6).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The line writers hold no resources; Restore completes them.")]
internal sealed class ConsoleCapture
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly LineWriter _out;
    private readonly LineWriter _error;
    private bool _restored;

    private ConsoleCapture(LogBuffer log)
    {
        _originalOut = Console.Out;
        _originalError = Console.Error;
        _out = new LineWriter(log, isError: false);
        _error = new LineWriter(log, isError: true);
        Console.SetOut(_out);
        Console.SetError(_error);
    }

    public static ConsoleCapture Start(LogBuffer log) => new(log);

    /// <summary>Put the original writers back. Unfinished lines are logged.</summary>
    public void Restore()
    {
        if (_restored) return;
        _restored = true;
        _out.Complete();
        _error.Complete();
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
    }

    /// <summary>
    /// Put the original writers back until the returned scope is disposed, for code that needs the real console
    /// (1.x spec §5.4). Lines already captured stay in the log.
    /// </summary>
    public IDisposable Pause()
    {
        if (_restored) return DisposeAction.None;
        _out.Complete();
        _error.Complete();
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        return new DisposeAction(() =>
        {
            if (_restored) return;
            Console.SetOut(_out);
            Console.SetError(_error);
        });
    }

    /// <summary>Write the captured error lines to the original stderr. Call after the terminal is restored.</summary>
    public void ReplayErrors()
    {
        foreach (var line in _error.Lines) _originalError.WriteLine(line);
        _originalError.Flush();
    }

    /// <summary>Splits writes into lines and appends each completed line to the log.</summary>
    private sealed class LineWriter(LogBuffer log, bool isError) : TextWriter
    {
        private readonly Lock _lock = new();
        private readonly StringBuilder _line = new();
        private readonly Queue<string> _lines = new();

        public override Encoding Encoding => Encoding.UTF8;

        /// <summary>Lines written so far (errors only; capped at the log's capacity).</summary>
        public IEnumerable<string> Lines
        {
            get { lock (_lock) return [.. _lines]; }
        }

        public override void Write(char value)
        {
            lock (_lock) Append(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            lock (_lock)
            {
                for (var i = index; i < index + count; i++) Append(buffer[i]);
            }
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            lock (_lock)
            {
                foreach (var c in value) Append(c);
            }
        }

        public void Complete()
        {
            lock (_lock)
            {
                if (_line.Length > 0) Emit();
            }
        }

        private void Append(char c)
        {
            if (c == '\n') Emit();
            else _line.Append(c);
        }

        private void Emit()
        {
            if (_line.Length > 0 && _line[^1] == '\r') _line.Length--;
            var text = _line.ToString();
            _line.Clear();
            log.Add(text, isError);
            if (!isError) return;
            _lines.Enqueue(text);
            if (_lines.Count > log.Capacity) _lines.Dequeue();
        }
    }
}
