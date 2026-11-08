using System;
using System.Collections.Generic;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The settings that influence what suggestions contain.</summary>
    public sealed class SuggestionOptions
    {
        /// <summary>Plain suggestions: no aliases, columns qualified only when ambiguous. The product takes its options from the settings.</summary>
        public static SuggestionOptions Default { get; } = new SuggestionOptions(
            KeywordCase.MatchTyped,
            new AliasSettings { AddAliases = false, QualifyColumnsWithAliases = false });

        public SuggestionOptions(KeywordCase keywordCase, AliasSettings aliases, Func<TableSource, IReadOnlyList<DbColumn>> remoteColumns = null, JoinSettings joins = null, IReadOnlyList<Snippet> snippets = null, InsertSettings inserts = null, EditorLayout editor = null)
        {
            KeywordCase = keywordCase;
            Aliases = aliases ?? new AliasSettings();
            RemoteColumns = remoteColumns;
            Joins = joins ?? new JoinSettings();
            Snippets = snippets ?? new Snippet[0];
            Inserts = inserts ?? new InsertSettings();
            Editor = editor ?? EditorLayout.Default;
        }

        public KeywordCase KeywordCase { get; }

        /// <summary>The case of a keyword the code writes itself (AS, ON, WHERE...): lower when the settings or the formatting style say so, otherwise upper.</summary>
        public string Kw(string upperKeyword)
        {
            var lower = KeywordCase == KeywordCase.Lower || (KeywordCase == KeywordCase.FollowStyle && Editor.Style != null && Editor.Style.KeywordCase == SqlPrompter.Core.Formatting.CaseStyle.Lower);
            return lower ? upperKeyword.ToLowerInvariant() : upperKeyword;
        }
        public AliasSettings Aliases { get; }

        public JoinSettings Joins { get; }

        public InsertSettings Inserts { get; }

        /// <summary>Where the list is shown; used to lay out multi-line insertions.</summary>
        public EditorLayout Editor { get; }

        /// <summary>Snippets offered in the list where a statement or an expression can start.</summary>
        public IReadOnlyList<Snippet> Snippets { get; }

        /// <summary>
        /// Columns of a table on a linked server (a four-part source), or null while they load or if unknown. Must be fast and never wait
        /// for the database; null (the property) when linked servers are not supported by the caller.
        /// </summary>
        public Func<TableSource, IReadOnlyList<DbColumn>> RemoteColumns { get; }

        public SuggestionOptions WithRemoteColumns(Func<TableSource, IReadOnlyList<DbColumn>> remoteColumns) => new SuggestionOptions(KeywordCase, Aliases, remoteColumns, Joins, Snippets, Inserts, Editor);

        public SuggestionOptions WithEditor(EditorLayout editor) => new SuggestionOptions(KeywordCase, Aliases, RemoteColumns, Joins, Snippets, Inserts, editor);

        public SuggestionOptions WithSnippets(IReadOnlyList<Snippet> snippets) => new SuggestionOptions(KeywordCase, Aliases, RemoteColumns, Joins, snippets, Inserts, Editor);
    }
}
