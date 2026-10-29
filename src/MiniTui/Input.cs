namespace MiniTui;

/// <summary>
/// Simple input handling utilities.
/// </summary>
public static class Input
{
    /// <summary>
    /// Non-blocking key poll. Returns null if no key is available.
    /// </summary>
    /// <remarks>
    /// Inside <see cref="App.RunWhile"/>'s condition, this returns the key the app is delivering (once), and a key
    /// taken this way isn't passed to the router. Elsewhere while an app reads the console, it returns null rather
    /// than steal the app's keys.
    /// </remarks>
    public static ConsoleKeyInfo? Poll()
    {
        if (App.Current is { } app) return app.TakeOfferedKey();
        if (App.ConsoleInputInUse) return null;
        if (InputPump.TryTakeOrphan(InputPump.Console, out var orphan)) return orphan;
        if (Console.KeyAvailable)
            return Console.ReadKey(intercept: true);
        return null;
    }

    /// <summary>
    /// Blocking key read. Waits for a key press.
    /// </summary>
    public static ConsoleKeyInfo Read() => Console.ReadKey(intercept: true);

    /// <summary>
    /// Check if a specific key was pressed. Convenience for common patterns.
    /// </summary>
    public static bool IsKey(ConsoleKeyInfo? key, ConsoleKey target) 
        => key?.Key == target;

    /// <summary>
    /// Check if a specific character was pressed.
    /// </summary>
    public static bool IsChar(ConsoleKeyInfo? key, char target)
        => key?.KeyChar == target;

    /// <summary>
    /// Check for common exit keys (q, Escape, Ctrl+C).
    /// </summary>
    public static bool IsQuit(ConsoleKeyInfo? key)
    {
        if (key == null) return false;
        return key.Value.KeyChar == 'q' 
            || key.Value.Key == ConsoleKey.Escape
            || IsCtrlC(key.Value);
    }

    /// <summary>
    /// Ctrl+C, which the terminal delivers as a key while an app is running.
    /// </summary>
    internal static bool IsCtrlC(ConsoleKeyInfo key) =>
        key.KeyChar == '\x03' || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control));
}

/// <summary>
/// State for a text input field.
/// </summary>
[Obsolete("Use TextFieldState instead.")]
public record struct TextInputState(string Value, int CursorPos)
{
    /// <summary>Empty state with no text and cursor at position 0.</summary>
    public static TextInputState Empty => new("", 0);
}

/// <summary>
/// State for a list/selection.
/// </summary>
[Obsolete("Use SelectionState instead.")]
public record struct ListState(int SelectedIndex, int ScrollOffset)
{
    /// <summary>Empty state with selection and scroll at 0.</summary>
    public static ListState Empty => new(0, 0);
}

/// <summary>
/// A parsed command with name and arguments.
/// </summary>
public record ParsedCommand(string Name, string[] Args, string Raw)
{
    /// <summary>
    /// Parse a colon-prefixed command string (e.g., ":add self Fixed bug").
    /// Returns null if the input doesn't start with a colon.
    /// </summary>
    public static ParsedCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        input = input.Trim();
        if (!input.StartsWith(':'))
            return null;

        var content = input[1..];
        var parts = content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return null;

        var name = parts[0].ToLowerInvariant();
        var args = parts.Length > 1
            ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];

        return new ParsedCommand(name, args, input);
    }

    /// <summary>
    /// Get all arguments as a single string (useful for commands like ":add person note text here").
    /// </summary>
    public string ArgsAsString => Args.Length > 0 ? string.Join(' ', Args) : "";

    /// <summary>
    /// Get argument at index, or null if not present.
    /// </summary>
    public string? GetArg(int index) => index < Args.Length ? Args[index] : null;

    /// <summary>
    /// Get all arguments starting from index as a single string.
    /// </summary>
    public string? GetArgsFrom(int index) => index < Args.Length
        ? string.Join(' ', Args.Skip(index))
        : null;
}

