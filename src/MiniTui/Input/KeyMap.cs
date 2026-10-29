namespace MiniTui;

/// <summary>
/// Key bindings with descriptions, for handling keys and showing hints and help (§8.5).
/// </summary>
/// <example>
/// <code>
/// var keys = new KeyMap()
///     .Add("r", "Refresh", Refresh)
///     .Add("j,down", "Down", () => selection.Next(count))
///     .Add("q,ctrl+c", "Quit", app.Quit);
/// app.OnKey(keys.Handle);
/// </code>
/// </example>
public sealed class KeyMap
{
    private readonly List<Entry> _entries = [];

    /// <summary>
    /// Add a binding. <paramref name="keys"/> is one or more comma-separated bindings in the
    /// <see cref="KeyBinding.Parse"/> grammar, such as <c>"j,down"</c>.
    /// </summary>
    /// <param name="keys">The bindings.</param>
    /// <param name="description">What the binding does, shown in hints and help.</param>
    /// <param name="handler">Called when a binding matches.</param>
    /// <param name="group">A heading to list the binding under in <see cref="HelpLines"/>.</param>
    /// <param name="showInHints">Whether <see cref="Hints"/> lists the binding.</param>
    /// <exception cref="FormatException"><paramref name="keys"/> isn't valid.</exception>
    public KeyMap Add(string keys, string description, Action handler, string? group = null, bool showInHints = true)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(handler);

        var parts = keys.Split(',');
        var bindings = new KeyBinding[parts.Length];
        for (var i = 0; i < parts.Length; i++) bindings[i] = KeyBinding.Parse(parts[i]);

        var labels = Array.ConvertAll(parts, p => p.Trim());
        _entries.Add(new Entry(bindings, labels, description, handler, group, showInHints));
        return this;
    }

    /// <summary>Run the handler of the first binding that matches <paramref name="key"/>.</summary>
    /// <returns>True if a binding matched.</returns>
    public bool Handle(ConsoleKeyInfo key) => Handle(key, skipPlainCharacters: false);

    /// <summary>The bindings, in add order.</summary>
    internal IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Like <see cref="Handle(ConsoleKeyInfo)"/>, optionally skipping single-character bindings without Ctrl or Alt (§9.4 text capture).</summary>
    internal bool Handle(ConsoleKeyInfo key, bool skipPlainCharacters)
    {
        foreach (var entry in _entries)
        {
            foreach (var binding in entry.Bindings)
            {
                if (skipPlainCharacters && binding.IsPlainCharacter) continue;
                if (!binding.Matches(key)) continue;
                entry.Handler();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Help text: one line per binding, <c>"  keys  description"</c>, with the descriptions aligned.
    /// Bindings with a group are listed under a line with the group name, after the ungrouped ones.
    /// </summary>
    public IEnumerable<string> HelpLines()
    {
        var width = 0;
        foreach (var entry in _entries) width = Math.Max(width, UnicodeWidth.Of(entry.KeysLabel));

        var groups = _entries.Select(e => e.Group).Distinct().OrderBy(g => g is null ? 0 : 1).ToList();
        var first = true;
        foreach (var group in groups)
        {
            if (group is not null)
            {
                if (!first) yield return "";
                yield return group;
            }
            first = false;
            foreach (var entry in _entries.Where(e => e.Group == group))
            {
                var pad = new string(' ', width - UnicodeWidth.Of(entry.KeysLabel));
                yield return $"  {entry.KeysLabel}{pad}  {entry.Description}";
            }
        }
    }

    /// <summary>
    /// A one-line summary such as <c>"r Refresh  q Quit"</c>: each binding shown in hints, with its first key.
    /// Only whole hints that fit in <paramref name="maxWidth"/> columns are included; if even the first doesn't
    /// fit, it's truncated with <c>…</c>.
    /// </summary>
    public string Hints(int maxWidth) =>
        FitHints(_entries.Where(e => e.ShowInHints).Select(e => $"{e.Labels[0]} {e.Description}"), maxWidth);

    /// <summary>Join hints with two spaces, keeping only whole hints that fit; the first is truncated if even it doesn't.</summary>
    internal static string FitHints(IEnumerable<string> hints, int maxWidth)
    {
        if (maxWidth <= 0) return "";
        var result = "";
        var width = 0;
        foreach (var hint in hints)
        {
            var hintWidth = UnicodeWidth.Of(hint);
            var separator = result.Length == 0 ? 0 : 2;
            if (width + separator + hintWidth > maxWidth)
                return result.Length == 0 ? UnicodeWidth.Truncate(hint, maxWidth) : result;
            result += (separator == 0 ? "" : "  ") + hint;
            width += separator + hintWidth;
        }
        return result;
    }

    internal sealed record Entry(
        KeyBinding[] Bindings, string[] Labels, string Description, Action Handler, string? Group, bool ShowInHints)
    {
        public string KeysLabel { get; } = string.Join(", ", Labels);
    }
}
