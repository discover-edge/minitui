using System.Globalization;
using System.Text;

namespace MiniTui;

/// <summary>
/// Display width of text in terminal columns, by grapheme cluster (UAX #29).
/// Width tables come from Unicode 16.0; see <c>tools/GenWidthTable.cs</c>.
/// </summary>
public static class UnicodeWidth
{
    /// <summary>Total display width of <paramref name="text"/>: the sum over its grapheme clusters.</summary>
    public static int Of(ReadOnlySpan<char> text)
    {
        var width = 0;
        while (!text.IsEmpty)
        {
            if (text[0] is >= ' ' and <= '~' && (text.Length == 1 || text[1] < 0x300))
            {
                width++;
                text = text[1..];
                continue;
            }
            var length = StringInfo.GetNextTextElementLength(text);
            width += OfGrapheme(text[..length]);
            text = text[length..];
        }
        return width;
    }

    /// <summary>Display width of a single grapheme cluster: 0, 1 or 2.</summary>
    public static int OfGrapheme(ReadOnlySpan<char> grapheme)
    {
        if (grapheme.IsEmpty) return 0;
        if (Rune.DecodeFromUtf16(grapheme, out var first, out _) != System.Buffers.OperationStatus.Done)
            first = Rune.ReplacementChar;

        if (grapheme.Contains('️')) return 2;

        var runeCount = 0;
        foreach (var _ in grapheme.EnumerateRunes()) runeCount++;
        if (first.Value is >= 0x1F1E6 and <= 0x1F1FF && runeCount >= 2) return 2;

        var emojiPresentation = InTable(UnicodeWidthTable.EmojiPresentation, first.Value);
        if (grapheme.Contains('‍') && emojiPresentation) return 2;

        var category = Rune.GetUnicodeCategory(first);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
            || first.Value == 0x200B)
            return 0;

        if (emojiPresentation || InTable(UnicodeWidthTable.EastAsianWide, first.Value)) return 2;
        return 1;
    }

    /// <summary>Split text into grapheme clusters (StringInfo text elements).</summary>
    public static IEnumerable<string> Graphemes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
            yield return e.GetTextElement();
    }

    /// <summary>
    /// Truncate text to at most <paramref name="maxWidth"/> columns. If it doesn't fit, as many whole
    /// grapheme clusters as fit are kept and <paramref name="ellipsis"/> is appended.
    /// </summary>
    public static string Truncate(string text, int maxWidth, string ellipsis = "…")
    {
        ArgumentNullException.ThrowIfNull(text);
        ellipsis ??= "";
        if (maxWidth <= 0) return "";
        if (Of(text) <= maxWidth) return text;

        var ellipsisWidth = Of(ellipsis);
        if (ellipsisWidth > maxWidth)
        {
            ellipsis = "";
            ellipsisWidth = 0;
        }

        var budget = maxWidth - ellipsisWidth;
        var width = 0;
        var end = 0;
        var span = text.AsSpan();
        while (end < span.Length)
        {
            var length = StringInfo.GetNextTextElementLength(span[end..]);
            var w = OfGrapheme(span.Slice(end, length));
            if (width + w > budget) break;
            width += w;
            end += length;
        }
        return string.Concat(span[..end], ellipsis);
    }

    private static bool InTable(ReadOnlySpan<uint> table, int codePoint)
    {
        var cp = (uint)codePoint;
        int lo = 0, hi = table.Length / 2 - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (cp < table[mid * 2]) hi = mid - 1;
            else if (cp > table[mid * 2 + 1]) lo = mid + 1;
            else return true;
        }
        return false;
    }
}
