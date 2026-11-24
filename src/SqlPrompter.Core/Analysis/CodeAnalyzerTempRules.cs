using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>The column of a temporary table or table variable of the script that does not exist (#452).</summary>
    public static partial class CodeAnalyzer
    {
        /// <summary>Above this the rule is skipped: it resolves the sources of every reference.</summary>
        private const int MaxTokensForTempColumns = 40000;

        /// <summary>
        /// <c>alias.column</c> where the alias stands for a temporary table or a table variable of the script whose columns are all known (CREATE TABLE,
        /// DECLARE @t TABLE, SELECT ... INTO with a named select list) and the column is not one of them. SSMS does not know such tables, so it does
        /// not underline anything. Nothing is reported when the columns are not complete (a star over an unknown source, dynamic SQL, ALTER TABLE of
        /// a SELECT INTO table) or the table is not created in the script before the use.
        /// </summary>
        private static void FindUnknownTempColumns(string script, List<Token> tokens, IDatabaseMetadata metadata, List<CodeIssue> issues)
        {
            if (tokens.Count > MaxTokensForTempColumns)
            {
                return;
            }

            var names = new HashSet<string>(
                ScriptModel.Build(script, script.Length + 1).Tables.Where(t => t.Kind != ScriptTableKind.Cte).Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0)
            {
                return;
            }

            var bounds = StatementSplitter.Split(tokens);
            for (var b = 0; b < bounds.Count; b++)
            {
                var start = bounds[b];
                var end = b + 1 < bounds.Count ? bounds[b + 1] : tokens.Count;
                if (!tokens.Skip(start).Take(end - start).Any(t => (t.Kind == TokenKind.Word || t.Kind == TokenKind.Variable) && names.Contains(t.Text)))
                {
                    continue;
                }

                // What is defined before this statement: a table created later is not known here.
                var model = ScriptModel.Build(script, tokens[start].Start);
                for (var i = start; i + 2 < end; i++)
                {
                    if (!tokens[i].IsNamePart || tokens[i + 1].Kind != TokenKind.Dot || !tokens[i + 2].IsNamePart ||
                        (i > start && tokens[i - 1].Kind == TokenKind.Dot) ||
                        (i + 3 < end && (tokens[i + 3].Kind == TokenKind.Dot || tokens[i + 3].Kind == TokenKind.OpenParen)))
                    {
                        continue;
                    }

                    var source = TempSourceOf(script, tokens[i]);
                    if (source == null)
                    {
                        continue;
                    }

                    var known = model.Resolve(source, metadata);
                    if (known == null || !known.IsComplete || known.Columns.Count == 0)
                    {
                        continue;
                    }

                    var column = tokens[i + 2].IdentifierValue;
                    if (!known.Columns.Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)))
                    {
                        issues.Add(Issue(CodeIssueKind.UnknownColumn, CodeIssueSeverity.Warning, tokens[i + 2], "Column '" + column + "' does not exist in " + source.ObjectName + "."));
                    }
                }
            }
        }

        /// <summary>The temporary table or table variable that the qualifier stands for at its position, or null when it is something else or ambiguous.</summary>
        private static TableSource TempSourceOf(string script, Token qualifier)
        {
            var name = qualifier.IdentifierValue;
            var matching = ContextAnalyzer.SourcesAt(script, qualifier.Start)
                .Where(s => string.Equals(s.ReferenceName, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // UPDATE p ... FROM #t p: the target is named like the alias; the alias is the source.
            var aliased = matching.Where(s => s.Alias != null).ToList();
            var candidates = aliased.Count > 0 ? aliased : matching;
            if (candidates.Count == 0 || candidates.Any(s => s.Kind != candidates[0].Kind || !string.Equals(s.ObjectName, candidates[0].ObjectName, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var source = candidates[0];
            return source.Kind == TableSourceKind.TempTable || source.Kind == TableSourceKind.Variable ? source : null;
        }
    }
}
