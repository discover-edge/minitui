namespace MiniTui.Tests;

public class InputSourceTests
{
    [Fact]
    public void ScriptedInput_ReadsKeysInOrder()
    {
        using var input = new ScriptedInputSource();
        var escape = new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false);
        input.EnqueueText("aZ1 ").Enqueue(escape);

        var keys = new List<ConsoleKeyInfo>();
        while (input.TryRead(out var key)) keys.Add(key);

        Assert.Equal(
        [
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('Z', ConsoleKey.Z, true, false, false),
            new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false),
            new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false),
            escape,
        ], keys);
        Assert.Equal(0, input.Pending);
    }

    [Fact]
    public void ScriptedInput_Empty_ReturnsFalse()
    {
        using var input = new ScriptedInputSource();

        Assert.False(input.TryRead(out _));
    }

    [Fact]
    public void ScriptedInput_MapsControlCharacters()
    {
        using var input = new ScriptedInputSource();
        input.EnqueueText("\n\t\b");

        input.TryRead(out var enter);
        input.TryRead(out var tab);
        input.TryRead(out var backspace);

        Assert.Equal((ConsoleKey.Enter, ConsoleKey.Tab, ConsoleKey.Backspace), (enter.Key, tab.Key, backspace.Key));
    }

    [Fact]
    public void ScriptedInput_IsThreadSafe()
    {
        using var input = new ScriptedInputSource();

        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 1000; i++) input.EnqueueText("x");
        });

        Assert.Equal(8000, input.Pending);
    }

    [Fact]
    public void LegacyBackend_CapsColorModeAt16Colors()
    {
        Assert.Equal(ColorMode.Ansi16, new LegacyConsoleBackend(ColorMode.TrueColor).ColorMode);
        Assert.Equal(ColorMode.None, new LegacyConsoleBackend(ColorMode.None).ColorMode);
    }
}