/// <summary>
/// State for a command input with history.
/// </summary>
[Obsolete("Use CommandLineState instead.")]
public record struct CommandInputState(string Value, int CursorPos, List<string> History, int HistoryIndex)
{
    /// <summary>Empty state with no text, cursor at 0, empty history.</summary>
    public static CommandInputState Empty => new("", 0, [], 0);

    /// <summary>
    /// Create a new state with a copy of the given history.
    /// </summary>
    public static CommandInputState WithHistory(List<string> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return new("", 0, [.. history], history.Count);
    }
}

/// <summary>
/// Result of handling command input - either just state update, or state + submitted command.
/// </summary>
[Obsolete("Use CommandLineState.HandleKey(key, out submitted) instead.")]
public record struct CommandInputResult(CommandInputState State, ParsedCommand? Command);

/// <summary>
/// A timed message that expires after a duration.
/// </summary>
public record struct TimedMessage(string Text, DateTime Expiry)
{
    /// <summary>Empty message (already expired).</summary>
    public static TimedMessage Empty => new("", DateTime.MinValue);

    /// <summary>
    /// Create a message that expires after the specified duration.
    /// </summary>
    public static TimedMessage Show(string text, TimeSpan duration) =>
        new(text, DateTime.Now + duration);

    /// <summary>
    /// Create a message that expires after the specified seconds.
    /// </summary>
    public static TimedMessage Show(string text, double seconds = 3) =>
        Show(text, TimeSpan.FromSeconds(seconds));

    /// <summary>
    /// Get the message text if not expired, otherwise return the default.
    /// </summary>
    public readonly string GetOrDefault(string defaultText) =>
        DateTime.Now < Expiry ? Text : defaultText;

    /// <summary>
    /// Check if the message is still visible (not expired).
    /// </summary>
    public readonly bool IsVisible => DateTime.Now < Expiry;
}

/// <summary>
/// State for a confirmation dialog.
/// </summary>
public record struct ConfirmState(bool IsActive, bool YesSelected, Action<bool>? OnComplete)
{
    /// <summary>Inactive state.</summary>
    public static ConfirmState Inactive => new(false, true, null);

    /// <summary>Show a confirmation dialog.</summary>
    public static ConfirmState Show(Action<bool> onComplete, bool defaultYes = true) =>
        new(true, defaultYes, onComplete);

    /// <summary>Complete the confirmation with the selected result.</summary>
    public readonly void Complete()
    {
        OnComplete?.Invoke(YesSelected);
    }
}

/// <summary>
/// State for a tabs widget.
/// </summary>
[Obsolete("Use TabBarState instead.")]
public record struct TabsState(int SelectedIndex, int TabCount)
{
    /// <summary>Create initial state with given tab count, first tab selected.</summary>
    public static TabsState Create(int tabCount) => new(0, tabCount);
}

/// <summary>
/// Helper functions for handling widget input.
/// </summary>
public static class InputHelpers
{
    /// <summary>
    /// Handle text input keys. Returns new state after processing the key.
    /// </summary>
    /// <remarks>Edits like <see cref="TextFieldState.HandleKey"/>, except that characters outside the BMP can't be typed.</remarks>
    [Obsolete("Use TextFieldState.HandleKey instead.")]
    public static TextInputState HandleTextInput(ConsoleKeyInfo key, TextInputState state)
    {
        var field = new TextFieldState(state.Value ?? "") { Cursor = state.CursorPos };
        field.HandleKey(key);
        return new(field.Text, field.Cursor);
    }

