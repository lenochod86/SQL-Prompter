using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// Refactorings that work on the text of a script alone (no metadata): they return the changed text, or the same text when there is
    /// nothing to do. They change only the characters they must, so comments, spacing and line breaks stay as they were.
    /// </summary>
    public static class Refactorings
    {
        /// <summary>Words that start a statement (or a batch) when they are written without brackets, so a name like [Open] keeps them.</summary>
        private static readonly HashSet<string> KeepBracketed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GO", "OPEN", "CLOSE", "SAVE", "BREAK", "CONTINUE", "FETCH", "DEALLOCATE", "THROW", "WAITFOR", "PRINT", "RAISERROR", "USE", "MERGE",
            "TRUNCATE", "COMMIT", "ROLLBACK", "GOTO", "RETURN", "GRANT", "DENY", "REVOKE", "WITH", "EXEC", "EXECUTE",
        };

        /// <summary>
        /// Removes square brackets around identifiers that are valid without them. A name that is reserved, has a space or other special
        /// character, starts a statement (<c>[Open]</c>) or would run into the next word stays bracketed.
        /// </summary>
        public static string RemoveBrackets(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var edits = new List<Edit>();
            foreach (var token in Lexer.Tokenize(text))
            {
                if (token.Kind != TokenKind.QuotedIdentifier || token.IsUnterminated || token.Text[0] != '[')
                {
                    continue;
                }

                var value = token.IdentifierValue;
                if (SqlIdentifier.NeedsQuoting(value) || KeepBracketed.Contains(value) || value.IndexOf('$') >= 0)
                {
                    continue;
                }

                if ((token.Start > 0 && IsGlue(text[token.Start - 1])) || (token.End < text.Length && IsGlue(text[token.End])))
                {
                    continue;
                }

                edits.Add(new Edit(token.Start, token.End - token.Start, value));
            }

            return Apply(text, edits);
        }

        /// <summary>
        /// Ends every statement with a semicolon, put right after its last token (before a trailing comment). Left alone are statements that
        /// already end with one, <c>GO</c>, the heads of <c>IF</c>, <c>ELSE</c>, <c>WHILE</c>, <c>BEGIN</c>, <c>BEGIN TRY</c>, <c>END TRY</c>, the
        /// header of a procedure or function (up to <c>AS</c>), a statement that is followed by <c>ELSE</c> and a statement that looks unfinished.
        /// </summary>
        public static string InsertSemicolons(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            if (tokens.Count == 0)
            {
                return text;
            }

            var bounds = StatementSplitter.Split(tokens);
            var edits = new List<Edit>();
            for (var s = 0; s < bounds.Count; s++)
            {
                var first = bounds[s];
                var last = (s + 1 < bounds.Count ? bounds[s + 1] : tokens.Count) - 1;
                var next = s + 1 < bounds.Count ? tokens[bounds[s + 1]] : null;
                if (NeedsSemicolon(tokens, first, last, next))
                {
                    edits.Add(new Edit(tokens[last].End, 0, ";"));
                }
            }

            return Apply(text, edits);
        }

        /// <summary>
        /// Removes the variables that are declared with DECLARE but never mentioned again: the whole statement when it declares only that
        /// one, otherwise the variable and its comma. Procedure parameters are not touched.
        /// </summary>
        public static string RemoveUnusedVariables(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var settings = new AnalysisSettings
            {
                Enabled = true, EqualsNull = false, JoinWithoutOn = false, InsertWithoutColumns = false, UnusedVariable = true, SelectStar = false, LockHint = false,
            };
            var unused = CodeAnalyzer.Analyze(text, settings).Where(i => i.Kind == CodeIssueKind.UnusedVariable).Select(i => i.Start).ToList();
            if (unused.Count == 0)
            {
                return text;
            }

            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
            var edits = new List<Edit>();
            for (var d = 0; d < tokens.Count; d++)
            {
                if (!tokens[d].IsWord("DECLARE"))
                {
                    continue;
                }

                var declarators = Declarators(tokens, d, out var end);
                if (declarators.Count == 0 || !declarators.Any(x => unused.Contains(tokens[x.Name].Start)))
                {
                    continue;
                }

                var keep = declarators.Where(x => !unused.Contains(tokens[x.Name].Start)).ToList();
                if (keep.Count == 0)
                {
                    var from = tokens[d].Start;
                    var to = tokens[end].End;
                    if (end + 1 < tokens.Count && tokens[end + 1].Kind == TokenKind.Semicolon && tokens[end + 1].Start == to)
                    {
                        to = tokens[end + 1].End;
                    }

                    edits.Add(WholeLines(text, from, to));
                }
                else
                {
                    // Rewrite the declaration list: the kept declarators in their own text, joined as they were.
                    var from = tokens[declarators[0].Name].Start;
                    var to = tokens[declarators[declarators.Count - 1].Last].End;
                    var parts = keep.Select(x => text.Substring(tokens[x.Name].Start, tokens[x.Last].End - tokens[x.Name].Start));
                    edits.Add(new Edit(from, to - from, string.Join(", ", parts)));
                }

            }

            return Apply(text, edits);
        }

        private static bool NeedsSemicolon(List<Token> tokens, int first, int last, Token next)
        {
            var head = tokens[first];
            var tail = tokens[last];
            if (tail.Kind == TokenKind.Semicolon || head.Kind == TokenKind.Semicolon)
            {
                return false;
            }

            var word = head.Upper;
            if (word == "GO" || word == "IF" || word == "ELSE" || word == "WHILE")
            {
                return false;
            }

            var second = first + 1 <= last ? tokens[first + 1].Upper : null;
            if (word == "BEGIN" && second != "TRAN" && second != "TRANSACTION" && second != "DISTRIBUTED" && second != "DIALOG" && second != "CONVERSATION")
            {
                return false;
            }

            if (word == "END" && second == "TRY")
            {
                return false;
            }

            if (next != null && next.IsWord("ELSE"))
            {
                return false;
            }

            if (tail.IsWord("AS") && (word == "CREATE" || word == "ALTER"))
            {
                return false;
            }

            if (tail.Kind == TokenKind.Comma || tail.Kind == TokenKind.OpenParen || tail.Kind == TokenKind.Dot ||
                (tail.Kind == TokenKind.Operator && tail.Text != "*"))
            {
                return false;
            }

            // A statement that does not begin with a keyword is a label or something we do not understand.
            return word != null;
        }

        private static List<Declarator> Declarators(List<Token> tokens, int declare, out int end)
        {
            var result = new List<Declarator>();
            var depth = 0;
            var i = declare + 1;
            Declarator current = null;
            end = declare;
            for (; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (token.Kind == TokenKind.CloseParen)
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (depth == 0 && (token.Kind == TokenKind.Semicolon || (token.Kind == TokenKind.Word && IsStatementWord(token.Upper) && i > declare + 1)))
                {
                    break;
                }

                if (depth == 0 && token.Kind == TokenKind.Comma)
                {
                    current = null;
                }
                else if (current == null && token.Kind == TokenKind.Variable && depth == 0)
                {
                    current = new Declarator { Name = i };
                    result.Add(current);
                }

                if (current != null)
                {
                    current.Last = i;
                }

                end = i;
            }

            return result;
        }

        private static bool IsStatementWord(string word) =>
            word == "SELECT" || word == "INSERT" || word == "UPDATE" || word == "DELETE" || word == "SET" || word == "EXEC" || word == "EXECUTE" ||
            word == "IF" || word == "WHILE" || word == "BEGIN" || word == "END" || word == "PRINT" || word == "WITH" || word == "MERGE" ||
            word == "RETURN" || word == "USE" || word == "DECLARE" || word == "GO" || word == "ELSE" || word == "CREATE" || word == "DROP" ||
            word == "ALTER" || word == "TRUNCATE" || word == "COMMIT" || word == "ROLLBACK";

        /// <summary>Widens a removed span to the whole lines when nothing else is on them, so no empty line is left behind.</summary>
        private static Edit WholeLines(string text, int from, int to)
        {
            var lineStart = from;
            while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
            {
                lineStart--;
            }

            var lineEnd = to;
            while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r')
            {
                lineEnd++;
            }

            if (text.Substring(lineStart, from - lineStart).Trim().Length == 0 && text.Substring(to, lineEnd - to).Trim().Length == 0)
            {
                if (lineEnd < text.Length)
                {
                    lineEnd += text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? 2 : 1;
                }

                return new Edit(lineStart, lineEnd - lineStart, string.Empty);
            }

            return new Edit(from, to - from, string.Empty);
        }

        private static bool IsGlue(char c) => Lexer.IsWordChar(c) || c == '@' || c == '#' || c == '$';

        private static string Apply(string text, List<Edit> edits)
        {
            if (edits.Count == 0)
            {
                return text;
            }

            var result = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.Start))
            {
                result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
            }

            return result.ToString();
        }

        private sealed class Declarator
        {
            public int Name;

            public int Last;
        }

        private sealed class Edit
        {
            public Edit(int start, int length, string replacement)
            {
                Start = start;
                Length = length;
                Replacement = replacement;
            }

            public int Start { get; }

            public int Length { get; }

            public string Replacement { get; }
        }
    }
}
