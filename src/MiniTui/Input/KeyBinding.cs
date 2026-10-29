using System.Globalization;

namespace MiniTui;

/// <summary>
/// One key combination, such as <c>q</c>, <c>ctrl+r</c> or <c>shift+tab</c> (§8.5).
/// </summary>
/// <param name="Key">The key, for named keys and Ctrl/Alt combinations. 0 for a plain character binding.</param>
/// <param name="Char">The character, for a binding that matches what was typed. Null for named keys.</param>
/// <param name="Modifiers">The modifiers that must be held. Ignored for plain character bindings, where Shift is part of the character.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Name from the spec (§8.5): the character the binding matches.")]
public readonly record struct KeyBinding(ConsoleKey Key, char? Char, ConsoleModifiers Modifiers)
{
    private static readonly Dictionary<string, ConsoleKey> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = ConsoleKey.Enter,
        ["esc"] = ConsoleKey.Escape,
        ["tab"] = ConsoleKey.Tab,
        ["backspace"] = ConsoleKey.Backspace,
        ["delete"] = ConsoleKey.Delete,
        ["insert"] = ConsoleKey.Insert,
        ["up"] = ConsoleKey.UpArrow,
        ["down"] = ConsoleKey.DownArrow,
        ["left"] = ConsoleKey.LeftArrow,
        ["right"] = ConsoleKey.RightArrow,
        ["home"] = ConsoleKey.Home,
        ["end"] = ConsoleKey.End,
        ["pgup"] = ConsoleKey.PageUp,
        ["pgdn"] = ConsoleKey.PageDown,
        ["space"] = ConsoleKey.Spacebar,
    };

    /// <summary>
    /// Parse one binding: <c>(mod "+")* name</c>, where mod is ctrl, alt or shift and name is a single
    /// printable character or one of enter esc tab backspace delete insert up down left right home end pgup pgdn
    /// space f1–f12. Case-insensitive, except that a character binding matches the character typed:
    /// <c>"R"</c> and <c>"shift+r"</c> both match Shift+R.
    /// </summary>
    /// <exception cref="FormatException">The text isn't a valid binding.</exception>
    public static KeyBinding Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var text = s.Trim();
        if (text.Length == 0) throw Invalid(s, "it is empty");

        // A trailing "+" is the plus key ("+", "ctrl++"), not a separator
        string name;
        string[] mods;
        if (text == "+") (name, mods) = ("+", []);
        else if (text.EndsWith("++", StringComparison.Ordinal)) (name, mods) = ("+", text[..^2].Split('+'));
        else
        {
            var parts = text.Split('+');
            (name, mods) = (parts[^1], parts[..^1]);
        }

        var modifiers = (ConsoleModifiers)0;
        foreach (var part in mods)
        {
            var mod = part.Trim().ToLowerInvariant() switch
            {
                "ctrl" => ConsoleModifiers.Control,
                "alt" => ConsoleModifiers.Alt,
                "shift" => ConsoleModifiers.Shift,
                _ => throw Invalid(s, $"'{part}' is not ctrl, alt or shift"),
            };
            if ((modifiers & mod) != 0) throw Invalid(s, $"'{part}' is repeated");
            modifiers |= mod;
        }

        name = name.Trim();
        if (name.Length == 0) throw Invalid(s, "the key name is missing");

        if (Names.TryGetValue(name, out var named) || TryFunctionKey(name, out named))
            return new KeyBinding(named, null, modifiers);

        if (name.Length != 1 || char.IsControl(name[0]) || char.IsWhiteSpace(name[0]))
            throw Invalid(s, $"'{name}' is not a printable character or key name");

        var c = name[0];
        var command = modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt);
        if (command == 0)
        {
            // Shift is part of the typed character
            if ((modifiers & ConsoleModifiers.Shift) != 0) c = char.ToUpperInvariant(c);
            return new KeyBinding(0, c, 0);
        }

        // Ctrl/Alt combinations have no reliable character, so letters and digits match by key, ignoring case
        var lower = char.ToLowerInvariant(c);
        if (lower is >= 'a' and <= 'z')
            return new KeyBinding(ConsoleKey.A + (lower - 'a'), lower, modifiers);
        if (c is >= '0' and <= '9')
            return new KeyBinding(ConsoleKey.D0 + (c - '0'), c, modifiers);
        return new KeyBinding(0, c, modifiers);
    }

    /// <summary>A single character without Ctrl or Alt, which a text field would type.</summary>
    internal bool IsPlainCharacter => Key == 0 && (Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

    /// <summary>A key press that matches this binding, for tests and <see cref="ComponentHarness"/>.</summary>
    internal ConsoleKeyInfo ToKeyInfo()
    {
        var shift = (Modifiers & ConsoleModifiers.Shift) != 0;
        var alt = (Modifiers & ConsoleModifiers.Alt) != 0;
        var ctrl = (Modifiers & ConsoleModifiers.Control) != 0;
        if (Key == 0)
        {
            var c = Char ?? ' ';
            var key = char.IsAsciiLetter(c) ? ConsoleKey.A + (char.ToLowerInvariant(c) - 'a')
                : char.IsAsciiDigit(c) ? ConsoleKey.D0 + (c - '0') : 0;
            return new ConsoleKeyInfo(c, key, char.IsAsciiLetterUpper(c), alt, ctrl);
        }

        var keyChar = Key switch
        {
            >= ConsoleKey.A and <= ConsoleKey.Z when ctrl => (char)(Key - ConsoleKey.A + 1),
            >= ConsoleKey.A and <= ConsoleKey.Z => Char ?? '\0',
            >= ConsoleKey.D0 and <= ConsoleKey.D9 => Char ?? '\0',
            ConsoleKey.Enter => '\r',
            ConsoleKey.Tab => '\t',
            ConsoleKey.Escape => '\x1b',
            ConsoleKey.Spacebar => ' ',
            ConsoleKey.Backspace => '\b',
            _ => '\0',
        };
        return new ConsoleKeyInfo(keyChar, Key, shift, alt, ctrl);
    }

    /// <summary>True if <paramref name="key"/> is this combination.</summary>
    public bool Matches(ConsoleKeyInfo key)
    {
        if (Key == 0)
        {
            // A character binding: what was typed, with Ctrl/Alt matching exactly
            if (Char is not { } c || key.KeyChar != c) return false;
            var command = Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt);
            return command == 0 ? !KeyInput.HasCommandModifier(key) : (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == command;
        }

        if (key.Key == Key) return key.Modifiers == Modifiers;

        // Terminals send Ctrl+letter as a control character, which some sources report without the key
        return Key is >= ConsoleKey.A and <= ConsoleKey.Z
            && Modifiers == ConsoleModifiers.Control
            && key.KeyChar == (char)(Key - ConsoleKey.A + 1);
    }

    /// <summary>The binding in the form <see cref="Parse"/> accepts, for example <c>ctrl+r</c>.</summary>
    public override string ToString()
    {
        var prefix = "";
        if ((Modifiers & ConsoleModifiers.Control) != 0) prefix += "ctrl+";
        if ((Modifiers & ConsoleModifiers.Alt) != 0) prefix += "alt+";
        if (Key == 0) return prefix + Char;

        var shift = (Modifiers & ConsoleModifiers.Shift) != 0 ? "shift+" : "";
        if (Char is { } c) return prefix + shift + c;
        if (Key is >= ConsoleKey.F1 and <= ConsoleKey.F12) return prefix + shift + "f" + (Key - ConsoleKey.F1 + 1).ToString(CultureInfo.InvariantCulture);
        foreach (var (n, k) in Names)
            if (k == Key) return prefix + shift + n;
        return prefix + shift + Key.ToString().ToLowerInvariant();
    }

    private static bool TryFunctionKey(string name, out ConsoleKey key)
    {
        key = 0;
        if (name.Length is < 2 or > 3 || name[0] is not ('f' or 'F')) return false;
        if (!int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n is < 1 or > 12)
            return false;
        key = ConsoleKey.F1 + (n - 1);
        return true;
    }

    private static FormatException Invalid(string s, string reason) => new($"Invalid key binding '{s}': {reason}.");
}
