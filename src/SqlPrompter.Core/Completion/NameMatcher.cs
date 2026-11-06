using System;
using System.Collections.Generic;

namespace SqlPrompter.Core.Completion
{
    /// <summary>How the typed text matches a name; a lower value ranks higher.</summary>
    public enum MatchKind
    {
        Exact,
        Prefix,

        /// <summary>The typed text starts at the beginnings of words (CamelCase humps, after _ - . or a space): <c>BKT</c>, <c>BuKl</c>, <c>kt</c> for <c>Burza_Klient_Typ</c>.</summary>
        Words,

        /// <summary>The typed text is somewhere inside the name.</summary>
        Substring,

        /// <summary>The typed characters appear in the name in this order (<c>bzkl</c> for <c>BurzaKlientTyp</c>), the last resort.</summary>
        Subsequence,
    }

    /// <summary>A match of the typed text: how it matches and which characters of the name it covers (for highlighting).</summary>
    public sealed class NameMatch
    {
        public NameMatch(MatchKind kind, IReadOnlyList<int> positions)
        {
            Kind = kind;
            Positions = positions;
        }

        public MatchKind Kind { get; }

        /// <summary>The indexes of the matched characters in the name, ascending.</summary>
        public IReadOnlyList<int> Positions { get; }

        /// <summary>The matched characters as runs (start, length), neighbouring characters joined.</summary>
        public IReadOnlyList<KeyValuePair<int, int>> Runs()
        {
            var runs = new List<KeyValuePair<int, int>>();
            var i = 0;
            while (i < Positions.Count)
            {
                var start = Positions[i];
                var length = 1;
                while (i + length < Positions.Count && Positions[i + length] == start + length)
                {
                    length++;
                }

                runs.Add(new KeyValuePair<int, int>(start, length));
                i += length;
            }

            return runs;
        }
    }

    /// <summary>
    /// The loose matching of names (#219): besides the prefix and the substring the typed text may pick the humps of a CamelCase name or the words of
    /// a name with separators, or just its characters in order. Case is ignored. A cheap check that the typed characters occur in order comes first,
    /// so that a list of tens of thousands of names is filtered quickly.
    /// </summary>
    public static class NameMatcher
    {
        /// <param name="name">The name to match.</param>
        /// <param name="typed">The typed text.</param>
        /// <param name="loose">Without it only the prefix and the substring match (the behavior before #219).</param>
        public static NameMatch Match(string name, string typed, bool loose = true)
        {
            if (string.IsNullOrEmpty(typed))
            {
                return new NameMatch(MatchKind.Prefix, Array.Empty<int>());
            }

            name = name ?? string.Empty;
            if (name.Length < typed.Length)
            {
                return null;
            }

            if (!loose)
            {
                if (name.Length == typed.Length && string.Equals(name, typed, StringComparison.OrdinalIgnoreCase))
                {
                    return new NameMatch(MatchKind.Exact, Range(0, typed.Length));
                }

                var at = name.IndexOf(typed, StringComparison.OrdinalIgnoreCase);
                return at < 0 ? null : new NameMatch(at == 0 ? MatchKind.Prefix : MatchKind.Substring, Range(at, typed.Length));
            }

            if (!IsSubsequence(name, typed))
            {
                return null;
            }

            if (name.Length == typed.Length && string.Equals(name, typed, StringComparison.OrdinalIgnoreCase))
            {
                return new NameMatch(MatchKind.Exact, Range(0, typed.Length));
            }

            if (name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            {
                return new NameMatch(MatchKind.Prefix, Range(0, typed.Length));
            }

            var words = MatchWords(name, typed);
            if (words != null)
            {
                return new NameMatch(MatchKind.Words, words);
            }

            var index = name.IndexOf(typed, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return new NameMatch(MatchKind.Substring, Range(index, typed.Length));
            }

            return new NameMatch(MatchKind.Subsequence, Subsequence(name, typed));
        }

        private static bool IsSubsequence(string name, string typed)
        {
            var t = 0;
            for (var i = 0; i < name.Length && t < typed.Length; i++)
            {
                if (char.ToUpperInvariant(name[i]) == char.ToUpperInvariant(typed[t]))
                {
                    t++;
                }
            }

            return t == typed.Length;
        }

        private static int[] Subsequence(string name, string typed)
        {
            var result = new int[typed.Length];
            var t = 0;
            for (var i = 0; i < name.Length && t < typed.Length; i++)
            {
                if (char.ToUpperInvariant(name[i]) == char.ToUpperInvariant(typed[t]))
                {
                    result[t++] = i;
                }
            }

            return result;
        }

        private static int[] Range(int start, int length)
        {
            var result = new int[length];
            for (var i = 0; i < length; i++)
            {
                result[i] = start + i;
            }

            return result;
        }

        private static bool IsSeparator(char c) => c == '_' || c == '-' || c == '.' || c == ' ' || c == '[' || c == ']' || c == '@' || c == '#';

        /// <summary>The start of a word: the first character, after a separator, an upper-case letter after a lower-case one or a digit, or the end of an acronym (XMLParser starts a word at P).</summary>
        private static bool IsWordStart(string name, int i)
        {
            if (IsSeparator(name[i]))
            {
                return false;
            }

            if (i == 0 || IsSeparator(name[i - 1]))
            {
                return true;
            }

            var previous = name[i - 1];
            var current = name[i];
            if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
            {
                return true;
            }

            return char.IsUpper(current) && char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1]);
        }

        /// <summary>
        /// The typed text as a sequence of pieces, each piece the start of a word (at least its first character, as many more as match); words may be
        /// skipped. Null when it cannot be matched that way.
        /// </summary>
        private static int[] MatchWords(string name, string typed)
        {
            var positions = new List<int>(typed.Length);
            return Walk(name, typed, 0, 0, positions) ? positions.ToArray() : null;
        }

        private static bool Walk(string name, string typed, int ti, int from, List<int> positions)
        {
            if (ti == typed.Length)
            {
                return true;
            }

            for (var start = from; start < name.Length; start++)
            {
                if (!IsWordStart(name, start) || char.ToUpperInvariant(name[start]) != char.ToUpperInvariant(typed[ti]))
                {
                    continue;
                }

                // As many characters of the word as match, the longest first.
                var length = 1;
                while (start + length < name.Length && ti + length < typed.Length && !IsWordStart(name, start + length) && !IsSeparator(name[start + length]) &&
                       char.ToUpperInvariant(name[start + length]) == char.ToUpperInvariant(typed[ti + length]))
                {
                    length++;
                }

                for (var take = length; take >= 1; take--)
                {
                    var mark = positions.Count;
                    for (var k = 0; k < take; k++)
                    {
                        positions.Add(start + k);
                    }

                    if (Walk(name, typed, ti + take, start + take, positions))
                    {
                        return true;
                    }

                    positions.RemoveRange(mark, positions.Count - mark);
                }
            }

            return false;
        }
    }
}
