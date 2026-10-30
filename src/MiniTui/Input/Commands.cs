namespace MiniTui;

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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0027:API with optional parameter(s) should have the most parameters amongst its public overloads", Justification = "The overloads differ by parameter type, so calls are never ambiguous; kept from 0.7 for source compatibility.")]
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
internal sealed class Command
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