    /// <summary>
    /// Handle list navigation keys. Returns new state after processing the key.
    /// </summary>
    [Obsolete("Use SelectionState.HandleKey instead.")]
    public static ListState HandleListNavigation(ConsoleKeyInfo key, ListState state, int itemCount, int visibleHeight)
    {
        if (itemCount == 0) return state;

        var (selected, scroll) = state;
        var maxScroll = Math.Max(0, itemCount - visibleHeight);

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                if (selected > 0)
                {
                    selected--;
                    if (selected < scroll)
                        scroll = selected;
                }
                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                if (selected < itemCount - 1)
                {
                    selected++;
                    if (selected >= scroll + visibleHeight)
                        scroll = selected - visibleHeight + 1;
                }
                break;

            case ConsoleKey.PageUp:
                selected = Math.Max(0, selected - visibleHeight);
                scroll = Math.Max(0, scroll - visibleHeight);
                break;

            case ConsoleKey.PageDown:
                selected = Math.Min(itemCount - 1, selected + visibleHeight);
                scroll = Math.Min(maxScroll, scroll + visibleHeight);
                break;

            case ConsoleKey.Home:
                selected = 0;
                scroll = 0;
                break;

            case ConsoleKey.End:
                selected = itemCount - 1;
                scroll = maxScroll;
                break;
        }

        return new(selected, scroll);
    }

    /// <summary>
    /// Check if the key is a text editing key (not navigation).
    /// </summary>
    [Obsolete("Use the return value of TextFieldState.HandleKey instead.")]
    public static bool IsTextEditKey(ConsoleKeyInfo key)
    {
        return key.Key == ConsoleKey.Backspace
            || key.Key == ConsoleKey.Delete
            || (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) && !KeyInput.HasCommandModifier(key));
    }

    /// <summary>
    /// Check if the key is a list navigation key.
    /// </summary>
    public static bool IsListNavigationKey(ConsoleKeyInfo key)
    {
        return key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow
            or ConsoleKey.PageUp or ConsoleKey.PageDown
            or ConsoleKey.Home or ConsoleKey.End
            or ConsoleKey.J or ConsoleKey.K;
    }

    /// <summary>
    /// Check if the key should dismiss a modal (Escape, Enter, or Q).
    /// </summary>
    public static bool IsModalDismissKey(ConsoleKeyInfo key)
    {
        return key.Key is ConsoleKey.Escape or ConsoleKey.Enter or ConsoleKey.Q;
    }

    /// <summary>
    /// Handle confirmation dialog input. Returns updated state (null if dialog should close).
    /// </summary>
    public static ConfirmState? HandleConfirmInput(ConsoleKeyInfo key, ConfirmState state)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
            case ConsoleKey.Tab:
            case ConsoleKey.H:
            case ConsoleKey.L:
                // Toggle selection
                return state with { YesSelected = !state.YesSelected };

            case ConsoleKey.Enter:
                // Confirm current selection
                state.Complete();
                return null;

            case ConsoleKey.Y:
                // Yes
                state.OnComplete?.Invoke(true);
                return null;

            case ConsoleKey.N:
            case ConsoleKey.Escape:
                // No / Cancel
                state.OnComplete?.Invoke(false);
                return null;

            default:
                return state;
        }
    }

    /// <summary>
    /// Handle tabs navigation keys. Returns new state after processing the key.
    /// </summary>
    [Obsolete("Use TabBarState.HandleKey instead.")]
    public static TabsState HandleTabsNavigation(ConsoleKeyInfo key, TabsState state)
    {
        return key.Key switch
        {
            ConsoleKey.LeftArrow or ConsoleKey.H => state with
            {
                SelectedIndex = Math.Max(0, state.SelectedIndex - 1)
            },
            ConsoleKey.RightArrow or ConsoleKey.L => state with
            {
                SelectedIndex = Math.Min(state.TabCount - 1, state.SelectedIndex + 1)
            },
            _ => state
        };
    }

    /// <summary>
    /// Handle command input with history. Returns new state and optionally a parsed command on Enter.
    /// The returned state has its own history list; the input state's list is never changed.
    /// </summary>
    [Obsolete("Use CommandLineState.HandleKey(key, out submitted) instead.")]
    public static CommandInputResult HandleCommandInput(ConsoleKeyInfo key, CommandInputState state)
    {
        var history = state.History ?? [];
        var line = new CommandLineState(history) { MaxHistory = int.MaxValue };
        line.Text = state.Value ?? "";
        line.Cursor = state.CursorPos;
        line.HistoryIndex = Math.Clamp(state.HistoryIndex, 0, history.Count);

        line.HandleKey(key, out var command);
        var index = line.HistoryIndex;
        var newHistory = line.History.Count != history.Count ? [.. line.History] : history;
        return new(new CommandInputState(line.Text, line.Cursor, newHistory, index), command);
    }
}

