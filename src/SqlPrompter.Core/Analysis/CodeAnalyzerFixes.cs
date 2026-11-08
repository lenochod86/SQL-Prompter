using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>The repairs of TOP without ORDER BY, NOT IN (subquery) and the old-style join (#208). A repair is made only for the plain shapes it understands; otherwise the issue has the message only.</summary>
    public static partial class CodeAnalyzer
    {
        /// <summary>A source of a FROM clause: <c>[schema.]table [[AS] alias]</c>; null for anything else (a subquery, a function, a hint).</summary>
        private sealed class PlainSource
        {
            public List<string> Parts = new List<string>();
            public string Alias;
            public int Start;
            public int End;

            /// <summary>The name a column of it is qualified with.</summary>
            public string Reference => Alias ?? Parts[Parts.Count - 1];
        }

        private static PlainSource ParseSource(List<Token> tokens, int from, int to)
        {
            if (from >= to || !tokens[from].IsNamePart)
            {
                return null;
            }

            var source = new PlainSource { Start = tokens[from].Start };
            var i = from;
            while (i < to)
            {
                if (!tokens[i].IsNamePart)
                {
                    return null;
                }

                source.Parts.Add(tokens[i].IdentifierValue);
                i++;
                if (i < to && tokens[i].Kind == TokenKind.Dot)
                {
                    i++;
                    continue;
                }

                break;
            }

            if (i < to && tokens[i].IsWord("AS"))
            {
                i++;
                if (i >= to || !tokens[i].IsNamePart)
                {
                    return null;
                }

                source.Alias = tokens[i].IdentifierValue;
                i++;
            }
            else if (i < to && tokens[i].IsIdentifier)
            {
                source.Alias = tokens[i].IdentifierValue;
                i++;
            }

            if (i != to)
            {
                return null;
            }

            source.End = tokens[to - 1].End;
            return source;
        }

        private static string Quoted(string name) => "[" + name.Replace("]", "]]") + "]";

        // ---- TOP without ORDER BY: ORDER BY the primary key (needs the metadata)

        private static CodeFix TopFix(List<Token> tokens, int from, int blockEnd, IDatabaseMetadata metadata)
        {
            if (metadata == null || blockEnd <= from)
            {
                return null;
            }

            var depth = 0;
            var fromIndex = -1;
            var sourceEnd = blockEnd;
            for (var j = from; j < blockEnd; j++)
            {
                var t = tokens[j];
                if (t.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (t.Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (depth == 0 && t.Kind == TokenKind.Word)
                {
                    if (t.IsWord("DISTINCT") || t.IsWord("GROUP") || t.IsWord("HAVING") || t.IsWord("INTO") || t.IsWord("JOIN") || t.IsWord("APPLY"))
                    {
                        return null;
                    }

                    if (fromIndex < 0 && t.IsWord("FROM"))
                    {
                        fromIndex = j;
                    }
                    else if (fromIndex >= 0 && t.IsWord("WHERE"))
                    {
                        sourceEnd = j;
                        break;
                    }
                }
                else if (depth == 0 && t.Kind == TokenKind.Comma && fromIndex >= 0)
                {
                    return null;
                }
            }

            if (fromIndex < 0 || depth != 0)
            {
                return null;
            }

            var source = ParseSource(tokens, fromIndex + 1, sourceEnd);
            if (source == null)
            {
                return null;
            }

            var obj = metadata.FindObject(source.Parts.Count >= 2 ? source.Parts[source.Parts.Count - 2] : null, source.Parts[source.Parts.Count - 1]);
            var key = obj == null ? null : metadata.GetPrimaryKey(obj);
            if (key == null || key.Count == 0)
            {
                return null;
            }

            var prefix = source.Alias != null ? Quoted(source.Alias) + "." : string.Empty;
            return new CodeFix(tokens[blockEnd - 1].End, 0, " ORDER BY " + string.Join(", ", key.Select(k => prefix + Quoted(k))));
        }

        // ---- NOT IN (SELECT col FROM t [WHERE ...]) -> NOT EXISTS (SELECT 1 FROM t WHERE ... AND col = outer)

        private static CodeFix NotInFix(string script, List<Token> tokens, int notIndex)
        {
            // The left operand: a plain (qualified) column right before NOT IN.
            var leftEnd = notIndex - 1;
            if (leftEnd < 0 || !tokens[leftEnd].IsNamePart)
            {
                return null;
            }

            var leftStart = leftEnd;
            while (leftStart - 2 >= 0 && tokens[leftStart - 1].Kind == TokenKind.Dot && tokens[leftStart - 2].IsNamePart)
            {
                leftStart -= 2;
            }

            // An unqualified outer column would be taken for a column of the subquery (its own scope wins), so only a qualified one is rewritten.
            if (leftStart == leftEnd)
            {
                return null;
            }

            if (leftStart > 0 && (tokens[leftStart - 1].Kind == TokenKind.Dot || tokens[leftStart - 1].Kind == TokenKind.Operator || tokens[leftStart - 1].Kind == TokenKind.Variable))
            {
                return null;
            }

            var open = notIndex + 2;
            var close = Matching(tokens, open);
            if (close < 0)
            {
                return null;
            }

            // SELECT [DISTINCT] column FROM source [WHERE condition]
            var i = open + 2; // after SELECT
            if (i < close && tokens[i].IsWord("DISTINCT"))
            {
                i++;
            }

            var columnStart = i;
            while (i < close && !tokens[i].IsWord("FROM") && (tokens[i].IsNamePart || tokens[i].Kind == TokenKind.Dot))
            {
                i++;
            }

            if (i >= close || i == columnStart || !tokens[i].IsWord("FROM"))
            {
                return null;
            }

            var column = tokens.GetRange(columnStart, i - columnStart);
            if (column.Count % 2 == 0 || column.Where((t, k) => (k % 2 == 1) != (t.Kind == TokenKind.Dot)).Any())
            {
                return null;
            }

            var fromIndex = i;
            var whereIndex = -1;
            var depth = 0;
            for (var j = fromIndex + 1; j < close; j++)
            {
                var t = tokens[j];
                if (t.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (t.Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (depth == 0 && t.Kind == TokenKind.Word)
                {
                    if (t.IsWord("WHERE") && whereIndex < 0)
                    {
                        whereIndex = j;
                    }
                    else if (t.IsWord("GROUP") || t.IsWord("HAVING") || t.IsWord("ORDER") || t.IsWord("UNION") || t.IsWord("EXCEPT") || t.IsWord("INTERSECT") || t.IsWord("TOP") ||
                             t.IsWord("OPTION") || (whereIndex < 0 && (t.IsWord("JOIN") || t.IsWord("APPLY"))))
                    {
                        return null;
                    }
                }
                else if (depth == 0 && t.Kind == TokenKind.Comma && whereIndex < 0)
                {
                    return null;
                }
            }

            if (depth != 0)
            {
                return null;
            }

            var sourceEnd = whereIndex >= 0 ? whereIndex : close;
            var source = ParseSource(tokens, fromIndex + 1, sourceEnd);
            if (source == null)
            {
                return null;
            }

            var inner = column.Count == 1 ? Quoted(column[0].IdentifierValue) : null;
            string innerText;
            if (column.Count == 1)
            {
                // An unqualified column of the subquery belongs to its table: say so, or it would compare with itself.
                innerText = Quoted(source.Reference) + "." + inner;
            }
            else
            {
                innerText = script.Substring(column[0].Start, column[column.Count - 1].End - column[0].Start);
            }

            var left = script.Substring(tokens[leftStart].Start, tokens[leftEnd].End - tokens[leftStart].Start);
            var from = script.Substring(tokens[fromIndex].Start, tokens[sourceEnd - 1].End - tokens[fromIndex].Start);
            var condition = innerText + " = " + left;
            if (whereIndex >= 0 && whereIndex + 1 < close)
            {
                var where = script.Substring(tokens[whereIndex + 1].Start, tokens[close - 1].End - tokens[whereIndex + 1].Start);
                var hasOr = false;
                var d = 0;
                for (var j = whereIndex + 1; j < close; j++)
                {
                    if (tokens[j].Kind == TokenKind.OpenParen)
                    {
                        d++;
                    }
                    else if (tokens[j].Kind == TokenKind.CloseParen)
                    {
                        d--;
                    }
                    else if (d == 0 && tokens[j].IsWord("OR"))
                    {
                        hasOr = true;
                    }
                }

                condition = (hasOr ? "(" + where + ")" : where) + " AND " + condition;
            }

            var text = "NOT EXISTS (SELECT 1 " + from + " WHERE " + condition + ")";
            return new CodeFix(tokens[leftStart].Start, tokens[close].End - tokens[leftStart].Start, text);
        }

        // ---- FROM a, b WHERE a.x = b.x AND ... -> FROM a JOIN b ON a.x = b.x WHERE ...

        private static readonly HashSet<string> EndsWhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "OPTION", "FOR", "SELECT", "INSERT", "UPDATE", "DELETE", "WITH", "GO", "DECLARE", "SET", "IF", "WHILE", "EXEC", "EXECUTE", "PRINT",
        };

        private sealed class Conjunct
        {
            public int From;
            public int To;
            public string Left;
            public string Right;
            public string Text;
        }

        private static CodeFix OldJoinFix(string script, List<Token> tokens, int commaIndex)
        {
            // The FROM that owns the comma.
            var fromIndex = -1;
            var depth = 0;
            for (var i = commaIndex - 1; i >= 0; i--)
            {
                if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    depth--;
                    if (depth < 0)
                    {
                        return null;
                    }
                }
                else if (depth == 0 && tokens[i].IsWord("FROM"))
                {
                    fromIndex = i;
                    break;
                }
            }

            if (fromIndex < 0)
            {
                return null;
            }

            // The sources, split by the commas at the level of the FROM.
            var sources = new List<PlainSource>();
            var start = fromIndex + 1;
            var j = start;
            depth = 0;
            var whereIndex = -1;
            for (; j <= tokens.Count; j++)
            {
                var atEnd = j == tokens.Count;
                var t = atEnd ? null : tokens[j];
                if (!atEnd && t.Kind == TokenKind.OpenParen)
                {
                    return null;
                }

                var ends = atEnd || t.Kind == TokenKind.CloseParen || t.Kind == TokenKind.Semicolon || (t.Kind == TokenKind.Word && (EndsFrom.Contains(t.Text) || EndsWhere.Contains(t.Text)));
                if (atEnd || t.Kind == TokenKind.Comma || ends)
                {
                    var source = ParseSource(tokens, start, j);
                    if (source == null)
                    {
                        return null;
                    }

                    sources.Add(source);
                    start = j + 1;
                    if (atEnd || t.Kind != TokenKind.Comma)
                    {
                        if (!atEnd && t.IsWord("WHERE"))
                        {
                            whereIndex = j;
                        }
                        else if (!atEnd && !t.IsWord("WHERE") && !(t.Kind == TokenKind.Semicolon || t.Kind == TokenKind.CloseParen || EndsWhere.Contains(t.Text ?? string.Empty)))
                        {
                            return null;
                        }

                        break;
                    }
                }
            }

            if (sources.Count < 2 || sources.Select(s => s.Reference.ToUpperInvariant()).Distinct().Count() != sources.Count)
            {
                return null;
            }

            // The conditions of the WHERE clause, split at the AND of the top level.
            var conjuncts = new List<Conjunct>();
            var endOfAll = sources[sources.Count - 1].End;
            if (whereIndex >= 0)
            {
                var k = whereIndex + 1;
                depth = 0;
                var from = k;
                var between = false;
                for (; k <= tokens.Count; k++)
                {
                    var atEnd = k == tokens.Count;
                    var t = atEnd ? null : tokens[k];
                    var ends = atEnd || t.Kind == TokenKind.Semicolon || (depth == 0 && t.Kind == TokenKind.CloseParen) || (depth == 0 && t.Kind == TokenKind.Word && EndsWhere.Contains(t.Text));
                    if (!atEnd && t.Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (!atEnd && t.Kind == TokenKind.CloseParen && depth > 0)
                    {
                        depth--;
                    }

                    if (!atEnd && depth == 0 && t.IsWord("OR"))
                    {
                        return null;
                    }

                    if (!atEnd && depth == 0 && t.IsWord("BETWEEN"))
                    {
                        between = true;
                    }

                    var isAnd = !atEnd && depth == 0 && t.IsWord("AND");
                    if (isAnd && between)
                    {
                        between = false;
                        isAnd = false;
                    }

                    if (ends || isAnd)
                    {
                        if (k == from)
                        {
                            return null;
                        }

                        conjuncts.Add(MakeConjunct(script, tokens, from, k));
                        endOfAll = tokens[k - 1].End;
                        from = k + 1;
                        if (ends)
                        {
                            break;
                        }
                    }
                }
            }

            var references = sources.Select(s => s.Reference).ToList();
            int IndexOf(string reference) => references.FindIndex(r => string.Equals(r, reference, StringComparison.OrdinalIgnoreCase));
            var used = new HashSet<Conjunct>();
            var text = new StringBuilder();
            text.Append(SourceText(script, sources[0]));
            for (var s = 1; s < sources.Count; s++)
            {
                var on = new List<Conjunct>();
                foreach (var c in conjuncts)
                {
                    if (c.Left == null || used.Contains(c))
                    {
                        continue;
                    }

                    var a = IndexOf(c.Left);
                    var b = IndexOf(c.Right);
                    if (a >= 0 && b >= 0 && a != b && Math.Max(a, b) == s)
                    {
                        on.Add(c);
                        used.Add(c);
                    }
                }

                text.Append(on.Count == 0 ? " CROSS JOIN " : " JOIN ").Append(SourceText(script, sources[s]));
                if (on.Count > 0)
                {
                    text.Append(" ON ").Append(string.Join(" AND ", on.Select(c => c.Text)));
                }
            }

            var rest = conjuncts.Where(c => !used.Contains(c)).Select(c => c.Text).ToList();
            if (rest.Count > 0)
            {
                text.Append(" WHERE ").Append(string.Join(" AND ", rest));
            }

            var firstStart = sources[0].Start;
            return new CodeFix(firstStart, endOfAll - firstStart, text.ToString());
        }

        private static string SourceText(string script, PlainSource source) => script.Substring(source.Start, source.End - source.Start);

        /// <summary>One condition; when it is <c>q1.c1 = q2.c2</c> the two qualifiers are kept to see whether it joins two sources.</summary>
        private static Conjunct MakeConjunct(string script, List<Token> tokens, int from, int to)
        {
            var conjunct = new Conjunct { From = from, To = to, Text = script.Substring(tokens[from].Start, tokens[to - 1].End - tokens[from].Start) };
            if (to - from == 7 && tokens[from].IsNamePart && tokens[from + 1].Kind == TokenKind.Dot && tokens[from + 2].IsNamePart && tokens[from + 3].Kind == TokenKind.Operator && tokens[from + 3].Text == "=" &&
                tokens[from + 4].IsNamePart && tokens[from + 5].Kind == TokenKind.Dot && tokens[from + 6].IsNamePart)
            {
                conjunct.Left = tokens[from].IdentifierValue;
                conjunct.Right = tokens[from + 4].IdentifierValue;
            }

            return conjunct;
        }
    }
}
