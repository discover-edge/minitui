namespace MiniTui.Tests;

public class ParsedCommandTests
{
    [Theory]
    [InlineData(":add john Fixed the bug", "add", new[] { "john", "Fixed", "the", "bug" })]
    [InlineData("  :Quit  ", "quit", new string[0])]
    [InlineData(":add   spaced    args", "add", new[] { "spaced", "args" })]
    public void Parse_SplitsNameAndArgs(string input, string name, string[] args)
    {
        var command = ParsedCommand.Parse(input);

        Assert.NotNull(command);
        Assert.Equal(name, command.Name);
        Assert.Equal(args, command.Args);
        Assert.Equal(input.Trim(), command.Raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("add x")]
    [InlineData(":")]
    [InlineData(":   ")]
    public void Parse_ReturnsNull_ForNonCommands(string input)
    {
        Assert.Null(ParsedCommand.Parse(input));
    }

    [Fact]
    public void ArgHelpers()
    {
        var command = ParsedCommand.Parse(":add john Fixed the bug")!;

        Assert.Equal("john Fixed the bug", command.ArgsAsString);
        Assert.Equal("john", command.GetArg(0));
        Assert.Null(command.GetArg(9));
        Assert.Equal("Fixed the bug", command.GetArgsFrom(1));
        Assert.Null(command.GetArgsFrom(9));
        Assert.Equal("", ParsedCommand.Parse(":quit")!.ArgsAsString);
    }
}

public class CommandRegistryTests
{
    private static CommandRegistry Registry(List<string> log) => CommandRegistry.Create()
        .Add("add", "a").Usage("<text>").Description("Add a note")
            .Handler(cmd => log.Add($"add {cmd.ArgsAsString}"))
        .Add("delete", "d", "rm").Usage("<index>")
            .Handler(cmd => int.TryParse(cmd.GetArg(0), out _) ? CommandResult.Ok("Deleted") : CommandResult.Error("Bad index"))
        .Add("git").Description("Git commands")
            .SubCommands(git => git
                .Add("push").Description("Push").Handler(_ => CommandResult.Ok("pushed"))
                .Add("pull").Handler(_ => CommandResult.Ok("pulled")))
            .Handler(_ => CommandResult.Error("Usage: :git <push|pull>"))
        .Add("quit", "q").Handler(_ => CommandResult.Quit("Bye"))
        .Add("empty")
        .Build();

    [Fact]
    public void Execute_MatchesNamesAndAliases_CaseInsensitively()
    {
        var log = new List<string>();
        var registry = Registry(log);

        Assert.Equal(CommandResult.Ok(), registry.Execute(ParsedCommand.Parse(":add milk")));
        Assert.Equal(CommandResult.Ok(), registry.Execute(ParsedCommand.Parse(":A eggs")));
        Assert.Equal(["add milk", "add eggs"], log);
        Assert.Equal("Deleted", registry.Execute(ParsedCommand.Parse(":rm 2"))!.Message);
        Assert.False(registry.Execute(ParsedCommand.Parse(":d x"))!.Success);
    }

    [Fact]
    public void Execute_ReturnsNull_ForUnknownOrMissingCommands()
    {
        var registry = Registry([]);

        Assert.Null(registry.Execute(ParsedCommand.Parse(":nope")));
        Assert.Null(registry.Execute(null));
        Assert.False(registry.TryExecute(ParsedCommand.Parse(":nope"), out var result));
        Assert.Null(result);
        Assert.True(registry.TryExecute(ParsedCommand.Parse(":quit"), out result));
        Assert.True(result!.ShouldQuit);
        Assert.Equal("Bye", result.Message);
    }

    [Fact]
    public void SubCommands_RunBeforeTheParentHandler()
    {
        var registry = Registry([]);

        Assert.Equal("pushed", registry.Execute(ParsedCommand.Parse(":git push"))!.Message);
        Assert.Equal("pulled", registry.Execute(ParsedCommand.Parse(":git pull --rebase"))!.Message);
        Assert.Equal("Usage: :git <push|pull>", registry.Execute(ParsedCommand.Parse(":git fetch"))!.Message);
        Assert.Equal("Usage: :git <push|pull>", registry.Execute(ParsedCommand.Parse(":git"))!.Message);
    }

    [Fact]
    public void CommandWithoutHandler_ReturnsAnError()
    {
        var result = Registry([]).Execute(ParsedCommand.Parse(":empty"));

        Assert.Equal(CommandResult.Error("Command 'empty' has no handler"), result);
    }

    [Fact]
    public void Help_ListsUsageAliasesDescriptionsAndSubcommands()
    {
        Assert.Equal(
            [
                "  :add <text> (:a)  - Add a note",
                "  :delete <index> (:d, :rm)",
                "  :git  - Git commands",
                "  :git push  - Push",
                "  :git pull",
                "  :quit (:q)",
                "  :empty",
            ],
            Registry([]).GetHelp());
    }
}

public class TimedMessageTests
{
    [Fact]
    public void ShowsUntilItExpires()
    {
        var visible = TimedMessage.Show("Saved", TimeSpan.FromMinutes(1));
        var expired = TimedMessage.Show("Old", seconds: -1);

        Assert.True(visible.IsVisible);
        Assert.Equal("Saved", visible.GetOrDefault("idle"));
        Assert.False(expired.IsVisible);
        Assert.Equal("idle", expired.GetOrDefault("idle"));
        Assert.False(TimedMessage.Empty.IsVisible);
    }
}
