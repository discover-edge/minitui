namespace MiniTui;

/// <summary>Key classification shared by the state classes and <see cref="KeyBinding"/>.</summary>
internal static class KeyInput
{
    /// <summary>
    /// Ctrl or Alt is held, so the key is a command rather than text. Both together is AltGr on Windows,
    /// which types characters such as @ on many layouts.
    /// </summary>
    public static bool HasCommandModifier(ConsoleKeyInfo key)
    {
        var ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        var alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        return ctrl != alt;
    }

    /// <summary>Ctrl plus a letter, by key or by the control character terminals send for it.</summary>
    public static bool IsCtrl(ConsoleKeyInfo key, char letter)
    {
        var upper = char.ToUpperInvariant(letter);
        if (key.KeyChar == (char)(upper - 'A' + 1)) return true;
        return key.Key == ConsoleKey.A + (upper - 'A') && (key.Modifiers & ConsoleModifiers.Control) != 0;
    }

    public static bool HasControl(ConsoleKeyInfo key) => (key.Modifiers & ConsoleModifiers.Control) != 0;
}
