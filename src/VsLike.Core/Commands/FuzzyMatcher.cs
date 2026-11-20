namespace VsLike.Core.Commands;

/// <summary>
/// Subsequenz-Matching für die Command Palette. Treffer am Wortanfang und zusammenhängende Treffer zählen mehr.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>Returns a score (higher is better) or null when <paramref name="query"/> is not a subsequence.</summary>
    public static int? Score(string query, string candidate)
    {
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        var score = 0;
        var candidateIndex = 0;
        var previousMatch = -2;
        foreach (var q in query)
        {
            if (char.IsWhiteSpace(q))
            {
                continue;
            }

            var found = -1;
            for (var i = candidateIndex; i < candidate.Length; i++)
            {
                if (char.ToLowerInvariant(candidate[i]) == char.ToLowerInvariant(q))
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            score += 1;
            if (found == previousMatch + 1)
            {
                score += 5;
            }

            if (found == 0 || !char.IsLetterOrDigit(candidate[found - 1]))
            {
                score += 8;
            }

            previousMatch = found;
            candidateIndex = found + 1;
        }

        return score - (candidate.Length / 10);
    }
}
