namespace MiniTui.Tests;

// Keeps the README's app-testing example honest.
public class ReadmeSnippetTests
{
    [Fact]
    public void TestingAWholeApp()
    {
        var backend = new TestBackend(40, 10);
        var input = new ScriptedInputSource().EnqueueText("kkq");
        var count = 0;

        var app = App.Create().WithBackend(backend).WithInput(input).WithTickRate(null).CaptureConsoleOutput(false);
        app.OnKey(key =>
            {
                if (key.KeyChar == 'k') count++;
                if (key.KeyChar == 'q') app.Quit();
                return true;
            })
            .Render((buf, _) => buf.SetString(0, 0, $"Count: {count}", Style.Default))
            .Run();

        Assert.Equal(2, count);
    }

    [Fact]
    public void KeyMapHints()
    {
        var keys = new KeyMap()
            .Add("r", "Refresh", () => { })
            .Add("j,down", "Down", () => { }, group: "Navigation")
            .Add("ctrl+s", "Save", () => { })
            .Add("q,esc", "Quit", () => { });

        Assert.Equal("r Refresh  j Down  ctrl+s Save  q Quit", keys.Hints(40));
    }
}
