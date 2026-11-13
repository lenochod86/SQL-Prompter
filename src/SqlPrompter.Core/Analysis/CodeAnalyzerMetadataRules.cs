using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>The rules that need the metadata of the database (#208): an ambiguous column and an implicit conversion of a column and a literal. Silent while the columns are not loaded.</summary>
    public static partial class CodeAnalyzer
    {
        /// <summary>One plain table of the FROM clause of a SELECT with its columns.</summary>
        private sealed class ResolvedSource
        {
            public PlainSource Source;
            public DbObject Object;
            public IReadOnlyList<DbColumn> Columns;
        }

        private static readonly string[] CompareOperators = { "=", "<>", "<", ">", "<=", ">=", "!=" };

        private static void FindWithMetadata(string script, List<Token> tokens, AnalysisSettings settings, IDatabaseMetadata metadata, List<CodeIssue> issues)
        {
            if (metadata == null || (!settings.AmbiguousColumn && !settings.ImplicitConversion && !settings.IsOn(CodeIssueKind.MI003)))
            {
                return;
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("SELECT"))
                {
                    continue;
                }

                var sources = ResolveSources(tokens, i, metadata, out var from, out var end);
                if (sources == null || sources.Count == 0)
                {
                    continue;
                }

                if ((settings.AmbiguousColumn || settings.IsOn(CodeIssueKind.MI003)) && sources.Count > 1)
                {
                    FindAmbiguous(tokens, i, end, sources, issues, settings.AmbiguousColumn, settings.IsOn(CodeIssueKind.MI003));
                }

                if (settings.ImplicitConversion)
                {
                    FindImplicitConversion(script, tokens, from, end, sources, issues);
                }
            }
        }

        /// <summary>
        /// The tables of the FROM clause (with JOINs and commas) of the SELECT at <paramref name="select"/> with their loaded columns; null when there is a source that is
        /// not a plain table, an unknown table or one whose columns are not loaded. <paramref name="end"/> is the end of the SELECT (exclusive).
        /// </summary>
        private static List<ResolvedSource> ResolveSources(List<Token> tokens, int select, IDatabaseMetadata metadata, out int from, out int end)
        {
            from = -1;
            end = tokens.Count;
            var depth = 0;
            for (var j = select + 1; j < tokens.Count; j++)
            {
                var t = tokens[j];
                if (t.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (t.Kind == TokenKind.CloseParen)
                {
                    if (--depth < 0)
                    {
                        end = j;
                        break;
                    }
                }
                else if (depth == 0 && (t.Kind == TokenKind.Semicolon || (t.Kind == TokenKind.Word && (t.IsWord("UNION") || t.IsWord("EXCEPT") || t.IsWord("INTERSECT") || t.IsWord("GO") || EndsABlock.Contains(t.Text)) && j > select + 1)))
                {
                    end = j;
                    break;
                }
                else if (depth == 0 && from < 0 && t.IsWord("FROM"))
                {
                    from = j;
                }
            }

            if (from < 0)
            {
                return null;
            }

            var result = new List<ResolvedSource>();
            var k = from + 1;
            depth = 0;
            while (k < end)
            {
                // A source runs up to the next comma, JOIN-type word, ON or the end of the clause.
                var start = k;
                while (k < end && !(tokens[k].Kind == TokenKind.Comma) && !(tokens[k].Kind == TokenKind.Word && (tokens[k].IsWord("JOIN") || tokens[k].IsWord("ON") || tokens[k].IsWord("INNER") || tokens[k].IsWord("LEFT") ||
                    tokens[k].IsWord("RIGHT") || tokens[k].IsWord("FULL") || tokens[k].IsWord("CROSS") || tokens[k].IsWord("OUTER") || tokens[k].IsWord("WHERE") || tokens[k].IsWord("GROUP") || tokens[k].IsWord("ORDER") ||
                    tokens[k].IsWord("HAVING") || tokens[k].IsWord("OPTION") || tokens[k].IsWord("APPLY"))))
                {
                    if (tokens[k].Kind == TokenKind.OpenParen)
                    {
                        return null;
                    }

                    k++;
                }

                if (k > start)
                {
                    var source = ParseSource(tokens, start, k);
                    if (source == null)
                    {
                        return null;
                    }

                    var dbObject = metadata.FindObject(source.Parts.Count >= 2 ? source.Parts[source.Parts.Count - 2] : null, source.Parts[source.Parts.Count - 1]);
                    var columns = dbObject == null || source.Parts.Count > 2 ? null : metadata.GetColumns(dbObject);
                    if (columns == null || columns.Count == 0)
                    {
                        return null;
                    }

                    result.Add(new ResolvedSource { Source = source, Object = dbObject, Columns = columns });
                }

                if (k >= end)
                {
                    break;
                }

                var stop = tokens[k];
                if (stop.IsWord("WHERE") || stop.IsWord("GROUP") || stop.IsWord("ORDER") || stop.IsWord("HAVING") || stop.IsWord("OPTION"))
                {
                    break;
                }

                if (stop.IsWord("ON"))
                {
                    // The condition: up to the next JOIN or the end of the join clauses.
                    var d = 0;
                    k++;
                    while (k < end)
                    {
                        if (tokens[k].Kind == TokenKind.OpenParen)
                        {
                            d++;
                        }
                        else if (tokens[k].Kind == TokenKind.CloseParen)
                        {
                            d--;
                        }
                        else if (d == 0 && tokens[k].Kind == TokenKind.Word && (tokens[k].IsWord("JOIN") || tokens[k].IsWord("INNER") || tokens[k].IsWord("LEFT") || tokens[k].IsWord("RIGHT") || tokens[k].IsWord("FULL") ||
                            tokens[k].IsWord("CROSS") || tokens[k].IsWord("WHERE") || tokens[k].IsWord("GROUP") || tokens[k].IsWord("ORDER") || tokens[k].IsWord("HAVING") || tokens[k].IsWord("OPTION")))
                        {
                            break;
                        }

                        k++;
                    }

                    continue;
                }

                k++;
                while (k < end && tokens[k].Kind == TokenKind.Word && (tokens[k].IsWord("JOIN") || tokens[k].IsWord("OUTER") || tokens[k].IsWord("INNER") || tokens[k].IsWord("LEFT") || tokens[k].IsWord("RIGHT") || tokens[k].IsWord("FULL") || tokens[k].IsWord("CROSS")))
                {
                    k++;
                }
            }

            return result;
        }

        /// <summary>An unqualified name of a column that two or more tables of the query have: SQL Server refuses it.</summary>
        private static void FindAmbiguous(List<Token> tokens, int select, int end, List<ResolvedSource> sources, List<CodeIssue> issues, bool ambiguous = true, bool unqualified = false)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var depth = 0;
            for (var j = select + 1; j < end; j++)
            {
                if (tokens[j].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[j].Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (tokens[j].IsWord("AS") && j + 1 < end && tokens[j + 1].IsNamePart)
                {
                    aliases.Add(tokens[j + 1].IdentifierValue);
                }
            }

            var skipTo = -1;
            depth = 0;
            for (var j = select + 1; j < end; j++)
            {
                var t = tokens[j];
                if (j < skipTo)
                {
                    continue;
                }

                if (t.Kind == TokenKind.OpenParen)
                {
                    // A nested SELECT has sources of its own.
                    if (j + 1 < end && tokens[j + 1].IsWord("SELECT"))
                    {
                        skipTo = Math.Min(Matching(tokens, j) + 1, end);
                        if (skipTo <= j)
                        {
                            return;
                        }
                    }

                    continue;
                }

                if (!t.IsNamePart || (t.Kind == TokenKind.Word && (Keywords.CannotBeAlias(t.Text) || Keywords.IsReserved(t.Text))))
                {
                    continue;
                }

                var previous = tokens[j - 1];
                var next = j + 1 < tokens.Count ? tokens[j + 1] : null;
                if (previous.Kind == TokenKind.Dot || (next != null && (next.Kind == TokenKind.Dot || next.Kind == TokenKind.OpenParen)) || previous.IsWord("AS") ||
                    previous.IsWord("FROM") || previous.IsWord("JOIN") || previous.IsWord("INTO") || previous.IsWord("TOP") || previous.IsWord("DECLARE"))
                {
                    continue;
                }

                var name = t.IdentifierValue;
                if (aliases.Contains(name))
                {
                    continue;
                }

                var owners = sources.Where(s => s.Columns.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))).ToList();
                if (owners.Count == 0 || IsSourceNameOrAlias(tokens, j, sources) || (owners.Count < 2 && !unqualified) || (owners.Count >= 2 && !ambiguous))
                {
                    continue;
                }

                if (owners.Count == 1)
                {
                    issues.Add(new CodeIssue(CodeIssueKind.MI003, CodeIssueSeverity.Hint, t.Start, t.End - t.Start, "The column " + name + " is not qualified; write " + owners[0].Source.Reference + "." + name + ".",
                        new CodeFix(t.Start, 0, owners[0].Source.Reference + ".")));
                    continue;
                }

                issues.Add(new CodeIssue(CodeIssueKind.AmbiguousColumn, CodeIssueSeverity.Warning, t.Start, t.End - t.Start,
                    "Column " + name + " is in " + string.Join(" and ", owners.Select(o => o.Source.Reference)) + "; qualify it with an alias (" + string.Join(" or ", owners.Select(o => o.Source.Reference + "." + name)) + ")."));
            }
        }

        /// <summary>The name is the table or alias of a source itself (in FROM or JOIN), not a column.</summary>
        private static bool IsSourceNameOrAlias(List<Token> tokens, int index, List<ResolvedSource> sources)
        {
            var text = tokens[index].IdentifierValue;
            return sources.Any(s => string.Equals(s.Source.Reference, text, StringComparison.OrdinalIgnoreCase) && tokens[index].Start >= s.Source.Start && tokens[index].End <= s.Source.End);
        }

        private enum TypeClass
        {
            Other,
            Ansi,
            Unicode,
            Number,
        }

        private static TypeClass ClassOf(string typeName)
        {
            var type = (typeName ?? string.Empty).ToLowerInvariant();
            var cut = type.IndexOf('(');
            if (cut >= 0)
            {
                type = type.Substring(0, cut);
            }

            switch (type)
            {
                case "varchar":
                case "char":
                case "text":
                    return TypeClass.Ansi;
                case "nvarchar":
                case "nchar":
                case "ntext":
                    return TypeClass.Unicode;
                case "int":
                case "bigint":
                case "smallint":
                case "tinyint":
                case "decimal":
                case "numeric":
                case "float":
                case "real":
                case "money":
                case "smallmoney":
                    return TypeClass.Number;
                default:
                    return TypeClass.Other;
            }
        }

        /// <summary>A column compared with a literal of another type family: the column (and its index) or the literal is converted.</summary>
        private static void FindImplicitConversion(string script, List<Token> tokens, int from, int end, List<ResolvedSource> sources, List<CodeIssue> issues)
        {
            for (var j = from; j + 1 < end; j++)
            {
                var op = tokens[j];
                var isComparison = op.Kind == TokenKind.Operator && CompareOperators.Contains(op.Text);
                if (!isComparison)
                {
                    continue;
                }

                // column op literal: the column is the (qualified) name right before the operator.
                var c = j - 1;
                if (c < 0 || !tokens[c].IsNamePart)
                {
                    continue;
                }

                string qualifier = null;
                if (c >= 2 && tokens[c - 1].Kind == TokenKind.Dot && tokens[c - 2].IsNamePart)
                {
                    qualifier = tokens[c - 2].IdentifierValue;
                    if (c >= 4 && tokens[c - 3].Kind == TokenKind.Dot)
                    {
                        continue;
                    }
                }

                var literal = tokens[j + 1];
                if (literal.Kind != TokenKind.String && literal.Kind != TokenKind.Number)
                {
                    continue;
                }

                if (literal.Kind == TokenKind.Number && j + 2 < end && tokens[j + 2].Kind == TokenKind.Operator)
                {
                    continue;
                }

                var name = tokens[c].IdentifierValue;
                var owners = sources.Where(s => (qualifier == null || string.Equals(s.Source.Reference, qualifier, StringComparison.OrdinalIgnoreCase)) &&
                    s.Columns.Any(col => string.Equals(col.Name, name, StringComparison.OrdinalIgnoreCase))).ToList();
                if (owners.Count != 1)
                {
                    continue;
                }

                var column = owners[0].Columns.First(col => string.Equals(col.Name, name, StringComparison.OrdinalIgnoreCase));
                var columnClass = ClassOf(column.TypeName);
                var unicodeLiteral = literal.Kind == TokenKind.String && literal.Text.Length > 0 && (literal.Text[0] == 'N' || literal.Text[0] == 'n');
                var literalClass = literal.Kind == TokenKind.Number ? TypeClass.Number : unicodeLiteral ? TypeClass.Unicode : TypeClass.Ansi;

                if (columnClass == TypeClass.Ansi && literalClass == TypeClass.Unicode)
                {
                    var plain = script.Substring(literal.Start + 1, literal.End - literal.Start - 1);
                    var fix = plain.All(ch => ch < 128) && !literal.IsUnterminated ? new CodeFix(literal.Start, 1, string.Empty) : null;
                    issues.Add(new CodeIssue(CodeIssueKind.ImplicitConversion, CodeIssueSeverity.Warning, literal.Start, literal.End - literal.Start,
                        "The " + column.TypeName + " column " + name + " is converted to nvarchar to compare it with an N'...' literal, so an index on it cannot be used; drop the N.", fix));
                }
                else if (columnClass == TypeClass.Ansi && literalClass == TypeClass.Number)
                {
                    issues.Add(new CodeIssue(CodeIssueKind.ImplicitConversion, CodeIssueSeverity.Warning, literal.Start, literal.End - literal.Start,
                        "The " + column.TypeName + " column " + name + " is converted to a number to compare it with " + literal.Text + ", so an index on it cannot be used and a value that is not a number fails; write it as a string."));
                }
                else if (columnClass == TypeClass.Unicode && literalClass == TypeClass.Number)
                {
                    issues.Add(new CodeIssue(CodeIssueKind.ImplicitConversion, CodeIssueSeverity.Warning, literal.Start, literal.End - literal.Start,
                        "The " + column.TypeName + " column " + name + " is converted to a number to compare it with " + literal.Text + ", so an index on it cannot be used and a value that is not a number fails; write it as a string."));
                }
                else if (columnClass == TypeClass.Number && literal.Kind == TokenKind.String)
                {
                    var inner = script.Substring(literal.Start + (unicodeLiteral ? 2 : 1), Math.Max(0, literal.End - literal.Start - (unicodeLiteral ? 3 : 2)));
                    var fix = inner.Length > 0 && inner.All(ch => char.IsDigit(ch)) && !literal.IsUnterminated ? new CodeFix(literal.Start, literal.End - literal.Start, inner) : null;
                    issues.Add(new CodeIssue(CodeIssueKind.ImplicitConversion, CodeIssueSeverity.Hint, literal.Start, literal.End - literal.Start,
                        "The string " + literal.Text + " is converted to " + column.TypeName + " to compare it with the column " + name + "; write the number.", fix));
                }
            }
        }
    }
}
