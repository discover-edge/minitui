namespace MiniTui;

/// <summary>
/// A command input: a prompt followed by a text field with history (see <see cref="CommandLineState"/>).
/// </summary>
public sealed class CommandLineBuilder
{
    private CommandLineState? _state;
    private string _prompt = "> ";
    private bool _focused = true;
    private Style? _style;

    /// <summary>The text, cursor and history.</summary>
    public CommandLineBuilder State(CommandLineState state) { _state = state; return this; }

    /// <summary>The prompt. The default is <c>"> "</c>.</summary>
    public CommandLineBuilder Prompt(string prompt) { _prompt = prompt ?? ""; return this; }

    /// <summary>Show the cursor. On by default.</summary>
    public CommandLineBuilder Focused(bool focused = true) { _focused = focused; return this; }

    /// <summary>A style patched over the theme's input style.</summary>
    public CommandLineBuilder Style(Style style) { _style = style; return this; }

    /// <summary>Draw the command line on the first row of <paramref name="area"/>.</summary>
    public void Draw(Buffer buf, Rect area)
    {
        ArgumentNullException.ThrowIfNull(buf);
        if (area.IsEmpty) return;
        var theme = Theme.Current;
        var row = area with { Height = 1 };
        WidgetText.Clear(buf, row);

        var promptWidth = WidgetText.DrawString(buf, area.X, area.Y, _prompt, area.Width, theme.Prompt);
        var field = new Rect(area.X + promptWidth, area.Y, area.Width - promptWidth, 1);
        var style = (_focused ? theme.InputFocused : theme.Input).Patch(_style ?? default);
        TextFieldRenderer.Draw(buf, field, _state ?? new CommandLineState(), _focused, style, placeholder: null);
    }
}