/// <summary>
/// Simple focus manager for navigating between UI elements.
/// </summary>
public class FocusManager
{
    private int _current;
    private readonly int _count;

    /// <summary>Index of currently focused element.</summary>
    public int Current => _current;

    /// <summary>Create a focus manager for the given number of elements.</summary>
    public FocusManager(int elementCount)
    {
        _count = elementCount;
        _current = 0;
    }

    /// <summary>Move focus to the next element (wraps around).</summary>
    public void Next() => _current = (_current + 1) % _count;

    /// <summary>Move focus to the previous element (wraps around).</summary>
    public void Previous() => _current = (_current - 1 + _count) % _count;

    /// <summary>Set focus to a specific element index.</summary>
    public void Set(int index) => _current = Math.Clamp(index, 0, _count - 1);

    /// <summary>Check if element at index is focused.</summary>
    public bool IsFocused(int index) => _current == index;

    /// <summary>
    /// Handle common navigation keys. Returns true if handled.
    /// </summary>
    public bool HandleInput(ConsoleKeyInfo? key)
    {
        if (key == null) return false;

        switch (key.Value.Key)
        {
            case ConsoleKey.Tab:
                if (key.Value.Modifiers.HasFlag(ConsoleModifiers.Shift))
                    Previous();
                else
                    Next();
                return true;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                Next();
                return true;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                Previous();
                return true;
        }

        return false;
    }
}

/// <summary>
/// Result of executing a command.
/// </summary>
public record CommandResult(bool Success, string? Message = null, bool ShouldQuit = false)
{
    /// <summary>Successful result with no message.</summary>
    public static CommandResult Ok() => new(true);

    /// <summary>Successful result with a message.</summary>
    public static CommandResult Ok(string message) => new(true, message);

    /// <summary>Failed result with an error message.</summary>
    public static CommandResult Error(string message) => new(false, message);

    /// <summary>Result that signals the app should quit.</summary>
    public static CommandResult Quit(string? message = null) => new(true, message, ShouldQuit: true);
}

/// <summary>
/// Defines a command with aliases, usage, and handler.
/// </summary>
public class Command
{
    internal string Name { get; }
    internal List<string> Aliases { get; } = [];
    internal string? Usage { get; set; }
    internal string? Description { get; set; }
    internal Func<ParsedCommand, CommandResult>? Handler { get; set; }
    internal CommandRegistry? SubCommands { get; set; }

    internal Command(string name) => Name = name;

    internal bool Matches(string name) =>
        Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
        Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Fluent builder for configuring a command.
/// </summary>
public class CommandBuilder
{
    private readonly Command _command;
    private readonly CommandRegistry _registry;

    internal CommandBuilder(Command command, CommandRegistry registry)
    {
        _command = command;
        _registry = registry;
    }

    /// <summary>Set the usage string (e.g., "&lt;text&gt;" or "@person &lt;text&gt;").</summary>
    public CommandBuilder Usage(string usage)
    {
        _command.Usage = usage;
        return this;
    }

    /// <summary>Set the description shown in help.</summary>
    public CommandBuilder Description(string description)
    {
        _command.Description = description;
        return this;
    }

    /// <summary>Set the handler that processes the command.</summary>
    public CommandBuilder Handler(Action<ParsedCommand> handler)
    {
        _command.Handler = cmd => { handler(cmd); return CommandResult.Ok(); };
        return this;
    }

