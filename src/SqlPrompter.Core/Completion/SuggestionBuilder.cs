using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Completion
{
    public enum SuggestionKind
    {
        Keyword,
        Schema,
        Table,
        View,
        TableFunction,
        Column,
        Alias,
        Function,
        DataType,

        /// <summary>CTE, temporary table or table variable defined in the script.</summary>
        ScriptTable,

        /// <summary>Variable or parameter defined in the script.</summary>
        Variable,
    }

    public sealed class Suggestion
    {
        public Suggestion(SuggestionKind kind, string displayText, string insertText, string filterText, string suffix, string description, int group)
        {
            Kind = kind;
            DisplayText = displayText;
            InsertText = insertText;
            FilterText = filterText;
            Suffix = suffix;
            Description = description;
            Group = group;
        }

        public SuggestionKind Kind { get; }

        public string DisplayText { get; }

        public string InsertText { get; }

        /// <summary>Text the list is filtered by while typing.</summary>
        public string FilterText { get; }

        /// <summary>Short suffix on the right of the list, e.g. schema or alias.</summary>
        public string Suffix { get; }

        public string Description { get; }

        /// <summary>Order of the group in the list, lower first.</summary>
        public int Group { get; }

        /// <summary>Sort key: group, then text.</summary>
        public string SortText => Group.ToString("D2") + DisplayText;

        public override string ToString() => Kind + " " + DisplayText + " → " + InsertText;
    }

    public static class SuggestionBuilder
    {
        /// <param name="script">Definitions in the script (CTEs, temporary tables, variables); optional.</param>
        /// <param name="keywordCase">Casing of keywords, built-in functions and data types.</param>
        public static IReadOnlyList<Suggestion> Build(
            CaretContext context,
            IDatabaseMetadata metadata,
            ScriptModel script = null,
            KeywordCase keywordCase = KeywordCase.MatchTyped)
        {
            var suggestions = BuildCore(context, metadata, script ?? ScriptModel.Empty);
            return keywordCase == KeywordCase.MatchTyped ? suggestions : suggestions.Select(s => ForceCase(s, keywordCase)).ToList();
        }

        private static Suggestion ForceCase(Suggestion s, KeywordCase keywordCase)
        {
            if (s.Kind != SuggestionKind.Keyword && s.Kind != SuggestionKind.Function && s.Kind != SuggestionKind.DataType)
            {
                return s;
            }

            string Convert(string text) => keywordCase == KeywordCase.Upper ? text.ToUpperInvariant() : text.ToLowerInvariant();
            return new Suggestion(s.Kind, Convert(s.DisplayText), Convert(s.InsertText), s.FilterText, s.Suffix, s.Description, s.Group);
        }

        private static IReadOnlyList<Suggestion> BuildCore(CaretContext context, IDatabaseMetadata metadata, ScriptModel script)
        {
            switch (context.Kind)
            {
                case ContextKind.Keyword:
                    return KeywordSuggestions(context, 0).ToList();
                case ContextKind.ObjectName:
                    return ObjectSuggestions(context, metadata, script).ToList();
                case ContextKind.Expression:
                    return ExpressionSuggestions(context, metadata, script).ToList();
                case ContextKind.Variable:
                    return VariableSuggestions(context, script, 0).ToList();
                case ContextKind.DataType:
                    return DataTypeSuggestions(context, 0).ToList();
                case ContextKind.Fallback:
                    return KeywordSuggestions(context, 0)
                        .Concat(FunctionSuggestions(context, 2))
                        .Concat(DataTypeSuggestions(context, 3))
                        .ToList();
                default:
                    return Array.Empty<Suggestion>();
            }
        }

        private static IEnumerable<Suggestion> FunctionSuggestions(CaretContext context, int group)
        {
            foreach (var function in Keywords.BuiltInFunctions)
            {
                var text = Keywords.ApplyCase(function, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.Function, text, text, text, null, "built-in function", group);
            }
        }

        private static IEnumerable<Suggestion> DataTypeSuggestions(CaretContext context, int group)
        {
            foreach (var type in Keywords.DataTypes)
            {
                var text = Keywords.ApplyCase(type, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.DataType, text, text, text, null, "data type", group);
            }
        }

        private static IEnumerable<Suggestion> KeywordSuggestions(CaretContext context, int firstGroup)
        {
            var preferred = new HashSet<string>(context.PreferredKeywords, StringComparer.OrdinalIgnoreCase);
            foreach (var keyword in context.PreferredKeywords.Concat(Keywords.All).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var text = Keywords.ApplyCase(keyword, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.Keyword, text, text, text, null, null,
                    preferred.Contains(keyword) ? firstGroup : firstGroup + 1);
            }
        }

        private static IEnumerable<Suggestion> VariableSuggestions(CaretContext context, ScriptModel script, int group)
        {
            foreach (var variable in script.Variables)
            {
                var description = (variable.IsParameter ? "parameter " : "variable ") + variable.Name +
                    (variable.TypeName.Length > 0 ? " " + variable.TypeName : string.Empty);
                yield return new Suggestion(SuggestionKind.Variable, variable.Name, variable.Name, variable.Name,
                    variable.TypeName.Length > 0 ? variable.TypeName : null, description, group);
            }
        }

        private static IEnumerable<Suggestion> ScriptTableSuggestions(CaretContext context, ScriptModel script)
        {
            var quote = context.QuoteIdentifiers;
            foreach (var table in script.Tables.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()))
            {
                var insert = table.Kind == ScriptTableKind.Cte ? SqlIdentifier.Quote(table.Name, quote) : table.Name;
                yield return new Suggestion(SuggestionKind.ScriptTable, table.Name, insert, table.Name, null, table.Description, 0);
            }
        }

        private static IEnumerable<Suggestion> ObjectSuggestions(CaretContext context, IDatabaseMetadata metadata, ScriptModel script)
        {
            if (context.Qualifier.Count == 0)
            {
                foreach (var suggestion in ScriptTableSuggestions(context, script))
                {
                    yield return suggestion;
                }
            }

            if (metadata == null)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            if (context.Qualifier.Count == 1)
            {
                var schema = context.Qualifier[0];
                foreach (var dbObject in metadata.Objects.Where(o => string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return ObjectSuggestion(dbObject, SqlIdentifier.Quote(dbObject.Name, quote), null);
                }

                yield break;
            }

            foreach (var dbObject in metadata.Objects.Where(o => !o.IsSystem))
            {
                var insert = SqlIdentifier.Quote(dbObject.Schema, quote) + "." + SqlIdentifier.Quote(dbObject.Name, quote);
                yield return ObjectSuggestion(dbObject, insert, dbObject.Schema);
            }

            foreach (var schema in metadata.Schemas)
            {
                yield return new Suggestion(SuggestionKind.Schema, schema, SqlIdentifier.Quote(schema, quote), schema, null, "schema", 1);
            }

            foreach (var keyword in context.PreferredKeywords)
            {
                var text = Keywords.ApplyCase(keyword, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.Keyword, text, text, text, null, null, 2);
            }
        }

        private static Suggestion ObjectSuggestion(DbObject dbObject, string insertText, string suffix)
        {
            SuggestionKind kind;
            string description;
            switch (dbObject.Kind)
            {
                case DbObjectKind.View:
                    kind = SuggestionKind.View;
                    description = "view";
                    break;
                case DbObjectKind.TableFunction:
                    kind = SuggestionKind.TableFunction;
                    description = "table-valued function";
                    break;
                default:
                    kind = SuggestionKind.Table;
                    description = "table";
                    break;
            }

            return new Suggestion(kind, dbObject.Name, insertText, dbObject.Name, suffix, description + " " + dbObject, 0);
        }

        /// <summary>Columns of one table source and how to describe their owner.</summary>
        private sealed class SourceColumns
        {
            public SourceColumns(TableSource source, IReadOnlyList<DbColumn> columns, string owner)
            {
                Source = source;
                Columns = columns;
                Owner = owner;
            }

            public TableSource Source { get; }

            public IReadOnlyList<DbColumn> Columns { get; }

            public string Owner { get; }
        }

        /// <summary>Columns of a source defined in the script or found in the database; null if they are not known (yet).</summary>
        private static SourceColumns ResolveColumns(TableSource source, IDatabaseMetadata metadata, ScriptModel script)
        {
            var scripted = script.Resolve(source, metadata);
            if (scripted != null)
            {
                var owner = source.Kind == TableSourceKind.Derived ? "derived table" : string.Join(".", source.NameParts);
                return new SourceColumns(source, scripted.Columns, owner);
            }

            var dbObject = Readiness.Resolve(source, metadata);
            var columns = dbObject != null ? metadata.GetColumns(dbObject) : null;
            return columns == null ? null : new SourceColumns(source, columns, dbObject.ToString());
        }

        private static IEnumerable<Suggestion> ExpressionSuggestions(CaretContext context, IDatabaseMetadata metadata, ScriptModel script)
        {
            var quote = context.QuoteIdentifiers;
            if (context.QualifiedSource != null)
            {
                var qualified = ResolveColumns(context.QualifiedSource, metadata, script);
                foreach (var column in qualified?.Columns ?? Array.Empty<DbColumn>())
                {
                    yield return ColumnSuggestion(column, SqlIdentifier.Quote(column.Name, quote), null, qualified.Owner, 0);
                }

                yield break;
            }

            var resolved = context.Sources
                .Select(s => ResolveColumns(s, metadata, script))
                .Where(x => x != null)
                .ToList();

            // A column whose name exists in several sources of the same block is inserted with its alias.
            var ambiguous = new HashSet<string>(
                resolved.Where(x => x.Source.ScopeLevel == 0)
                    .SelectMany(x => x.Columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                    .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (var x in resolved)
            {
                var reference = x.Source.ReferenceName;
                var inner = x.Source.ScopeLevel == 0;
                foreach (var column in x.Columns)
                {
                    var name = SqlIdentifier.Quote(column.Name, quote);
                    var qualify = !inner || ambiguous.Contains(column.Name);
                    var insert = qualify ? SqlIdentifier.Quote(reference, quote) + "." + name : name;
                    yield return ColumnSuggestion(column, insert, reference, x.Owner, inner ? 0 : 2);
                }
            }

            foreach (var source in context.Sources.Where(s => s.ReferenceName != null)
                         .GroupBy(s => s.ReferenceName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                var target = source.Kind == TableSourceKind.Derived ? "derived table" : string.Join(".", source.NameParts);
                yield return new Suggestion(SuggestionKind.Alias, source.ReferenceName, SqlIdentifier.Quote(source.ReferenceName, quote),
                    source.ReferenceName, source.Alias != null ? target : null, (source.Alias != null ? "alias " : "source ") + target, 1);
            }

            foreach (var variable in VariableSuggestions(context, script, 2))
            {
                yield return variable;
            }

            foreach (var function in FunctionSuggestions(context, 3))
            {
                yield return function;
            }

            foreach (var keyword in KeywordSuggestions(context, 4))
            {
                yield return keyword;
            }
        }

        private static Suggestion ColumnSuggestion(DbColumn column, string insertText, string suffix, string owner, int group)
        {
            var type = column.TypeName.Length > 0 ? " " + column.TypeName + (column.IsNullable ? " null" : " not null") : string.Empty;
            return new Suggestion(SuggestionKind.Column, column.Name, insertText, column.Name, suffix, column.Name + type + " (" + owner + ")", group);
        }
    }
}
