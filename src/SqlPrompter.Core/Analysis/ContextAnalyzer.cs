using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>
    /// Determines what is being typed at the caret. Works with incomplete SQL; positions it does not
    /// understand are reported as Name or Fallback so that only our list is shown.
    /// </summary>
    public static class ContextAnalyzer
    {
        private enum Clause
        {
            None,
            Select,
            From,
            On,
            Where,
            GroupBy,
            Having,
            OrderBy,
            Set,
            Other,
        }

        private static readonly HashSet<string> JoinModifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "LEFT", "RIGHT", "FULL", "INNER", "CROSS", "OUTER",
        };

        public static CaretContext Analyze(string text, int caret)
        {
            if (text == null || caret < 0 || caret > text.Length)
            {
                return CaretContext.None;
            }

            var all = Lexer.Tokenize(text);
            if (all.Any(t => (t.Kind == TokenKind.Comment || t.Kind == TokenKind.String) && Contains(t, caret)))
            {
                return CaretContext.None;
            }

            var tokens = all.Where(t => t.Kind != TokenKind.Comment).ToList();
            var word = tokens.FirstOrDefault(t => t.Start < caret && caret <= t.End);
            if (word != null && word.Kind == TokenKind.Number)
            {
                return CaretContext.None;
            }

            // Variable or temporary table: not known to the extension yet, but the built-in list must not show either.
            var namesUnknownObject = word != null &&
                (word.Kind == TokenKind.Variable || (word.Kind == TokenKind.Word && word.Text.StartsWith("#", StringComparison.Ordinal)));

            if (word != null && !namesUnknownObject && word.Kind != TokenKind.Word && word.Kind != TokenKind.QuotedIdentifier)
            {
                // Caret right after an operator, dot or parenthesis: no word being typed.
                word = null;
            }

            var caretIndex = word != null ? tokens.IndexOf(word) : CountBefore(tokens, caret);

            // Qualifier: a.b.| -> [a, b]
            var qualifier = new List<string>();
            var nameStart = caretIndex;
            while (nameStart >= 2 && tokens[nameStart - 1].Kind == TokenKind.Dot && tokens[nameStart - 2].IsNamePart)
            {
                qualifier.Insert(0, tokens[nameStart - 2].IdentifierValue);
                nameStart -= 2;
            }

            var unknownQualifier = nameStart >= 1 && tokens[nameStart - 1].Kind == TokenKind.Dot; // db..t, @t.column
            var replaceStart = word?.Start ?? caret;
            var replaceLength = word != null ? word.End - word.Start : 0;
            var typed = word != null ? text.Substring(word.Start, caret - word.Start) : string.Empty;
            if (typed.StartsWith("[", StringComparison.Ordinal))
            {
                typed = typed.Substring(1);
            }

            var bounds = StatementSplitter.Split(tokens);
            var statementStart = 0;
            var statementEnd = tokens.Count;
            for (var b = 0; b < bounds.Count; b++)
            {
                if (bounds[b] <= nameStart)
                {
                    statementStart = bounds[b];
                    statementEnd = b + 1 < bounds.Count ? bounds[b + 1] : tokens.Count;
                }
            }

            if (statementEnd <= nameStart)
            {
                statementEnd = tokens.Count;
            }

            CaretContext Make(ContextKind kind) => new CaretContext(kind, replaceStart, replaceLength, typed)
            {
                QuoteIdentifiers = word != null && word.Text.StartsWith("[", StringComparison.Ordinal),
                Qualifier = qualifier,
            };

            if (namesUnknownObject || unknownQualifier)
            {
                return Make(ContextKind.Name);
            }

            var anchorIndex = nameStart - 1;
            if (anchorIndex < statementStart || tokens[anchorIndex].Kind == TokenKind.Semicolon)
            {
                if (qualifier.Count > 0)
                {
                    return Make(ContextKind.Name);
                }

                var start = Make(ContextKind.Keyword);
                start.PreferredKeywords = Keywords.StatementStart;
                return start;
            }

            var structure = new Structure(tokens, statementStart, statementEnd);
            var anchor = tokens[anchorIndex];
            var scopes = structure.ScopesAt(nameStart);
            var block = scopes[0];
            var clauseState = ScanClause(structure, block.Start, nameStart);
            var anchorAtBlockLevel = structure.Depth[anchorIndex] == block.Depth;

            // 1. Object name after FROM, JOIN, UPDATE, INTO …
            var objectKeywords = ObjectNameAnchor(tokens, anchorIndex, statementStart);
            var commaInFrom = anchor.Kind == TokenKind.Comma && anchorAtBlockLevel && clauseState.InFrom && clauseState.Clause == Clause.From;
            if (objectKeywords != null || commaInFrom)
            {
                if (qualifier.Count > 1)
                {
                    // Another database or server: the extension does not know its objects.
                    return Make(ContextKind.Name);
                }

                var objectName = Make(ContextKind.ObjectName);
                objectName.PreferredKeywords = qualifier.Count == 0 ? objectKeywords ?? Array.Empty<string>() : Array.Empty<string>();
                return objectName;
            }

            if (qualifier.Count == 0 &&
                ((anchor.IsWord("AS") && IsCastArgument(structure, anchorIndex)) ||
                 (anchor.Kind == TokenKind.Variable && tokens[statementStart].IsWord("DECLARE"))))
            {
                return Make(ContextKind.DataType);
            }

            if (anchor.IsWord("AS"))
            {
                // Naming an alias.
                return Make(ContextKind.Name);
            }

            var sources = CollectSources(structure, scopes);
            var operandBefore = IsOperand(anchor);

            // 2. After a table source in FROM: an alias or the next clause.
            if (clauseState.Clause == Clause.From && anchorAtBlockLevel && qualifier.Count == 0)
            {
                if (operandBefore || anchor.Kind == TokenKind.CloseParen)
                {
                    var after = Make(ContextKind.Keyword);
                    after.PreferredKeywords = Keywords.AfterTableSource;
                    after.SoftSelection = true;
                    return after;
                }

                if (JoinModifiers.Contains(anchor.Upper ?? string.Empty))
                {
                    var join = Make(ContextKind.Keyword);
                    join.PreferredKeywords = new[] { "JOIN", "OUTER JOIN", "APPLY" };
                    return join;
                }
            }

            // 3. Expression.
            if (!IsExpressionClause(clauseState.Clause))
            {
                if (qualifier.Count > 0)
                {
                    return Make(ContextKind.Name);
                }

                // EXEC, VALUES, SET @x = … and other positions the extension does not understand yet.
                var fallback = Make(ContextKind.Fallback);
                fallback.SoftSelection = true;
                return fallback;
            }

            var expression = Make(ContextKind.Expression);
            expression.Sources = sources;
            expression.SoftSelection = operandBefore;
            if (qualifier.Count > 0)
            {
                var target = FindQualifiedSource(sources, qualifier);
                if (target == null)
                {
                    // Qualifier unknown even as an alias: schema of a function, CTE of an outer statement …
                    return Make(ContextKind.Name);
                }

                expression.QualifiedSource = target;
                expression.SoftSelection = false;
            }

            return expression;
        }

        /// <summary>AS inside CAST(…) or TRY_CAST(…), followed by a data type.</summary>
        private static bool IsCastArgument(Structure s, int asIndex)
        {
            var depth = 0;
            for (var i = asIndex - 1; i >= s.StatementStart; i--)
            {
                var kind = s.Tokens[i].Kind;
                if (kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (kind == TokenKind.OpenParen)
                {
                    if (depth == 0)
                    {
                        return i > s.StatementStart && (s.Tokens[i - 1].IsWord("CAST") || s.Tokens[i - 1].IsWord("TRY_CAST"));
                    }

                    depth--;
                }
            }

            return false;
        }

        private static bool Contains(Token token, int caret)
        {
            if (caret <= token.Start)
            {
                return false;
            }

            return caret < token.End || (caret == token.End && (token.IsUnterminated || token.IsLineComment));
        }

        private static int CountBefore(List<Token> tokens, int caret)
        {
            var count = 0;
            while (count < tokens.Count && tokens[count].End <= caret)
            {
                count++;
            }

            return count;
        }

        /// <summary>A value or identifier that may be followed by an alias or an operator.</summary>
        private static bool IsOperand(Token token)
        {
            switch (token.Kind)
            {
                case TokenKind.QuotedIdentifier:
                case TokenKind.Variable:
                case TokenKind.String:
                case TokenKind.Number:
                case TokenKind.CloseParen:
                    return true;
                case TokenKind.Word:
                    return !Keywords.IsReserved(token.Text) || token.IsWord("NULL");
                default:
                    return false;
            }
        }

        private static bool IsExpressionClause(Clause clause)
        {
            switch (clause)
            {
                case Clause.Select:
                case Clause.On:
                case Clause.Where:
                case Clause.GroupBy:
                case Clause.Having:
                case Clause.OrderBy:
                case Clause.Set:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// If a table name follows the token, returns the keywords that make sense there
        /// (e.g. FROM after DELETE). Otherwise null.
        /// </summary>
        private static IReadOnlyList<string> ObjectNameAnchor(List<Token> tokens, int anchorIndex, int statementStart)
        {
            var anchor = tokens[anchorIndex];
            var prev = anchorIndex > statementStart ? tokens[anchorIndex - 1] : null;
            var prevWord = prev?.Upper;
            switch (anchor.Upper)
            {
                case "FROM":
                case "JOIN":
                case "APPLY":
                case "USING":
                    return Array.Empty<string>();
                case "UPDATE":
                    // Not ON UPDATE CASCADE, FOR UPDATE, AFTER INSERT, UPDATE …
                    var isDml = prev == null || (prev.Kind != TokenKind.Comma && prevWord != "ON" && prevWord != "FOR" &&
                                                 prevWord != "AFTER" && prevWord != "OF");
                    return isDml ? Array.Empty<string>() : null;
                case "INTO":
                    return prevWord == "INSERT" || prevWord == "MERGE" ? Array.Empty<string>() : null;
                case "INSERT":
                    return new[] { "INTO" };
                case "MERGE":
                    return new[] { "INTO" };
                case "DELETE":
                    return anchorIndex == statementStart ? new[] { "FROM" } : null;
                case "TABLE":
                    return prevWord == "TRUNCATE" || prevWord == "DROP" || prevWord == "ALTER" ? Array.Empty<string>() : null;
                default:
                    return null;
            }
        }

        private static TableSource FindQualifiedSource(IReadOnlyList<TableSource> sources, IReadOnlyList<string> qualifier)
        {
            if (qualifier.Count == 1)
            {
                var name = qualifier[0];
                return sources.FirstOrDefault(s => string.Equals(s.Alias, name, StringComparison.OrdinalIgnoreCase))
                    ?? sources.FirstOrDefault(s => s.Alias == null && string.Equals(s.ObjectName, name, StringComparison.OrdinalIgnoreCase));
            }

            if (qualifier.Count == 2)
            {
                return sources.FirstOrDefault(s => s.Alias == null &&
                    string.Equals(s.Schema, qualifier[0], StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.ObjectName, qualifier[1], StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private struct ClauseState
        {
            public Clause Clause;
            public bool InFrom;
        }

        /// <summary>Scans the block from its start to the caret; skips nested parentheses except those containing the caret.</summary>
        private static ClauseState ScanClause(Structure s, int from, int to)
        {
            var state = new ClauseState();
            var sawUpdate = false;
            var i = from;
            while (i < to)
            {
                var token = s.Tokens[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    var close = s.Match[i];
                    i = close >= 0 && close < to ? close + 1 : i + 1;
                    continue;
                }

                switch (token.Upper)
                {
                    case "SELECT":
                        state.Clause = Clause.Select;
                        state.InFrom = false;
                        break;
                    case "FROM":
                        state.Clause = Clause.From;
                        state.InFrom = true;
                        break;
                    case "JOIN":
                    case "APPLY":
                    case "USING":
                        state.Clause = Clause.From;
                        break;
                    case "ON":
                        state.Clause = Clause.On;
                        break;
                    case "WHERE":
                        state.Clause = Clause.Where;
                        state.InFrom = false;
                        break;
                    case "GROUP":
                        state.Clause = Clause.GroupBy;
                        state.InFrom = false;
                        break;
                    case "HAVING":
                        state.Clause = Clause.Having;
                        state.InFrom = false;
                        break;
                    case "ORDER":
                    case "PARTITION":
                        state.Clause = Clause.OrderBy;
                        state.InFrom = false;
                        break;
                    case "UPDATE":
                        sawUpdate = true;
                        state.Clause = Clause.Other;
                        break;
                    case "SET":
                        state.Clause = sawUpdate ? Clause.Set : Clause.Other;
                        break;
                    case "VALUES":
                    case "INTO":
                    case "OUTPUT":
                    case "OPTION":
                    case "FOR":
                    case "INSERT":
                    case "DELETE":
                    case "MERGE":
                    case "DECLARE":
                    case "EXEC":
                    case "EXECUTE":
                        state.Clause = Clause.Other;
                        state.InFrom = false;
                        break;
                }

                i++;
            }

            return state;
        }

        private static List<TableSource> CollectSources(Structure s, IReadOnlyList<Scope> scopes)
        {
            var result = new List<TableSource>();
            for (var level = 0; level < scopes.Count; level++)
            {
                var scope = scopes[level];
                result.AddRange(ParseSources(s, scope.Start, scope.End, scope.Depth, level));
                if (!scope.SeesOuter)
                {
                    break;
                }
            }

            return result;
        }

        private static List<TableSource> ParseSources(Structure s, int from, int to, int depth, int level)
        {
            var sources = new List<TableSource>();
            var tokens = s.Tokens;
            var inFrom = false;
            for (var i = from; i < to; i++)
            {
                var token = tokens[i];
                if (s.Depth[i] != depth)
                {
                    continue;
                }

                if (token.Kind == TokenKind.OpenParen)
                {
                    var close = s.Match[i];
                    if (close < 0 || close >= to)
                    {
                        break;
                    }

                    i = close;
                    continue;
                }

                if (token.Kind == TokenKind.Comma)
                {
                    if (inFrom)
                    {
                        i = ParseReference(s, i + 1, to, depth, level, sources) - 1;
                    }

                    continue;
                }

                var prevWord = i > from ? tokens[i - 1].Upper : null;
                switch (token.Upper)
                {
                    case "FROM":
                        inFrom = true;
                        i = ParseReference(s, i + 1, to, depth, level, sources) - 1;
                        break;
                    case "JOIN":
                    case "APPLY":
                    case "USING":
                        i = ParseReference(s, i + 1, to, depth, level, sources) - 1;
                        break;
                    case "UPDATE":
                        if (prevWord != "ON" && prevWord != "FOR" && prevWord != "AFTER" && prevWord != "OF" &&
                            (i == from || tokens[i - 1].Kind != TokenKind.Comma))
                        {
                            var next = i + 1;
                            if (next < to && tokens[next].IsWord("TOP"))
                            {
                                next++;
                                if (next < to && tokens[next].Kind == TokenKind.OpenParen && s.Match[next] > 0)
                                {
                                    next = s.Match[next] + 1;
                                }
                            }

                            i = ParseReference(s, next, to, depth, level, sources) - 1;
                        }

                        break;
                    case "MERGE":
                        var target = i + 1;
                        if (target < to && tokens[target].IsWord("INTO"))
                        {
                            target++;
                        }

                        i = ParseReference(s, target, to, depth, level, sources) - 1;
                        break;
                    case "WHERE":
                    case "GROUP":
                    case "HAVING":
                    case "ORDER":
                    case "OPTION":
                    case "FOR":
                    case "UNION":
                    case "EXCEPT":
                    case "INTERSECT":
                    case "WINDOW":
                    case "SET":
                        inFrom = false;
                        break;
                }
            }

            // UPDATE u SET … FROM dbo.Users u: the "u" after UPDATE refers to the alias, it is not another source.
            sources.RemoveAll(src => src.Kind == TableSourceKind.Object && src.Alias == null && src.NameParts.Count == 1 &&
                sources.Any(other => other != src && string.Equals(other.Alias, src.ObjectName, StringComparison.OrdinalIgnoreCase)));
            return sources;
        }

        /// <summary>Reads one table source starting at index start and returns the index after it.</summary>
        private static int ParseReference(Structure s, int start, int to, int depth, int level, List<TableSource> sources)
        {
            var tokens = s.Tokens;
            var i = start;
            if (i >= to)
            {
                return i;
            }

            TableSourceKind kind;
            var parts = new List<string>();
            var first = tokens[i];
            if (first.Kind == TokenKind.OpenParen)
            {
                var close = s.Match[i];
                if (close < 0 || close >= to)
                {
                    return to;
                }

                kind = TableSourceKind.Derived;
                i = close + 1;
            }
            else if (first.Kind == TokenKind.Variable)
            {
                kind = TableSourceKind.Variable;
                parts.Add(first.Text);
                i++;
            }
            else if (first.IsIdentifier || (first.Kind == TokenKind.Word && first.Text.StartsWith("#", StringComparison.Ordinal)))
            {
                parts.Add(first.IdentifierValue);
                i++;
                while (i + 1 < to && tokens[i].Kind == TokenKind.Dot)
                {
                    if (tokens[i + 1].Kind == TokenKind.Dot)
                    {
                        parts.Add(string.Empty);
                        i++;
                        continue;
                    }

                    if (!tokens[i + 1].IsNamePart)
                    {
                        break;
                    }

                    parts.Add(tokens[i + 1].IdentifierValue);
                    i += 2;
                }

                if (i < to && tokens[i].Kind == TokenKind.OpenParen)
                {
                    // Table-valued function: dbo.fn(…)
                    var close = s.Match[i];
                    i = close >= 0 && close < to ? close + 1 : to;
                }

                kind = parts[parts.Count - 1].StartsWith("#", StringComparison.Ordinal) ? TableSourceKind.TempTable : TableSourceKind.Object;
            }
            else
            {
                return i;
            }

            string alias = null;
            if (i < to && tokens[i].IsWord("AS"))
            {
                i++;
            }

            if (i < to && ((tokens[i].Kind == TokenKind.Word && !Keywords.CannotBeAlias(tokens[i].Text)) ||
                           tokens[i].Kind == TokenKind.QuotedIdentifier))
            {
                alias = tokens[i].IdentifierValue;
                i++;
            }

            if (i < to && kind == TableSourceKind.Derived && tokens[i].Kind == TokenKind.OpenParen && s.Match[i] > 0)
            {
                // (SELECT …) AS d (a, b)
                i = Math.Min(to, s.Match[i] + 1);
            }

            sources.Add(new TableSource(kind, parts, alias, level));
            return i;
        }

        /// <summary>Token range searched for sources: a query block at one parenthesis level.</summary>
        private sealed class Scope
        {
            public int Start;
            public int End;
            public int Depth;

            /// <summary>The block sees the sources of the enclosing query (subquery in an expression or in APPLY).</summary>
            public bool SeesOuter;
        }

        /// <summary>Parentheses and token depths of one statement.</summary>
        private sealed class Structure
        {
            public Structure(List<Token> tokens, int start, int end)
            {
                Tokens = tokens;
                StatementStart = start;
                StatementEnd = end;
                Depth = new int[tokens.Count];
                Match = Enumerable.Repeat(-1, tokens.Count).ToArray();
                var stack = new Stack<int>();
                for (var i = start; i < end; i++)
                {
                    Depth[i] = stack.Count;
                    if (tokens[i].Kind == TokenKind.OpenParen)
                    {
                        stack.Push(i);
                    }
                    else if (tokens[i].Kind == TokenKind.CloseParen && stack.Count > 0)
                    {
                        var open = stack.Pop();
                        Match[open] = i;
                        Depth[i] = stack.Count;
                    }
                }
            }

            public List<Token> Tokens { get; }

            public int StatementStart { get; }

            public int StatementEnd { get; }

            public int[] Depth { get; }

            /// <summary>For "(" the index of the matching ")", otherwise -1.</summary>
            public int[] Match { get; }

            /// <summary>Blocks containing the position, innermost first.</summary>
            public IReadOnlyList<Scope> ScopesAt(int position)
            {
                var open = new List<int>();
                for (var i = StatementStart; i < position && i < StatementEnd; i++)
                {
                    if (Tokens[i].Kind == TokenKind.OpenParen && (Match[i] < 0 || Match[i] >= position))
                    {
                        open.Add(i);
                    }
                }

                var scopes = new List<Scope>();
                var current = position;
                for (var k = open.Count - 1; k >= 0; k--)
                {
                    var paren = open[k];
                    if (paren + 1 >= current || !IsQueryStart(Tokens[paren + 1]))
                    {
                        continue;
                    }

                    var regionEnd = Match[paren] >= 0 ? Match[paren] : StatementEnd;
                    var scope = BlockAt(paren + 1, regionEnd, Depth[paren] + 1, current);
                    scope.SeesOuter = SeesOuter(paren);
                    scopes.Add(scope);
                    current = paren;
                }

                scopes.Add(BlockAt(StatementStart, StatementEnd, 0, current));
                return scopes;
            }

            private static bool IsQueryStart(Token token) => token.IsWord("SELECT") || token.IsWord("WITH");

            /// <summary>A derived table in FROM and a CTE body do not see the outer query's sources; a subquery in an expression does.</summary>
            private bool SeesOuter(int paren)
            {
                if (paren <= StatementStart)
                {
                    return true;
                }

                var before = Tokens[paren - 1];
                if (before.IsWord("FROM") || before.IsWord("JOIN") || before.IsWord("AS"))
                {
                    return false;
                }

                return !(before.Kind == TokenKind.Comma && Depth[paren - 1] == Depth[paren] && InFromList(paren));
            }

            private bool InFromList(int index)
            {
                for (var i = index - 1; i >= StatementStart; i--)
                {
                    if (Depth[i] != Depth[index])
                    {
                        continue;
                    }

                    var word = Tokens[i].Upper;
                    if (word == "FROM")
                    {
                        return true;
                    }

                    if (word == "SELECT" || word == "WHERE" || word == "SET")
                    {
                        return false;
                    }
                }

                return false;
            }

            /// <summary>Finds the block between UNION/EXCEPT/INTERSECT in [start, end) that contains the position.</summary>
            private Scope BlockAt(int start, int end, int depth, int position)
            {
                var blockStart = start;
                var blockEnd = end;
                for (var i = start; i < end; i++)
                {
                    if (Depth[i] != depth)
                    {
                        continue;
                    }

                    var word = Tokens[i].Upper;
                    if (word != "UNION" && word != "EXCEPT" && word != "INTERSECT")
                    {
                        continue;
                    }

                    if (i < position)
                    {
                        blockStart = i + 1;
                        if (blockStart < end && Tokens[blockStart].IsWord("ALL"))
                        {
                            blockStart++;
                        }
                    }
                    else
                    {
                        blockEnd = i;
                        break;
                    }
                }

                return new Scope { Start = blockStart, End = blockEnd, Depth = depth };
            }
        }
    }
}