    /// <summary>Set the handler that processes the command and returns a result.</summary>
    public CommandBuilder Handler(Func<ParsedCommand, CommandResult> handler)
    {
        _command.Handler = handler;
        return this;
    }

    /// <summary>Configure subcommands for this command.</summary>
    public CommandBuilder SubCommands(Action<CommandRegistry> configure)
    {
        _command.SubCommands = new CommandRegistry();
        configure(_command.SubCommands);
        return this;
    }

    /// <summary>Add another command to the registry.</summary>
    public CommandBuilder Add(string name, params string[] aliases) => _registry.Add(name, aliases);

    /// <summary>Build the registry.</summary>
    public CommandRegistry Build() => _registry;
}

/// <summary>
/// Registry for commands with fluent configuration and execution.
/// </summary>
public class CommandRegistry
{
    private readonly List<Command> _commands = [];

    /// <summary>Create a new command registry.</summary>
    public static CommandRegistry Create() => new();

    /// <summary>Add a command with optional aliases.</summary>
    public CommandBuilder Add(string name, params string[] aliases)
    {
        var command = new Command(name);
        command.Aliases.AddRange(aliases);
        _commands.Add(command);
        return new CommandBuilder(command, this);
    }

    /// <summary>
    /// Execute a parsed command. Returns null if no matching command found.
    /// </summary>
    public CommandResult? Execute(ParsedCommand? command)
    {
        if (command == null) return null;

        var match = _commands.FirstOrDefault(c => c.Matches(command.Name));
        if (match == null) return null;

        // Handle subcommands
        if (match.SubCommands != null && command.Args.Length > 0)
        {
            var subCommand = ParsedCommand.Parse($":{command.Args[0]} {command.GetArgsFrom(1) ?? ""}");
            var subResult = match.SubCommands.Execute(subCommand);
            if (subResult != null) return subResult;
        }

        // Execute the main handler
        if (match.Handler != null)
            return match.Handler(command);

        return CommandResult.Error($"Command '{command.Name}' has no handler");
    }

    /// <summary>
    /// Try to execute a command. Returns false if no matching command found.
    /// </summary>
    public bool TryExecute(ParsedCommand? command, out CommandResult? result)
    {
        result = Execute(command);
        return result != null;
    }

    /// <summary>
    /// Generate help lines for all registered commands.
    /// </summary>
    public IEnumerable<string> GetHelpLines(string prefix = ":")
    {
        foreach (var cmd in _commands)
        {
            var aliases = cmd.Aliases.Count > 0
                ? $" ({string.Join(", ", cmd.Aliases.Select(a => prefix + a))})"
                : "";

            var usage = cmd.Usage != null ? $" {cmd.Usage}" : "";
            var line = $"  {prefix}{cmd.Name}{usage}{aliases}";

            if (cmd.Description != null)
                line += $"  - {cmd.Description}";

            yield return line;

            // Include subcommand help
            if (cmd.SubCommands != null)
            {
                foreach (var subLine in cmd.SubCommands.GetHelpLines($"{prefix}{cmd.Name} "))
                    yield return subLine;
            }
        }
    }

    /// <summary>
    /// Get help lines as an array.
    /// </summary>
    public string[] GetHelp(string prefix = ":") => GetHelpLines(prefix).ToArray();
}

/// <summary>
/// Routes input to focused elements with fluent configuration.
/// </summary>
public class InputRouter
{
    private readonly List<(string Name, Action<ConsoleKeyInfo> Handler, bool CapturesText)> _handlers = [];
    private readonly List<Func<ConsoleKeyInfo, bool>> _globals = [];
    private readonly List<(Func<bool> IsActive, Action<ConsoleKeyInfo> Handler)> _modals = [];
    private readonly List<bool> _modalWasActive = [];
    private readonly List<int> _activeOrder = []; // modal indices in activation order
    private int _focusIndex;

