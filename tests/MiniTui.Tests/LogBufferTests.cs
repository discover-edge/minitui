namespace MiniTui.Tests;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;

public class LogBufferTests
{
    [Fact]
    public void KeepsTheLastLines_OldestFirst()
    {
        var log = new LogBuffer(capacity: 3);
        for (var i = 1; i <= 5; i++) log.Add($"line {i}", isError: i == 5);

        var entries = log.Snapshot();

        Assert.Equal(["line 3", "line 4", "line 5"], entries.Select(e => e.Text));
        Assert.True(entries[2].IsError);
        Assert.Equal(3, log.Count);
    }

    [Fact]
    public void Clear_EmptiesIt()
    {
        var log = new LogBuffer();
        log.Add("x");
        log.Clear();

        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void Capacity_MustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogBuffer(0));
}

[Collection("Console")]
public class ConsoleCaptureTests
{
    [Fact]
    public async Task WhileRunning_ConsoleWritesGoToTheLog_AndStderrIsReplayedOnExit()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            App app = null!;
            app = App.Create().WithBackend(new TestBackend(20, 5)).WithInput(new ScriptedInputSource()).WithTickRate(null)
                .OnSetup(() =>
                {
                    Console.WriteLine("hello");
                    Console.Error.Write("bad ");
                    Console.Error.WriteLine("thing");
                    Console.Write("partial");
                    app.Quit();
                })
                .Render((_, _) => { });

            await AppHarness.Run(app);

            Assert.Equal(
                [(false, "hello"), (true, "bad thing"), (false, "partial")],
                app.Log.Snapshot().Select(e => (e.IsError, e.Text)));
            Console.Error.WriteLine("after");
            Assert.Same(originalOut, Console.Out);
            Assert.Equal($"bad thing{Environment.NewLine}after{Environment.NewLine}", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task CaptureOff_LeavesTheConsoleAlone()
    {
        TextWriter? during = null;
        App app = null!;
        app = AppHarness.Create(new TestBackend(20, 5), new ScriptedInputSource())
            .OnSetup(() => { during = Console.Out; app.Quit(); })
            .Render((_, _) => { });

        await AppHarness.Run(app);

        Assert.Same(Console.Out, during);
    }
}
