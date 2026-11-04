using System;
using System.Collections.Generic;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Completion
{
    /// <summary>The settings that influence what suggestions contain.</summary>
    public sealed class SuggestionOptions
    {
        /// <summary>Plain suggestions: no aliases, columns qualified only when ambiguous. The product takes its options from the settings.</summary>
        public static SuggestionOptions Default { get; } = new SuggestionOptions(
            KeywordCase.MatchTyped,
            new AliasSettings { AddAliases = false, QualifyColumnsWithAliases = false });

        public SuggestionOptions(KeywordCase keywordCase, AliasSettings aliases, Func<TableSource, IReadOnlyList<DbColumn>> remoteColumns = null, JoinSettings joins = null)
        {
            KeywordCase = keywordCase;
            Aliases = aliases ?? new AliasSettings();
            RemoteColumns = remoteColumns;
            Joins = joins ?? new JoinSettings();
        }

        public KeywordCase KeywordCase { get; }

        public AliasSettings Aliases { get; }

        public JoinSettings Joins { get; }

        /// <summary>
        /// Columns of a table on a linked server (a four-part source), or null while they load or if unknown. Must be fast and never wait
        /// for the database; null (the property) when linked servers are not supported by the caller.
        /// </summary>
        public Func<TableSource, IReadOnlyList<DbColumn>> RemoteColumns { get; }

        public SuggestionOptions WithRemoteColumns(Func<TableSource, IReadOnlyList<DbColumn>> remoteColumns) => new SuggestionOptions(KeywordCase, Aliases, remoteColumns, Joins);
    }
}
