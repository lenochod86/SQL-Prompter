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

        /// <summary>Database on the server, for three-part names.</summary>
        Database,

        /// <summary>CTE, temporary table or table variable defined in the script.</summary>
        ScriptTable,

        /// <summary>Variable or parameter defined in the script.</summary>
        Variable,
    }

    public sealed class Suggestion
    {
        public Suggestion(SuggestionKind kind, string displayText, string insertText, string filterText, string suffix, string description, int group, string fromClause = null, string detail = null)
        {
            Kind = kind;
            DisplayText = displayText;
            InsertText = insertText;
            FilterText = filterText;
            Suffix = suffix;
            Description = description;
            Group = group;
            FromClause = fromClause;
            Detail = detail;
        }

        public SuggestionKind Kind { get; }

        public string DisplayText { get; }

        public string InsertText { get; }

        /// <summary>Text the list is filtered by while typing.</summary>
        public string FilterText { get; }

        /// <summary>Short suffix on the right of the list, e.g. schema or alias.</summary>
        public string Suffix { get; }

        public string Description { get; }

        /// <summary>The suffix with the "SQL Prompter" marker appended, shown on the right of the list.</summary>
        public string MarkedSuffix => string.IsNullOrEmpty(Suffix) ? MarkerText : Suffix + " · " + MarkerText;

        public const string MarkerText = "SQL Prompter";

        /// <summary>Order of the group in the list, lower first.</summary>
        public int Group { get; }

        /// <summary>Text such as "FROM dbo.Users AS u" that choosing the item adds at the end of the select list; null for most items.</summary>
        public string FromClause { get; }

        /// <summary>Short text shown next to the name, e.g. "nvarchar(50) not null" for a column.</summary>
        public string Detail { get; }

        /// <summary>Sort key: group, then text.</summary>
        public string SortText => Group.ToString("D2") + DisplayText;

        public override string ToString() => Kind + " " + DisplayText + " → " + InsertText;
    }

    public static class SuggestionBuilder
    {
        /// <param name="script">Definitions in the script (CTEs, temporary tables, variables); optional.</param>
        /// <param name="options">Casing, aliases and qualification; defaults if null.</param>
        public static IReadOnlyList<Suggestion> Build(
            CaretContext context,
            IDatabaseMetadata metadata,
            ScriptModel script = null,
            SuggestionOptions options = null)
        {
            options = options ?? SuggestionOptions.Default;
            var suggestions = BuildCore(context, metadata, script ?? ScriptModel.Empty, options);
            var keywordCase = options.KeywordCase;
            return keywordCase == KeywordCase.MatchTyped ? suggestions : suggestions.Select(s => ForceCase(s, keywordCase)).ToList();
        }

        private static Suggestion ForceCase(Suggestion s, KeywordCase keywordCase)
        {
            if (s.Kind != SuggestionKind.Keyword && s.Kind != SuggestionKind.Function && s.Kind != SuggestionKind.DataType)
            {
                return s;
            }

            string Convert(string text) => keywordCase == KeywordCase.Upper ? text.ToUpperInvariant() : text.ToLowerInvariant();
            return new Suggestion(s.Kind, Convert(s.DisplayText), Convert(s.InsertText), s.FilterText, s.Suffix, s.Description, s.Group, s.FromClause, s.Detail);
        }

        private static IReadOnlyList<Suggestion> BuildCore(CaretContext context, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
        {
            switch (context.Kind)
            {
                case ContextKind.Keyword:
                    return KeywordSuggestions(context, 0).ToList();
                case ContextKind.ObjectName:
                    return ObjectSuggestions(context, metadata, script, options).ToList();
                case ContextKind.Expression:
                    return ExpressionSuggestions(context, metadata, script, options).ToList();
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

        private static IEnumerable<Suggestion> ScriptTableSuggestions(CaretContext context, ScriptModel script, SuggestionOptions options)
        {
            var quote = context.QuoteIdentifiers;
            foreach (var table in script.Tables.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()))
            {
                var insert = table.Kind == ScriptTableKind.Cte ? SqlIdentifier.Quote(table.Name, quote) : table.Name;
                insert += AliasText(context, options, table.Name);
                yield return new Suggestion(SuggestionKind.ScriptTable, table.Name, insert, table.Name, null, table.Description, 0);
            }
        }

        private static IEnumerable<Suggestion> ObjectSuggestions(CaretContext context, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
        {
            if (context.Qualifier.Count == 0)
            {
                foreach (var suggestion in ScriptTableSuggestions(context, script, options))
                {
                    yield return suggestion;
                }
            }

            if (metadata == null)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            if (context.Qualifier.Count > 1)
            {
                // A name with more parts (server.db.schema.) is not resolved.
                yield break;
            }

            if (context.Qualifier.Count == 1)
            {
                // An empty schema (db..) means the default one.
                var schema = context.Qualifier[0].Length == 0 ? metadata.DefaultSchema : context.Qualifier[0];
                foreach (var dbObject in metadata.Objects.Where(o => string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return ObjectSuggestion(dbObject, SqlIdentifier.Quote(dbObject.Name, quote) + ObjectAlias(context, options, dbObject), null);
                }

                yield break;
            }

            foreach (var dbObject in metadata.Objects.Where(o => !o.IsSystem))
            {
                var insert = (options.Aliases.QualifyObjectNames ? SqlIdentifier.Quote(dbObject.Schema, quote) + "." : string.Empty) +
                    SqlIdentifier.Quote(dbObject.Name, quote) + ObjectAlias(context, options, dbObject);
                yield return ObjectSuggestion(dbObject, insert, dbObject.Schema);
            }

            foreach (var schema in metadata.Schemas)
            {
                yield return new Suggestion(SuggestionKind.Schema, schema, SqlIdentifier.Quote(schema, quote), schema, null, "schema", 1);
            }

            foreach (var database in context.InOtherDatabase ? Array.Empty<string>() : metadata.Databases.ToArray())
            {
                yield return new Suggestion(SuggestionKind.Database, database, SqlIdentifier.Quote(database, quote), database, null, "database", 1);
            }

            foreach (var keyword in context.PreferredKeywords)
            {
                var text = Keywords.ApplyCase(keyword, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.Keyword, text, text, text, null, null, 2);
            }
        }

        /// <summary>The alias after a table or view; table-valued functions need their parameters first, so they get none.</summary>
        private static string ObjectAlias(CaretContext context, SuggestionOptions options, DbObject dbObject) =>
            dbObject.Kind == DbObjectKind.TableFunction ? string.Empty : AliasText(context, options, dbObject.Name);

        /// <summary>" AS alias" or " alias" where a table source is inserted and no alias follows; otherwise empty.</summary>
        private static string AliasText(CaretContext context, SuggestionOptions options, string objectName)
        {
            if (!context.AliasAllowed || !options.Aliases.AddAliases)
            {
                return string.Empty;
            }

            var alias = AliasGenerator.Generate(objectName, context.Sources.Select(s => s.ReferenceName).Where(n => n != null), options.Aliases);
            return AliasDefinition(alias, context, options);
        }

        /// <summary>" AS alias" or " alias" as set in the options.</summary>
        private static string AliasDefinition(string alias, CaretContext context, SuggestionOptions options)
        {
            var quoted = SqlIdentifier.Quote(alias, context.QuoteIdentifiers);
            if (!options.Aliases.IncludeAs)
            {
                return " " + quoted;
            }

            string asKeyword;
            switch (options.KeywordCase)
            {
                case KeywordCase.Lower:
                    asKeyword = "as";
                    break;
                case KeywordCase.Upper:
                    asKeyword = "AS";
                    break;
                default:
                    asKeyword = "AS";
                    break;
            }

            return " " + asKeyword + " " + quoted;
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
                case DbObjectKind.Synonym:
                    kind = SuggestionKind.Table;
                    description = "synonym";
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

            var target = dbObject.SynonymTarget != null ? " → " + string.Join(".", dbObject.SynonymTarget) : string.Empty;
            return new Suggestion(kind, dbObject.Name, insertText, dbObject.Name, suffix, description + " " + dbObject + target, 0);
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

        private static IEnumerable<Suggestion> ExpressionSuggestions(CaretContext context, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
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
                    var qualify = !inner || ambiguous.Contains(column.Name) || options.Aliases.QualifyColumnsWithAliases;
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

            if (context.SelectWithoutFrom)
            {
                foreach (var column in AllColumnSuggestions(context, metadata, options))
                {
                    yield return column;
                }
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

        /// <summary>The list is not built for a prefix shorter than this: all columns of a large database are too many items.</summary>
        private const int AllColumnsMinPrefix = 1;

        private const int AllColumnsLimit = 2000;

        /// <summary>
        /// SELECT without FROM: columns of all tables and views whose name matches the typed text, those starting with it first.
        /// Choosing one inserts it (qualified as the options say) and adds FROM with its table at the end of the select list.
        /// </summary>
        private static IEnumerable<Suggestion> AllColumnSuggestions(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            var prefix = context.TypedPrefix;
            if (metadata == null || prefix.Length < AllColumnsMinPrefix)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            var matches = new List<KeyValuePair<DbObject, DbColumn>>();
            foreach (var dbObject in metadata.Objects.Where(o => !o.IsSystem && (o.Kind == DbObjectKind.Table || o.Kind == DbObjectKind.View)))
            {
                var columns = metadata.GetColumns(dbObject);
                if (columns == null)
                {
                    continue;
                }

                matches.AddRange(columns
                    .Where(c => c.Name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(c => new KeyValuePair<DbObject, DbColumn>(dbObject, c)));
            }

            foreach (var match in matches
                         .OrderBy(m => m.Value.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                         .ThenBy(m => m.Value.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(m => m.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                         .Take(AllColumnsLimit))
            {
                var table = match.Key;
                var alias = options.Aliases.AddAliases ? AliasGenerator.Generate(table.Name, null, options.Aliases) : null;
                var owner = alias ?? table.Name;
                var name = SqlIdentifier.Quote(match.Value.Name, quote);
                var insert = options.Aliases.QualifyColumnsWithAliases ? SqlIdentifier.Quote(owner, quote) + "." + name : name;
                var from = "FROM " + (options.Aliases.QualifyObjectNames ? SqlIdentifier.Quote(table.Schema, quote) + "." : string.Empty) +
                    SqlIdentifier.Quote(table.Name, quote) + (alias != null ? AliasDefinition(alias, context, options) : string.Empty);
                var column = match.Value;
                var type = column.TypeName.Length > 0 ? " " + column.TypeName + (column.IsNullable ? " null" : " not null") : string.Empty;
                yield return new Suggestion(SuggestionKind.Column, column.Name, insert, column.Name, table.Name,
                    column.Name + type + " (" + table + "), adds " + from, 0, from, type.Trim());
            }
        }

        private static Suggestion ColumnSuggestion(DbColumn column, string insertText, string suffix, string owner, int group)
        {
            var type = column.TypeName.Length > 0 ? " " + column.TypeName + (column.IsNullable ? " null" : " not null") : string.Empty;
            return new Suggestion(SuggestionKind.Column, column.Name, insertText, column.Name, suffix, column.Name + type + " (" + owner + ")", group, null, type.Trim());
        }
    }
}
