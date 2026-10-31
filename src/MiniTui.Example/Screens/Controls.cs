using MiniTui;

namespace MiniTui.Example;

/// <summary>A labelled text field that captures typing while focused.</summary>
internal sealed class TextInput(string label, string placeholder = "") : Component
{
    public TextFieldState State { get; } = new();
    public override bool CapturesText => true;
    public override bool HandleKey(ConsoleKeyInfo key) => State.HandleKey(key);
    public override bool HandlePaste(string text) => State.Paste(text);

    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        Widget.TextField()
            .State(State)
            .Placeholder(placeholder)
            .Block(Widget.Block().Title($" {label} "))
            .Focused(ctx.Focused)
            .Draw(buf, area);
}

/// <summary>A multi-line text area in a titled block (1.x spec §6). Enter is a line break; a click places the cursor.</summary>
internal sealed class NotesInput(string label, string placeholder = "") : Component
{
    public TextAreaState State { get; } = new();
    public override bool CapturesText => true;
    public override bool HandleKey(ConsoleKeyInfo key) => State.HandleKey(key);
    public override bool HandlePaste(string text) => State.Paste(text);
    public override bool HandleMouse(MouseEvent mouse) => State.HandleMouse(mouse);

    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        Widget.TextArea()
            .State(State)
            .Placeholder(placeholder)
            .Block(Widget.Block().Title($" {label} "))
            .Focused(ctx.Focused)
            .Draw(buf, area);
}

/// <summary>A checkbox toggled with Space, Enter or a click.</summary>
internal sealed class Toggle(string label) : Component
{
    public bool Checked { get; set; }

    public override bool HandleKey(ConsoleKeyInfo key) => key.Key is ConsoleKey.Spacebar or ConsoleKey.Enter && Flip();

    public override bool HandleMouse(MouseEvent mouse) => mouse.IsClick && Flip();

    private bool Flip()
    {
        Checked = !Checked;
        return true;
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        Widget.Checkbox().Label(label).Checked(Checked).Focused(ctx.Focused).Draw(buf, area);
}

/// <summary>A scrolling list of strings with its own selection.</summary>
internal sealed class ListView(string title, Func<IReadOnlyList<string>> items) : Component
{
    public SelectionState Selection { get; } = new();
    public override string KeysGroup => title.Trim();
    public override bool HandleKey(ConsoleKeyInfo key) => Selection.HandleKey(key, items().Count);
    public override bool HandleMouse(MouseEvent mouse) => Selection.HandleMouse(mouse, items().Count);

    public override void Render(Buffer buf, Rect area, RenderContext ctx) =>
        Widget.List().Title(title).Items(items()).State(Selection).HighlightSymbol("> ").Focused(ctx.Focused).Draw(buf, area);
}

/// <summary>Helpers for demos that animate.</summary>
internal static class Animation
{
    /// <summary>
    /// Call <paramref name="step"/> every <paramref name="interval"/> on the UI thread until <paramref name="lifetime"/>
    /// ends (the screen is popped), redrawing after each step.
    /// </summary>
    public static void Every(App app, TimeSpan interval, CancellationToken lifetime, Action step) =>
        app.RunTask(async ct =>
        {
            using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
            try
            {
                while (true)
                {
                    await Task.Delay(interval, both.Token);
                    step();
                    app.RequestRedraw();
                }
            }
            catch (OperationCanceledException) when (both.IsCancellationRequested)
            {
            }
        });
}
