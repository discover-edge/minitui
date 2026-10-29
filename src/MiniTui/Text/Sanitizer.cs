using System.Globalization;
using System.Text;

namespace MiniTui;

/// <summary>
/// Cached single-character symbol strings, so ASCII rendering doesn't allocate.
/// </summary>
internal static class Symbols
{
    private static readonly string[] Ascii = CreateAscii();

    /// <summary>The cached string for a printable ASCII character (U+0020–U+007E).</summary>
    public static string OfAscii(char c) => Ascii[c - 0x20];

    /// <summary>The symbol for a single character, sanitized.</summary>
    public static string Of(char c)
    {
        if (c is >= ' ' and <= '~') return OfAscii(c);
        if (Sanitizer.IsControl(c) || char.IsSurrogate(c)) return Sanitizer.Replacement;
        return c.ToString();
    }

    private static string[] CreateAscii()
    {
        var table = new string[0x7F - 0x20];
        for (var i = 0; i < table.Length; i++)
            table[i] = ((char)(0x20 + i)).ToString();
        return table;
    }
}

/// <summary>
/// Removes control characters from drawn text so data can't inject terminal escape sequences (§4.6).
/// </summary>
internal static class Sanitizer
{
    /// <summary>U+FFFD REPLACEMENT CHARACTER.</summary>
    public const string Replacement = "\uFFFD";

    private const char ReplacementChar = '\uFFFD';

    /// <summary>C0 controls, DEL and C1 controls.</summary>
    public static bool IsControl(char c) => c < 0x20 || c is >= '\x7f' and <= '\x9f';

    /// <summary>True when every character is printable ASCII (U+0020–U+007E).</summary>
    public static bool IsPrintableAscii(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
            if (c is < ' ' or > '~') return false;
        return true;
    }

    /// <summary>
    /// Sanitize text for a single-line API: tab and line breaks (<c>\n</c>, <c>\r\n</c>) become
    /// one space, other controls become U+FFFD. Lone surrogates become U+FFFD.
    /// </summary>
    public static string SingleLine(ReadOnlySpan<char> text)
    {
        if (!NeedsSanitizing(text)) return text.ToString();

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                sb.Append(' ');
                i++;
            }
            else if (c is '\t' or '\n')
                sb.Append(' ');
            else if (IsControl(c))
                sb.Append(ReplacementChar);
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
                sb.Append(ReplacementChar);
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sanitize a cell symbol: controls become U+FFFD, and only the first grapheme cluster is kept.
    /// An empty or null symbol becomes a space.
    /// </summary>
    public static string Symbol(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return " ";
        if (symbol.Length == 1) return Symbols.Of(symbol[0]);

        var clean = SingleLine(symbol);
        var length = StringInfo.GetNextTextElementLength(clean);
        return length == clean.Length ? clean : clean[..length];
    }

    private static bool NeedsSanitizing(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsControl(c)) return true;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
            else if (char.IsSurrogate(c))
                return true;
        }
        return false;
    }
}
