using System;
using System.Collections.Generic;

namespace Tessera.Widgets;

/// <summary>
/// The result of matching a query against a candidate string: whether it matched, a score
/// (higher is better), and the indices of the matched characters (for highlighting).
/// </summary>
public readonly struct FuzzyMatch
{
    public bool Matched { get; }
    public int Score { get; }
    public IReadOnlyList<int> Indices { get; }

    public FuzzyMatch(bool matched, int score, IReadOnlyList<int> indices)
    {
        Matched = matched;
        Score = score;
        Indices = indices;
    }

    public static FuzzyMatch NoMatch { get; } = new(false, 0, []);
}

/// <summary>
/// A lightweight fzf-style fuzzy matcher. A query matches a candidate if the query's characters
/// appear in order (as a subsequence, case-insensitively). The score rewards consecutive runs,
/// matches at word boundaries / camelCase humps / after separators, and matches near the start,
/// and penalizes leading gaps, so results can be ranked. Cheap enough to filter on every keystroke.
/// </summary>
public static class FuzzyMatcher
{
    // Scoring weights, tuned to feel like fzf's default.
    private const int ScoreMatch = 16;
    private const int BonusBoundary = 8;   // match at a word boundary (after separator/space)
    private const int BonusCamel = 7;      // match at a camelCase hump (lower→Upper)
    private const int BonusConsecutive = 8; // adjacent to the previous match
    private const int BonusFirstChar = 8;  // match is the candidate's first char
    private const int PenaltyLeadingGap = 3; // per skipped char before the first match
    private const int PenaltyGap = 1;       // per skipped char between matches

    /// <summary>
    /// Matches <paramref name="query"/> against <paramref name="candidate"/>. An empty query
    /// matches everything with score 0. Returns <see cref="FuzzyMatch.NoMatch"/> if not all
    /// query characters can be found in order. Allocates a list for the matched indices —
    /// prefer <see cref="Score"/> for the filter/rank pass and <see cref="MatchInto"/> for
    /// highlighting only the visible rows.
    /// </summary>
    public static FuzzyMatch Match(string query, string candidate)
    {
        if (string.IsNullOrEmpty(query))
        {
            return new FuzzyMatch(true, 0, []);
        }

        if (string.IsNullOrEmpty(candidate))
        {
            return FuzzyMatch.NoMatch;
        }

        var indices = new List<int>(query.Length);
        bool matched = Core(query, candidate, indices, out int score);
        return matched ? new FuzzyMatch(true, score, indices) : FuzzyMatch.NoMatch;
    }

    /// <summary>
    /// Whether <paramref name="query"/> matches <paramref name="candidate"/>, and the match
    /// score, WITHOUT allocating (no matched-index list). Use this for the filter + rank pass
    /// over a full command set on every keystroke. An empty query matches with score 0.
    /// </summary>
    public static bool Score(string query, string candidate, out int score)
    {
        if (string.IsNullOrEmpty(query)) { score = 0; return true; }
        if (string.IsNullOrEmpty(candidate)) { score = 0; return false; }
        return Core(query, candidate, indices: null, out score);
    }

    /// <summary>
    /// Matches into a caller-provided list (cleared first), avoiding a per-call allocation.
    /// Use this to compute matched indices for the small number of rows actually rendered.
    /// Returns whether it matched; on no match the buffer is left empty.
    /// </summary>
    public static bool MatchInto(string query, string candidate, List<int> indices, out int score)
    {
        indices.Clear();
        if (string.IsNullOrEmpty(query)) { score = 0; return true; }
        if (string.IsNullOrEmpty(candidate)) { score = 0; return false; }
        return Core(query, candidate, indices, out score);
    }

    // Shared scoring pass. Records matched positions into `indices` when non-null.
    private static bool Core(string query, string candidate, List<int>? indices, out int score)
    {
        score = 0;
        int qi = 0;
        int prevMatch = -1;

        for (int ci = 0; ci < candidate.Length && qi < query.Length; ci++)
        {
            char qc = char.ToLowerInvariant(query[qi]);
            char cc = char.ToLowerInvariant(candidate[ci]);
            if (qc != cc)
            {
                continue;
            }

            int cellScore = ScoreMatch;

            if (ci == 0)
            {
                cellScore += BonusFirstChar;
            }
            else
            {
                char prev = candidate[ci - 1];
                if (IsSeparator(prev))
                {
                    cellScore += BonusBoundary;
                }
                else if (char.IsLower(prev) && char.IsUpper(candidate[ci]))
                {
                    cellScore += BonusCamel;
                }
            }

            if (prevMatch >= 0)
            {
                int gap = ci - prevMatch - 1;
                if (gap == 0)
                {
                    cellScore += BonusConsecutive;
                }
                else
                {
                    cellScore -= gap * PenaltyGap;
                }
            }
            else
            {
                cellScore -= ci * PenaltyLeadingGap;
            }

            score += cellScore;
            indices?.Add(ci);
            prevMatch = ci;
            qi++;
        }

        return qi >= query.Length;
    }

    private static bool IsSeparator(char c) =>
        c is ' ' or '_' or '-' or '.' or '/' or '\\' or ':' or '(' or ')';
}
