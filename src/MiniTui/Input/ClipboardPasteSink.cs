namespace MiniTui;

/// <summary>
/// Conhost has no bracketed paste, so on Windows a Ctrl+V that reaches the app as a key becomes a paste of the
/// clipboard's text (1.x spec §4.5). If the clipboard has no text, the key goes through.
/// </summary>
internal sealed class ClipboardPasteSink(IInputSink inner, Func<string?> readClipboard) : IInputSink
{
    public void Key(ConsoleKeyInfo key)
    {
        if (key.KeyChar == '\x16' && key.Modifiers == ConsoleModifiers.Control
            && readClipboard() is { Length: > 0 } text)
        {
            inner.Paste(AnsiInputDecoder.NormalizeLineBreaks(text));
            return;
        }
        inner.Key(key);
    }

    public void Paste(string text) => inner.Paste(text);
}