    /// <summary>
    /// Register a named focus element with its input handler. Set <paramref name="capturesText"/> for text fields,
    /// so global handlers don't see the keys typed into them.
    /// </summary>
    public InputRouter Add(string name, Action<ConsoleKeyInfo> handler, bool capturesText = false)
    {
        _handlers.Add((name, handler, capturesText));
        return this;
    }

    /// <summary>
    /// Register a handler that sees keys before the focused element (but after an active modal).
    /// Return true if it handled the key. Skipped while the focused element captures text.
    /// </summary>
    public InputRouter Global(Func<ConsoleKeyInfo, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _globals.Add(handler);
        return this;
    }

    /// <summary>
    /// Configure modal handling. Modal takes priority when active.
    /// Multiple modals can be registered; the most recently activated modal receives input.
    /// </summary>
    public InputRouter WithModal(Func<bool> isActive, Action<ConsoleKeyInfo> handler)
    {
        _modals.Add((isActive, handler));
        _modalWasActive.Add(false);
        return this;
    }

    /// <summary>
    /// Set which element starts with focus.
    /// </summary>
    public InputRouter StartWith(string name)
    {
        var index = _handlers.FindIndex(h => h.Name == name);
        if (index >= 0) _focusIndex = index;
        return this;
    }

    /// <summary>
    /// Get the name of the currently focused element.
    /// </summary>
    public string CurrentFocus => _handlers.Count > 0 ? _handlers[_focusIndex].Name : "";

    /// <summary>
    /// Check if any modal is currently active.
    /// </summary>
    public bool IsModalActive => _activeOrder.Count > 0;

    /// <summary>
    /// Check if a named element is focused.
    /// </summary>
    public bool IsFocused(string name) => _handlers.Count > 0 && _handlers[_focusIndex].Name == name;

    /// <summary>
    /// Move focus to the next element.
    /// </summary>
    public void FocusNext()
    {
        if (_handlers.Count > 0)
            _focusIndex = (_focusIndex + 1) % _handlers.Count;
    }

    /// <summary>
    /// Move focus to the previous element.
    /// </summary>
    public void FocusPrevious()
    {
        if (_handlers.Count > 0)
            _focusIndex = (_focusIndex - 1 + _handlers.Count) % _handlers.Count;
    }

    /// <summary>
    /// Set focus to a named element.
    /// </summary>
    public void SetFocus(string name)
    {
        var index = _handlers.FindIndex(h => h.Name == name);
        if (index >= 0) _focusIndex = index;
    }

    /// <summary>
    /// Recompute which modals are active. <see cref="App"/> calls this every loop iteration, so a modal opened
    /// from code (not a key press) is active at the next render.
    /// </summary>
    public void Refresh()
    {
        for (int i = 0; i < _modals.Count; i++)
        {
            bool isActive = _modals[i].IsActive();
            bool wasActive = _modalWasActive[i];

            if (isActive && !wasActive)
            {
                // Modal just became active - add to top of stack
                _activeOrder.Add(i);
            }
            else if (!isActive && wasActive)
            {
                // Modal just became inactive - remove from stack
                _activeOrder.Remove(i);
            }

            _modalWasActive[i] = isActive;
        }
    }

    /// <summary>
    /// Handle a key press. Routes to the active modal, then global handlers, then Tab, then the focused element.
    /// </summary>
    public void Handle(ConsoleKeyInfo key)
    {
        Refresh();

        // Top of stack gets input
        if (_activeOrder.Count > 0)
        {
            int topIndex = _activeOrder[^1];
            _modals[topIndex].Handler(key);
            return;
        }

        var capturesText = _handlers.Count > 0 && _handlers[_focusIndex].CapturesText;
        if (!capturesText)
        {
            foreach (var global in _globals)
                if (global(key)) return;
        }

        // Tab switches focus
        if (key.Key == ConsoleKey.Tab)
        {
            if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                FocusPrevious();
            else
                FocusNext();
            return;
        }

        // Route to focused handler
        if (_handlers.Count > 0)
            _handlers[_focusIndex].Handler(key);
    }
}
