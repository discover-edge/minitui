using System.Collections.Immutable;

namespace MiniTui;

/// <summary>
/// A text field with command history: Enter submits, ↑/↓ recall history, Esc clears (§8.4).
/// </summary>
public sealed class CommandLineState : TextFieldState
{
    private int _maxHistory = 100;
    private ImmutableList<string> _history = [];

    // Index into History while browsing; History.Count when editing a new line
    private int _historyIndex;

    /// <summary>The history entry being shown, or <c>History.Count</c> for a new line.</summary>
    internal int HistoryIndex
    {
        get => _historyIndex;
        set => _historyIndex = Math.Clamp(value, 0, History.Count);
    }

    /// <summary>Create a state with empty history.</summary>
    public CommandLineState() { }

    /// <summary>Create a state seeded with history (copied), oldest first.</summary>
    public CommandLineState(IEnumerable<string> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        History = [.. history];
    }

    /// <summary>Submitted lines, oldest first.</summary>
    public ImmutableList<string> History
    {
        get => _history;
        set => SetHistory(value ?? []);
    }

    /// <summary>The most entries <see cref="History"/> keeps; older ones are dropped. Default 100.</summary>
    public int MaxHistory
    {
        get => _maxHistory;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxHistory = value;
            SetHistory(History);
        }
    }

    /// <inheritdoc/>
    public override bool HandleKey(ConsoleKeyInfo key) => HandleKey(key, out _);

    /// <summary>
    /// Handle a key. Enter submits non-blank text: it's added to <see cref="History"/> (unless it repeats the
    /// last entry), the field is cleared, and <paramref name="submitted"/> is the parsed command
    /// (null if the text isn't a <c>:command</c>).
    /// </summary>
    /// <returns>True if the key was handled.</returns>
    public bool HandleKey(ConsoleKeyInfo key, out ParsedCommand? submitted)
    {
        submitted = null;
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                var line = Text;
                if (string.IsNullOrWhiteSpace(line)) return true;
                if (History.Count == 0 || History[^1] != line) History = History.Add(line);
                else _historyIndex = History.Count;
                Text = "";
                submitted = ParsedCommand.Parse(line);
                return true;

            case ConsoleKey.Escape:
                Text = "";
                _historyIndex = History.Count;
                return true;

            case ConsoleKey.UpArrow:
                if (_historyIndex > 0) Recall(_historyIndex - 1);
                return true;

            case ConsoleKey.DownArrow:
                if (_historyIndex < History.Count - 1) Recall(_historyIndex + 1);
                else if (_historyIndex == History.Count - 1)
                {
                    Text = "";
                    _historyIndex = History.Count;
                }
                return true;

            default:
                return base.HandleKey(key);
        }
    }

    private void Recall(int index)
    {
        _historyIndex = index;
        var text = History[index];
        Text = text;
    }

    private void SetHistory(ImmutableList<string> history)
    {
        _history = Cap(history);
        _historyIndex = _history.Count;
    }

    private ImmutableList<string> Cap(ImmutableList<string> history) =>
        history.Count > _maxHistory ? history.RemoveRange(0, history.Count - _maxHistory) : history;
}
