using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Formatting
{
    /// <summary>A token as the formatter sees it: the text to write (cased), what it was and whether white space stood before it.</summary>
    internal sealed class R
    {
        public R(TokenKind kind, string text, string upper, bool spaceBefore)
        {
            Kind = kind;
            Text = text;
            Upper = upper;
            SpaceBefore = spaceBefore;
        }

        public TokenKind Kind { get; }

        public string Text { get; }

        /// <summary>Upper-case text of a word (also of an operator or other token, for comparisons); never null.</summary>
        public string Upper { get; }

        public bool SpaceBefore { get; }

        public bool IsWord(string keyword) => Kind == TokenKind.Word && Upper == keyword;

        public bool IsWord(params string[] keywords) => Kind == TokenKind.Word && Array.IndexOf(keywords, Upper) >= 0;

        public override string ToString() => Text;
    }

    /// <summary>Turns tokens into <see cref="R"/> items: applies the casing, joins the operators that were written as one (<c>&lt;=</c>).</summary>
    internal static class FormatTokens
    {
        private static readonly HashSet<string> FunctionLike = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LEFT", "RIGHT", "COALESCE", "NULLIF", "CONVERT", "TRY_CONVERT", "CAST", "TRY_CAST", "IIF", "CHOOSE", "OPENQUERY", "OPENROWSET", "OPENXML",
            "CONTAINS", "FREETEXT", "OPENDATASOURCE",
        };

        private static readonly HashSet<string> BuiltIn = new HashSet<string>(Keywords.BuiltInFunctions, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> Types = new HashSet<string>(Keywords.DataTypes, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> CompoundOperators = new HashSet<string>(StringComparer.Ordinal)
        {
            "<=", ">=", "<>", "!=", "!<", "!>", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "::",
        };

        /// <summary>The items for the tokens (no comments): cased and with the compound operators joined.</summary>
        public static List<R> Prepare(IReadOnlyList<Token> tokens, FormatStyle style)
        {
            var result = new List<R>(tokens.Count);
            var openers = new Stack<string>();
            var ddl = IsTableDefinition(tokens);
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var space = i > 0 && token.Start > tokens[i - 1].End;
                switch (token.Kind)
                {
                    case TokenKind.OpenParen:
                        openers.Push(i > 0 && tokens[i - 1].Kind == TokenKind.Word ? tokens[i - 1].Upper : string.Empty);
                        result.Add(new R(token.Kind, token.Text, token.Text, space));
                        break;
                    case TokenKind.CloseParen:
                        if (openers.Count > 0)
                        {
                            openers.Pop();
                        }

                        result.Add(new R(token.Kind, token.Text, token.Text, space));
                        break;
                    case TokenKind.Operator:
                        // Operators are single characters in the lexer; <= is "<" followed by "=" without white space.
                        if (!space && result.Count > 0 && result[result.Count - 1].Kind == TokenKind.Operator &&
                            CompoundOperators.Contains(result[result.Count - 1].Text + token.Text))
                        {
                            var previous = result[result.Count - 1];
                            result[result.Count - 1] = new R(TokenKind.Operator, previous.Text + token.Text, previous.Text + token.Text, previous.SpaceBefore);
                        }
                        else
                        {
                            result.Add(new R(token.Kind, token.Text, token.Text, space));
                        }

                        break;
                    case TokenKind.Word:
                        result.Add(new R(token.Kind, Case(tokens, i, openers, style, ddl), token.Upper, space));
                        break;
                    case TokenKind.Variable:
                        result.Add(new R(token.Kind, CaseVariable(token, style), token.Text.ToUpperInvariant(), space));
                        break;
                    default:
                        result.Add(new R(token.Kind, token.Text, token.Text.ToUpperInvariant(), space));
                        break;
                }
            }

            return result;
        }

        /// <summary>The text of every word of the tokens after casing (other tokens keep theirs); used where the layout stays as written.</summary>
        public static string[] CaseAll(IReadOnlyList<Token> tokens, FormatStyle style)
        {
            var cased = new string[tokens.Count];
            var openers = new Stack<string>();
            var ddl = IsTableDefinition(tokens);
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    openers.Push(i > 0 && tokens[i - 1].Kind == TokenKind.Word ? tokens[i - 1].Upper : string.Empty);
                }
                else if (token.Kind == TokenKind.CloseParen && openers.Count > 0)
                {
                    openers.Pop();
                }

                cased[i] = token.Kind == TokenKind.Word ? Case(tokens, i, openers, style, ddl) : token.Kind == TokenKind.Variable ? CaseVariable(token, style) : token.Text;
            }

            return cased;
        }

        private static bool IsTableDefinition(IReadOnlyList<Token> tokens)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].IsWord("TABLE") && (i == 0 || tokens[i - 1].IsWord("CREATE") || tokens[i - 1].IsWord("ALTER") || tokens[i - 1].Kind == TokenKind.Variable))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Case(IReadOnlyList<Token> tokens, int i, Stack<string> openers, FormatStyle style, bool ddl)
        {
            var token = tokens[i];
            var up = token.Upper;
            var previous = i > 0 ? tokens[i - 1] : null;
            var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
            var nextIsParen = next != null && next.Kind == TokenKind.OpenParen;
            if (nextIsParen && (BuiltIn.Contains(up) || FunctionLike.Contains(up) || ContextKeywords.TableFunctions.Contains(up, StringComparer.OrdinalIgnoreCase)))
            {
                return Apply(token.Text, style.FunctionCase);
            }

            if (Types.Contains(up) && !Keywords.IsReserved(up) && IsTypePosition(tokens, i, openers, ddl))
            {
                return Apply(token.Text, style.DataTypeCase);
            }

            if (Keywords.IsReserved(up))
            {
                return Apply(token.Text, style.KeywordCase);
            }

            // Words that are keywords only in a fixed place: BEGIN TRY / END CATCH, CROSS APPLY, PARTITION BY.
            if (((up == "TRY" || up == "CATCH") && previous != null && (previous.IsWord("BEGIN") || previous.IsWord("END"))) ||
                (up == "APPLY" && previous != null && (previous.IsWord("CROSS") || previous.IsWord("OUTER"))) ||
                (up == "PARTITION" && next != null && next.IsWord("BY")))
            {
                return Apply(token.Text, style.KeywordCase);
            }

            // Words that are keywords only in a fixed place and are not reserved: SET options, hints, OUTPUT, TIES (#526).
            if (IsContextKeyword(tokens, i, openers, previous, next))
            {
                return Apply(token.Text, style.KeywordCase);
            }

            return token.Text;
        }

        /// <summary>A global function (<c>@@ROWCOUNT</c>) follows the case of the functions; the variables of the user stay as written.</summary>
        private static string CaseVariable(Token token, FormatStyle style) =>
            ContextKeywords.IsGlobalFunction(token.Text) ? Apply(token.Text, style.FunctionCase) : token.Text;

        private static bool IsContextKeyword(IReadOnlyList<Token> tokens, int i, Stack<string> openers, Token previous, Token next)
        {
            var up = tokens[i].Upper;
            if (previous != null && previous.Kind == TokenKind.Dot || next != null && next.Kind == TokenKind.Dot)
            {
                return false;
            }

            // SET NOCOUNT ON, SET STATISTICS IO ON, SET TRANSACTION ISOLATION LEVEL ...
            if (i > 0 && i <= 4 && tokens[0].IsWord("SET") && tokens[1].Kind == TokenKind.Word && ContextKeywords.IsSetOption(up))
            {
                return true;
            }

            // FROM t WITH (NOLOCK, INDEX (ix)).
            if (openers.Count > 0 && openers.Peek() == "WITH" && ContextKeywords.IsTableHint(up))
            {
                return true;
            }

            // OPTION (RECOMPILE, MAXDOP 1, OPTIMIZE FOR UNKNOWN).
            if (openers.Contains("OPTION") && ContextKeywords.IsQueryHint(up))
            {
                return true;
            }

            switch (up)
            {
                case "TIES":
                    return previous != null && previous.IsWord("WITH");
                case "OUTPUT":
                case "READONLY":
                    return previous == null || previous.Kind != TokenKind.Dot;
                case "OUT":
                    return previous != null && (previous.Kind == TokenKind.Variable || previous.Kind == TokenKind.CloseParen || previous.Kind == TokenKind.Number || Types.Contains(previous.Upper ?? string.Empty));
                default:
                    return false;
            }
        }

        /// <summary>A word like <c>int</c> or <c>date</c> is a type name after a variable, in CAST (… AS type), CONVERT(type …), RETURNS and in a table definition.</summary>
        private static bool IsTypePosition(IReadOnlyList<Token> tokens, int i, Stack<string> openers, bool ddl)
        {
            var previous = i > 0 ? tokens[i - 1] : null;
            var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
            if (previous == null)
            {
                return false;
            }

            if (previous.Kind == TokenKind.Variable || previous.IsWord("RETURNS"))
            {
                return true;
            }

            var opener = openers.Count > 0 ? openers.Peek() : null;
            if (previous.IsWord("AS") && (opener == "CAST" || opener == "TRY_CAST"))
            {
                return true;
            }

            if (previous.Kind == TokenKind.OpenParen && (opener == "CONVERT" || opener == "TRY_CONVERT"))
            {
                return true;
            }

            if ((ddl || opener == "WITH") && previous.IsNamePart && i >= 2 && (tokens[i - 2].Kind == TokenKind.OpenParen || tokens[i - 2].Kind == TokenKind.Comma))
            {
                return true;
            }

            return false;
        }

        public static string Apply(string text, CaseStyle style)
        {
            switch (style)
            {
                case CaseStyle.Upper: return text.ToUpperInvariant();
                case CaseStyle.Lower: return text.ToLowerInvariant();
                default: return text;
            }
        }

        /// <summary>
        /// True when two token sequences are the same script apart from white space and the case of words: every token has the same kind and text
        /// (words ignoring case), comments included. The formatter's safety net.
        /// </summary>
        public static bool Equivalent(IReadOnlyList<Token> before, IReadOnlyList<Token> after)
        {
            if (before.Count != after.Count)
            {
                return false;
            }

            for (var i = 0; i < before.Count; i++)
            {
                var a = before[i];
                var b = after[i];
                if (a.Kind != b.Kind)
                {
                    return false;
                }

                // A global function (@@ROWCOUNT) is cased like a function; the variables of the user are never touched.
                var same = a.Kind == TokenKind.Word || (a.Kind == TokenKind.Variable && ContextKeywords.IsGlobalFunction(a.Text))
                    ? string.Equals(a.Text, b.Text, StringComparison.OrdinalIgnoreCase)
                    : a.Text == b.Text;
                if (!same)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
