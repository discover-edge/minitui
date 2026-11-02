namespace MiniTui;

/// <summary>
/// A successful <see cref="Fuzzy.Match"/>: higher scores are better matches.
/// </summary>
/// <param name="Score">How well the text matched; only comparisons between scores mean anything.</param>
/// <param name="Positions">The matched indexes in the text, ascending, for highlighting.</param>
public readonly record struct FuzzyMatch(int Score, IReadOnlyList<int> Positions);

/// <summary>Fuzzy matching, as in <see cref="CommandPalette"/> (1.x spec §13.5).</summary>
public static class Fuzzy
{
    private const int WordStart = 10;
    private const int Consecutive = 5;
    private const int SameCase = 3;
    private const int MaxGapPenalty = 3;
    private const int ExactBonus = 15;
    private const int PrefixBonus = 10;

    // Large enough that no real score reaches it, small enough not to overflow when added to
    private const int None = int.MinValue / 4;

    /// <summary>
    /// Match <paramref name="pattern"/> against <paramref name="text"/>, ignoring case. Spaces in the pattern are
    /// skipped; every other pattern character must appear in the text in order. Word starts (after a space or
    /// punctuation, or a capital after a lowercase letter), runs of consecutive characters and the same case score
    /// higher, and skipped characters lower; a prefix of the text scores higher still.
    /// </summary>
    /// <returns>The match with the highest score, or null if the text doesn't match. An empty or all-space pattern matches every text with score 0.</returns>
    public static FuzzyMatch? Match(string pattern, string text)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(text);
        var chars = pattern.Where(c => c != ' ').ToArray();
        var n = chars.Length;
        var m = text.Length;
        if (n == 0) return new FuzzyMatch(0, []);
        if (n > m || !IsSubsequence(chars, text)) return null;

        // best[i, j]: the best score with pattern character i matched at text index j; from[i, j]: where i - 1 was
        var best = new int[n, m];
        var from = new int[n, m];
        for (var i = 0; i < n; i++)
        {
            // The best of best[i - 1, k] for k up to j - 4, where the gap penalty is at its cap
            var farBest = None;
            var farFrom = -1;
            for (var j = 0; j < m; j++)
            {
                best[i, j] = None;
                from[i, j] = -1;
                if (i > 0 && j >= 4 && best[i - 1, j - 4] > farBest)
                {
                    farBest = best[i - 1, j - 4];
                    farFrom = j - 4;
                }
                if (!SameLetter(chars[i], text[j])) continue;

                var own = CharScore(chars[i], text, j);
                if (i == 0)
                {
                    best[i, j] = own;
                    continue;
                }

                var previous = None;
                var previousAt = -1;
                for (var k = Math.Max(0, j - 3); k < j; k++)
                {
                    if (best[i - 1, k] == None) continue;
                    var gap = j - k - 1;
                    var candidate = best[i - 1, k] + (gap == 0 ? Consecutive : -gap);
                    if (candidate > previous)
                    {
                        previous = candidate;
                        previousAt = k;
                    }
                }
                if (farBest != None && farBest - MaxGapPenalty > previous)
                {
                    previous = farBest - MaxGapPenalty;
                    previousAt = farFrom;
                }
                if (previousAt < 0) continue;
                best[i, j] = previous + own;
                from[i, j] = previousAt;
            }
        }

        var end = -1;
        var score = None;
        for (var j = n - 1; j < m; j++)
        {
            if (best[n - 1, j] > score)
            {
                score = best[n - 1, j];
                end = j;
            }
        }
        if (end < 0) return null;

        var positions = new int[n];
        for (int i = n - 1, j = end; i >= 0; j = from[i, j], i--) positions[i] = j;
        if (positions[^1] == n - 1) score += n == m ? ExactBonus : PrefixBonus;
        return new FuzzyMatch(score, positions);
    }

    private static bool IsSubsequence(char[] chars, string text)
    {
        var i = 0;
        foreach (var c in text)
            if (i < chars.Length && SameLetter(chars[i], c)) i++;
        return i == chars.Length;
    }

    private static bool SameLetter(char a, char b) => a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    private static int CharScore(char patternChar, string text, int j) =>
        (IsWordStart(text, j) ? WordStart : 0) + (patternChar == text[j] ? SameCase : 0);

    private static bool IsWordStart(string text, int j)
    {
        if (j == 0) return true;
        var previous = text[j - 1];
        if (char.IsWhiteSpace(previous) || char.IsPunctuation(previous) || char.IsSymbol(previous)) return true;
        return char.IsUpper(text[j]) && char.IsLower(previous);
    }
}
