// Also exercises APIs that are obsolete in 0.9 and removed in 1.0 (§9.7).
#pragma warning disable CS0618

namespace MiniTui.Tests;

public class RouterTests
{
    private static ConsoleKeyInfo Key(char c) => AppHarness.Key(c);

    [Fact]
    public void Refresh_ActivatesAModalWithoutAKeyPress()
    {
        var open = false;
        var router = new InputRouter().WithModal(() => open, _ => { });

        open = true;
        Assert.False(router.IsModalActive);
        router.Refresh();
        Assert.True(router.IsModalActive);

        open = false;
        router.Refresh();
        Assert.False(router.IsModalActive);
    }

    [Fact]
    public void Global_RunsBeforeTheFocusedHandler_AndCanHandleTheKey()
    {
        var order = new List<string>();
        var router = new InputRouter()
            .Add("list", _ => order.Add("list"))
            .Global(key => { order.Add("global"); return key.KeyChar == 'q'; });

        router.Handle(Key('q'));
        router.Handle(Key('j'));

        Assert.Equal(["global", "global", "list"], order);
    }

    [Fact]
    public void Global_IsSkipped_WhileTheFocusedElementCapturesText()
    {
        var typed = new List<char>();
        var globals = 0;
        var router = new InputRouter()
            .Add("input", key => typed.Add(key.KeyChar), capturesText: true)
            .Global(_ => { globals++; return true; });

        router.Handle(Key('q'));

        Assert.Equal(['q'], typed);
        Assert.Equal(0, globals);
    }

    [Fact]
    public void ActiveModal_TakesKeysBeforeGlobals()
    {
        var modal = 0;
        var globals = 0;
        var router = new InputRouter()
            .Add("list", _ => { })
            .Global(_ => { globals++; return true; })
            .WithModal(() => true, _ => modal++);

        router.Handle(Key('x'));

        Assert.Equal(1, modal);
        Assert.Equal(0, globals);
    }
}
