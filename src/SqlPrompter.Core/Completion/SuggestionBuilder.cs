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

        /// <summary>A scalar function of the database, inserted with its parentheses.</summary>
        ScalarFunction,
        DataType,

        /// <summary>A snippet; <see cref="Suggestion.Tag"/> holds it and the editor expands it.</summary>
        Snippet,

        /// <summary>A whole join condition such as o.IdUser = u.IdUser, offered after ON.</summary>
        JoinCondition,

        /// <summary>Linked server, for four-part names.</summary>
        LinkedServer,

        /// <summary>Database on the server, for three-part names.</summary>
        Database,

        /// <summary>CTE, temporary table or table variable defined in the script.</summary>
        ScriptTable,

        /// <summary>Variable or parameter defined in the script.</summary>
        Variable,

        /// <summary>A stored procedure; choosing it inserts its parameters.</summary>
        Procedure,

        /// <summary>An index name after ALTER INDEX or DROP INDEX.</summary>
        Index,
    }

    public enum KeyKind
    {
        None,
        Primary,
        Foreign,
    }

    public sealed class Suggestion
    {
        public Suggestion(SuggestionKind kind, string displayText, string insertText, string filterText, string suffix, string description, int group, string fromClause = null, string detail = null, KeyKind key = KeyKind.None, object tag = null, int caretBack = 0, string beforeLine = null, bool isBlock = false)
        {
            CaretBack = caretBack;
            BeforeLine = beforeLine;
            IsBlock = isBlock;
            Kind = kind;
            DisplayText = displayText;
            InsertText = insertText;
            FilterText = filterText;
            Suffix = suffix;
            Description = description;
            Group = group;
            FromClause = fromClause;
            Detail = detail;
            Key = key;
            Tag = tag;
        }

        public SuggestionKind Kind { get; }

        /// <summary>How many characters before the end of the inserted text the caret goes, e.g. 1 to land inside the parentheses of a function.</summary>
        public int CaretBack { get; }

        /// <summary>Text (whole lines) inserted before the line the item is committed on, e.g. the DECLARE lines above an EXEC; null for most items.</summary>
        public string BeforeLine { get; }

        /// <summary>The item inserts several lines; the character that committed it (Space) is not typed after it.</summary>
        public bool IsBlock { get; }

        /// <summary>Schema of the database object whose definition (script) belongs to the item; null for items that are no object.</summary>
        public string DefinitionSchema { get; private set; }

        /// <summary>Name of the database object whose definition belongs to the item.</summary>
        public string DefinitionName { get; private set; }

        /// <summary>For a column item: the schema of the table or view it belongs to (null when it is not a database object), so that its description can be read (#206).</summary>
        public string ColumnOwnerSchema { get; private set; }

        /// <summary>For a column item: the name of the table or view it belongs to.</summary>
        public string ColumnOwnerName { get; private set; }

        internal Suggestion WithColumnOwner(DbObject owner)
        {
            if (owner != null)
            {
                ColumnOwnerSchema = owner.Schema;
                ColumnOwnerName = owner.Name;
            }

            return this;
        }

        internal Suggestion WithDefinition(string schema, string name)
        {
            DefinitionSchema = schema;
            DefinitionName = name;
            return this;
        }

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

        /// <summary>For a column: whether it is part of the primary key or of a foreign key (shown as a key icon).</summary>
        public KeyKind Key { get; }

        /// <summary>The object the item stands for when it is more than text (a <see cref="SqlPrompter.Core.Snippets.Snippet"/>); null otherwise.</summary>
        public object Tag { get; }

        /// <summary>Sort key: group (negative groups first), then text.</summary>
        public string SortText => (Group + 100).ToString("D3") + DisplayText;

        /// <summary>Sort key with the usage of the item: within a group the items chosen more often come first.</summary>
        public string RankedSortText(int usageCount)
        {
            // Usage comes first: a snippet that was chosen before is not left at the end of the list.
            var group = Kind == SuggestionKind.Snippet && usageCount > 0 ? Math.Min(Group, 1) : Group;
            return (group + 100).ToString("D3") + UsageStatistics.SortPrefix(usageCount) + DisplayText;
        }

        /// <summary>Identifies the item for the usage statistics: its kind, its text and where it belongs (schema, alias).</summary>
        public string UsageKey => Kind + "|" + DisplayText + "|" + Suffix;

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
            if (s.IsBlock || (s.Kind != SuggestionKind.Keyword && s.Kind != SuggestionKind.Function && s.Kind != SuggestionKind.DataType))
            {
                return s;
            }

            string Convert(string text) => keywordCase == KeywordCase.Upper ? text.ToUpperInvariant() : text.ToLowerInvariant();
            return new Suggestion(s.Kind, Convert(s.DisplayText), Convert(s.InsertText), s.FilterText, s.Suffix, s.Description, s.Group, s.FromClause, s.Detail, s.Key, s.Tag, s.CaretBack, s.BeforeLine, s.IsBlock).WithDefinition(s.DefinitionSchema, s.DefinitionName);
        }

        private static IReadOnlyList<Suggestion> BuildCore(CaretContext context, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
        {
            switch (context.Kind)
            {
                case ContextKind.Keyword:
                    return StatementFragments.Build(context, metadata, options).Concat(KeywordSuggestions(context, 0)).Concat(SnippetSuggestions(context, options)).ToList();
                case ContextKind.ObjectName:
                    return context.Module != ModuleKind.None
                        ? ObjectSuggestions(context, metadata, script, options).Concat(context.ModuleAction == ModuleAction.Drop ? KeywordSuggestions(context, 1) : Enumerable.Empty<Suggestion>()).ToList()
                        : ObjectSuggestions(context, metadata, script, options).Concat(SnippetSuggestions(context, options)).ToList();
                case ContextKind.InsertColumns:
                    return InsertColumnSuggestions(context, metadata, options).ToList();
                case ContextKind.Expression:
                {
                    var all = StatementFragments.Build(context, metadata, options)
                        .Concat(SelectListSuggestions.Leading(context))
                        .Concat(ExpressionSuggestions(context, metadata, script, options));
                    var columns = SelectListSuggestions.ColumnFilter(context);
                    return (columns == null ? all : all.Where(columns)).ToList();
                }
                case ContextKind.Name:
                    if (context.ConstraintTable != null || context.ExistingConstraintsOf != null)
                    {
                        return ConstraintNameSuggestions(context, metadata).ToList();
                    }

                    return context.UnknownExpressionQualifier && metadata != null
                        ? SchemaFunctionSuggestions(context, metadata).ToList()
                        : (IReadOnlyList<Suggestion>)Array.Empty<Suggestion>();
                case ContextKind.Variable:
                    return VariableSuggestions(context, script, 0).ToList();
                case ContextKind.DataType:
                    return ColumnTypeSuggestions(context, metadata).Concat(SizedTypeSuggestions(context)).Concat(DataTypeSuggestions(context, 0)).ToList();
                case ContextKind.Fallback:
                    return StatementFragments.Build(context, metadata, options).Concat(KeywordSuggestions(context, 0))
                        .Concat(SnippetSuggestions(context, options))
                        .Concat(FunctionSuggestions(context, 2))
                        .Concat(DataTypeSuggestions(context, 3))
                        .ToList();
                default:
                    return Array.Empty<Suggestion>();
            }
        }

        /// <summary>Functions that return the current date and time: first after a comparison with a date column.</summary>
        private static readonly HashSet<string> DateFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GETDATE", "SYSDATETIME", "GETUTCDATE", "SYSUTCDATETIME", "CURRENT_TIMESTAMP",
        };

        /// <summary>The functions that only make sense in a CATCH block: first there.</summary>
        private static readonly HashSet<string> ErrorFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ERROR_NUMBER", "ERROR_MESSAGE", "ERROR_SEVERITY", "ERROR_STATE", "ERROR_LINE", "ERROR_PROCEDURE", "XACT_STATE",
        };

        private static bool InCatch(CaretContext context) => context.Text != null && CatchBlock.IsInside(context.Text, context.Caret);

        /// <summary>The column named by a condition operand (qualifier first) in the sources of the block, or null.</summary>
        private static DbColumn FindOperandColumn(IReadOnlyList<string> parts, IReadOnlyList<SourceColumns> resolved)
        {
            var name = parts[parts.Count - 1];
            var qualifier = parts.Count > 1 ? parts[parts.Count - 2] : null;
            foreach (var x in resolved.OrderBy(r => r.Source.ScopeLevel))
            {
                if (qualifier != null && !string.Equals(x.Source.ReferenceName, qualifier, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var column = x.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (column != null)
                {
                    return column;
                }
            }

            return null;
        }

        private static IEnumerable<Suggestion> FunctionSuggestions(CaretContext context, int group, ISet<string> preferred = null)
        {
            foreach (var function in Keywords.BuiltInFunctions)
            {
                var text = Keywords.ApplyCase(function, context.TypedPrefix);
                // The name comes with parentheses; the caret goes between them unless the function takes no arguments.
                yield return new Suggestion(SuggestionKind.Function, text, text + "()", text, null, "built-in function", preferred != null && preferred.Contains(function) ? 0 : group, caretBack: Keywords.TakesNoArguments(function) ? 0 : 1);

                if (Keywords.IsWindowFunction(function))
                {
                    // The call with its OVER clause; the caret goes to the first slot (the argument, or PARTITION BY for the functions without one).
                    var noArguments = Keywords.IsRankingWithoutArguments(function);
                    var tail = noArguments ? " ORDER BY )" : ") OVER (PARTITION BY  ORDER BY )";
                    // As a snippet the slots are fields: Tab goes from the argument to PARTITION BY and to ORDER BY (#196).
                    var body = noArguments
                        ? text + "() OVER (PARTITION BY $partition$ ORDER BY $order$)$CURSOR$"
                        : text + "($argument$) OVER (PARTITION BY $partition$ ORDER BY $order$)$CURSOR$";
                    var fields = new List<SqlPrompter.Core.Snippets.SnippetPlaceholder>
                    {
                        new SqlPrompter.Core.Snippets.SnippetPlaceholder { Name = "partition", DefaultValue = "partition_column" },
                        new SqlPrompter.Core.Snippets.SnippetPlaceholder { Name = "order", DefaultValue = "order_column" },
                    };
                    if (!noArguments)
                    {
                        fields.Insert(0, new SqlPrompter.Core.Snippets.SnippetPlaceholder { Name = "argument", DefaultValue = Keywords.TakesNoArguments(function) ? string.Empty : "column" });
                    }

                    var windowSnippet = SqlPrompter.Core.Snippets.Snippet.Create(string.Empty, "window function with OVER", body, fields);
                    yield return new Suggestion(SuggestionKind.Function, text + "() OVER (...)", text + "() OVER (PARTITION BY  ORDER BY )", text, null, "window function with OVER", group,
                        caretBack: tail.Length, tag: windowSnippet);
                }
            }
        }

        /// <summary>
        /// DECLARE @IdCustomer |, CAST(IdCustomer AS |: the types, with their exact length, precision and scale, of the columns of the database that have the same name
        /// (case-insensitive; only columns whose table has been used, so that its columns are loaded); the most common first.
        /// </summary>
        private static IEnumerable<Suggestion> ColumnTypeSuggestions(CaretContext context, IDatabaseMetadata metadata)
        {
            if (metadata == null || string.IsNullOrEmpty(context.TypeOfName))
            {
                yield break;
            }

            var found = new List<KeyValuePair<string, string>>();
            foreach (var dbObject in metadata.Objects.Where(o => !o.IsSystem && (o.Kind == DbObjectKind.Table || o.Kind == DbObjectKind.View)))
            {
                var column = metadata.GetColumns(dbObject)?.FirstOrDefault(c => string.Equals(c.Name, context.TypeOfName, StringComparison.OrdinalIgnoreCase));
                if (column != null && column.TypeName.Length > 0)
                {
                    found.Add(new KeyValuePair<string, string>(column.TypeName, dbObject.Schema + "." + dbObject.Name + "." + column.Name));
                }
            }

            var order = 0;
            foreach (var type in found.GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()))
            {
                var owners = type.Select(t => t.Value).Take(3).ToList();
                yield return new Suggestion(SuggestionKind.DataType, type.Key, type.Key, type.Key, owners[0], "type of " + string.Join(", ", owners) + (type.Count() > 3 ? " ..." : string.Empty), -80 + order++);
            }
        }

        private static readonly string[] SizedTypes =
        {
            "nvarchar(50)", "nvarchar(100)", "nvarchar(255)", "nvarchar(MAX)", "varchar(50)", "varchar(100)", "varchar(255)", "varchar(MAX)", "char(10)", "nchar(10)",
            "decimal(18,2)", "decimal(18,4)", "datetime2(0)", "datetime2(7)", "varbinary(MAX)",
        };

        /// <summary>The usual lengths in a column definition, after the types of the same-named columns.</summary>
        private static IEnumerable<Suggestion> SizedTypeSuggestions(CaretContext context)
        {
            if (!context.DataTypeSizes)
            {
                yield break;
            }

            var order = 0;
            foreach (var type in SizedTypes)
            {
                yield return new Suggestion(SuggestionKind.DataType, type, type, type, null, "data type with a usual length", -60 + order++);
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

        /// <summary>
        /// Keywords: the preferred ones of the position first, then those that can follow as well, then the rest. In an expression
        /// (<paramref name="inExpression"/>) the keywords of a position after a complete operand rank above the columns, the others with the aliases.
        /// </summary>
        private static IEnumerable<Suggestion> KeywordSuggestions(CaretContext context, int firstGroup, bool inExpression = false)
        {
            var preferred = new HashSet<string>(context.PreferredKeywords, StringComparer.OrdinalIgnoreCase);
            var next = new HashSet<string>(context.NextKeywords, StringComparer.OrdinalIgnoreCase);
            var firstRank = inExpression ? (context.KeywordsAfterOperand ? -2 : 1) : firstGroup - 1;
            var nextRank = inExpression ? (context.KeywordsAfterOperand ? -1 : 1) : firstGroup;
            var restRank = inExpression ? firstGroup : firstGroup + 1;
            var all = context.PreferredKeywords.Concat(context.NextKeywords);
            if (!context.OnlyExpectedKeywords)
            {
                all = all.Concat(Keywords.All);
            }

            foreach (var keyword in all.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var text = Keywords.ApplyCase(keyword, context.TypedPrefix);
                yield return new Suggestion(SuggestionKind.Keyword, text, text, text, null, null,
                    preferred.Contains(keyword) ? firstRank : next.Contains(keyword) ? nextRank : restRank);
            }
        }

        /// <summary>The group of the snippets that do not match the typed text at its start (and of all of them before a character is typed): the end of the list.</summary>
        internal const int SnippetsLastGroup = 50;

        /// <summary>
        /// Snippets are always offered (not after a qualifier): those whose prefix starts with what is typed next to the other likely items,
        /// the others (those that contain it and, before a character is typed, all of them) at the end of the list. Items chosen often
        /// move up (<see cref="Suggestion.RankedSortText"/>).
        /// </summary>
        private static IEnumerable<Suggestion> SnippetSuggestions(CaretContext context, SuggestionOptions options)
        {
            if (context.Qualifier.Count > 0)
            {
                yield break;
            }

            var typed = context.TypedPrefix;
            foreach (var snippet in options.Snippets.Where(s => typed.Length == 0 || s.Prefix.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var startsWith = typed.Length > 0 && snippet.Prefix.StartsWith(typed, StringComparison.OrdinalIgnoreCase);
                var description = string.IsNullOrWhiteSpace(snippet.Description) ? "snippet" : "snippet: " + snippet.Description;
                yield return new Suggestion(SuggestionKind.Snippet, snippet.Prefix, snippet.Prefix, snippet.Prefix, "snippet", description + "\n\n" + Preview(snippet.Body), startsWith ? 1 : SnippetsLastGroup,
                    null, string.IsNullOrWhiteSpace(snippet.Description) ? null : snippet.Description, KeyKind.None, snippet);
            }
        }

        /// <summary>The start of the body, shown as the description of a snippet.</summary>
        private static string Preview(string body)
        {
            var lines = (body ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var text = string.Join("\n", lines.Take(12));
            return lines.Length > 12 ? text + "\n…" : text;
        }

        private static IEnumerable<Suggestion> VariableSuggestions(CaretContext context, ScriptModel script, int group, TypeClass valueType = TypeClass.Unknown)
        {
            foreach (var variable in script.Variables)
            {
                // After a comparison the variables of the type of the column come first.
                var fits = valueType != TypeClass.Unknown && OperatorSuggestions.Classify(variable.TypeName) == valueType;
                var description = (variable.IsParameter ? "parameter " : "variable ") + variable.Name +
                    (variable.TypeName.Length > 0 ? " " + variable.TypeName : string.Empty);
                yield return new Suggestion(SuggestionKind.Variable, variable.Name, variable.Name, variable.Name,
                    variable.TypeName.Length > 0 ? variable.TypeName : null, description, fits ? 0 : group);
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

        /// <summary>
        /// After ALTER PROCEDURE, FUNCTION, VIEW or TRIGGER (also CREATE OR ALTER and DROP): the modules of that kind only. For ALTER and CREATE OR ALTER
        /// the item carries a <see cref="ModuleScript"/>: its commit replaces the statement with the definition of the module.
        /// </summary>
        private static IEnumerable<Suggestion> ModuleSuggestions(CaretContext context, IDatabaseMetadata metadata)
        {
            if (metadata == null || context.Qualifier.Count > 1)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            string schemaFilter = context.Qualifier.Count == 1
                ? (context.Qualifier[0].Length == 0 ? metadata.DefaultSchema : context.Qualifier[0])
                : null;
            var modules = new List<(string Schema, string Name, SuggestionKind Kind, string Description, string Suffix)>();
            switch (context.Module)
            {
                case ModuleKind.Procedure:
                    modules.AddRange(metadata.Procedures.Select(p => (p.Schema, p.Name, SuggestionKind.Procedure, "procedure " + p, (string)p.Schema)));
                    break;
                case ModuleKind.View:
                    modules.AddRange(metadata.Objects.Where(o => o.Kind == DbObjectKind.View && !o.IsSystem).Select(o => (o.Schema, o.Name, SuggestionKind.View, "view " + o, (string)o.Schema)));
                    break;
                case ModuleKind.Function:
                    modules.AddRange(metadata.Objects.Where(o => (o.Kind == DbObjectKind.ScalarFunction || o.Kind == DbObjectKind.TableFunction) && !o.IsSystem)
                        .Select(o => (o.Schema, o.Name, o.Kind == DbObjectKind.ScalarFunction ? SuggestionKind.ScalarFunction : SuggestionKind.TableFunction,
                            (o.Kind == DbObjectKind.ScalarFunction ? "scalar function " : "table function ") + o, (string)o.Schema)));
                    break;
                case ModuleKind.Index:
                    foreach (var index in metadata.Indexes)
                    {
                        // ON table is part of the insert: ALTER INDEX ix ON dbo.T REBUILD, DROP INDEX ix ON dbo.T.
                        var table = SqlIdentifier.Quote(index.Schema, quote) + "." + SqlIdentifier.Quote(index.Table, quote);
                        yield return new Suggestion(SuggestionKind.Index, index.Name, SqlIdentifier.Quote(index.Name, quote) + " ON " + table, index.Name, index.Schema + "." + index.Table,
                            index.TypeText + " index on " + index.Schema + "." + index.Table, 0);
                    }

                    if (context.ModuleAction == ModuleAction.Alter)
                    {
                        yield return new Suggestion(SuggestionKind.Keyword, "ALL", "ALL", "ALL", null, "every index of the table: ALTER INDEX ALL ON table", 0);
                    }

                    yield break;
                case ModuleKind.Trigger:
                    modules.AddRange(metadata.Triggers.Select(t => (t.Schema, t.Name, SuggestionKind.Procedure,
                        t.ParentName == null ? "database trigger " + t.Name : "trigger " + t + " on " + t.ParentName, t.ParentName ?? "database")));
                    break;
            }

            foreach (var module in modules)
            {
                if (schemaFilter != null && !string.Equals(module.Schema, schemaFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = SqlIdentifier.Quote(module.Name, quote);
                var insert = module.Schema == null || schemaFilter != null ? name : SqlIdentifier.Quote(module.Schema, quote) + "." + name;
                var tag = context.ModuleAction == ModuleAction.Drop
                    ? null
                    : new ModuleScript(context.Module, module.Schema, module.Name, context.ModuleAction == ModuleAction.CreateOrAlter, context.ModuleHeaderStart);
                yield return new Suggestion(module.Kind, module.Name, insert, module.Name, module.Suffix, module.Description, 0, tag: tag)
                    .WithDefinition(module.Schema, module.Name);
            }
        }

        private static IEnumerable<Suggestion> ObjectSuggestions(CaretContext context, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
        {
            if (context.Module != ModuleKind.None)
            {
                foreach (var suggestion in ModuleSuggestions(context, metadata))
                {
                    yield return suggestion;
                }

                yield break;
            }

            if (context.ProcedureName)
            {
                foreach (var suggestion in ProcedureSuggestions(context, metadata, options))
                {
                    yield return suggestion;
                }

                yield break;
            }

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
                foreach (var dbObject in metadata.Objects.Where(o => o.Kind != DbObjectKind.ScalarFunction && string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase)))
                {
                    var qualifiedInsert = SqlIdentifier.Quote(dbObject.Name, quote) + ObjectAlias(context, options, dbObject);
                    foreach (var suggestion in WithInsertColumns(context, metadata, options, dbObject, qualifiedInsert, null))
                    {
                        yield return suggestion;
                    }
                }

                yield break;
            }

            if (context.AfterJoin)
            {
                foreach (var join in JoinSuggestions.Build(context, metadata, options))
                {
                    yield return join;
                }
            }

            if (context.CorrelatedFrom)
            {
                foreach (var related in JoinSuggestions.Build(context, metadata, options, correlated: true))
                {
                    yield return related;
                }
            }

            foreach (var dbObject in metadata.Objects.Where(o => !o.IsSystem && o.Kind != DbObjectKind.ScalarFunction))
            {
                var insert = (options.Aliases.QualifyObjectNames ? SqlIdentifier.Quote(dbObject.Schema, quote) + "." : string.Empty) +
                    SqlIdentifier.Quote(dbObject.Name, quote) + ObjectAlias(context, options, dbObject);
                foreach (var suggestion in WithInsertColumns(context, metadata, options, dbObject, insert, dbObject.Schema))
                {
                    yield return suggestion;
                }
            }

            foreach (var schema in metadata.Schemas)
            {
                yield return new Suggestion(SuggestionKind.Schema, schema, SqlIdentifier.Quote(schema, quote), schema, null, "schema", 1);
            }

            foreach (var server in context.InOtherDatabase ? Array.Empty<string>() : metadata.LinkedServers.ToArray())
            {
                yield return new Suggestion(SuggestionKind.LinkedServer, server, SqlIdentifier.Quote(server, quote), server, null, "linked server", 1);
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

        /// <summary>
        /// The suggestion for a table or view; as the target of an INSERT it carries the column list (or has a second item that does),
        /// depending on the settings.
        /// </summary>
        private static IEnumerable<Suggestion> WithInsertColumns(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options, DbObject dbObject, string insertText, string suffix)
        {
            var plain = ObjectSuggestion(dbObject, insertText, suffix);
            var columns = context.InsertTarget && (dbObject.Kind == DbObjectKind.Table || dbObject.Kind == DbObjectKind.View)
                ? InsertBuilder.InsertableColumns(metadata.GetColumns(dbObject))
                : null;
            if (columns == null || columns.Count == 0 || options.Inserts.ColumnsMode == InsertColumnsMode.AfterParenthesis)
            {
                yield return plain;
                yield break;
            }

            var withColumns = new Suggestion(
                plain.Kind,
                options.Inserts.ColumnsMode == InsertColumnsMode.Item ? dbObject.Name + " (columns)" : dbObject.Name,
                InsertBuilder.TableWithColumns(insertText, columns, options.Inserts, options.Editor, context.QuoteIdentifiers),
                dbObject.Name,
                suffix,
                plain.Description + " with its column list",
                0,
                isBlock: true).WithDefinition(plain.DefinitionSchema, plain.DefinitionName);
            if (options.Inserts.ColumnsMode == InsertColumnsMode.Item)
            {
                yield return plain;
            }

            yield return withColumns;
        }

        /// <summary>
        /// After CONSTRAINT (#213): names by convention made from the table and the column (PK_t, FK_t_col, DF_t_col, UQ_t_col, CK_t_col), or, after DROP /
        /// CHECK / NOCHECK CONSTRAINT, the existing constraints of the table.
        /// </summary>
        private static IEnumerable<Suggestion> ConstraintNameSuggestions(CaretContext context, IDatabaseMetadata metadata)
        {
            var quote = context.QuoteIdentifiers;
            if (context.ExistingConstraintsOf != null)
            {
                if (metadata == null)
                {
                    yield break;
                }

                var parts = context.ExistingConstraintsOf;
                var table = parts[parts.Count - 1];
                var schema = parts.Count > 1 ? parts[parts.Count - 2] : null;
                foreach (var constraint in metadata.Constraints.Where(c => string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase) &&
                    (schema == null || string.Equals(c.Schema, schema, StringComparison.OrdinalIgnoreCase))))
                {
                    yield return new Suggestion(SuggestionKind.Index, constraint.Name, SqlIdentifier.Quote(constraint.Name, quote), constraint.Name, constraint.TypeText, constraint.TypeText + " constraint on " + constraint.Schema + "." + constraint.Table, 0);
                }

                yield break;
            }

            var t = context.ConstraintTable;
            var c = context.ConstraintColumn;
            var names = new List<(string Prefix, string Text)>();
            if (c != null)
            {
                names.Add(("DF", "default of the column"));
                names.Add(("UQ", "unique"));
                names.Add(("FK", "foreign key"));
                names.Add(("CK", "check"));
                names.Add(("PK", "primary key (the table name only)"));
            }
            else
            {
                names.Add(("PK", "primary key"));
                names.Add(("FK", "foreign key"));
                names.Add(("UQ", "unique"));
                names.Add(("CK", "check"));
                names.Add(("DF", "default"));
            }

            for (var i = 0; i < names.Count; i++)
            {
                var (prefix, text) = names[i];
                var name = prefix + "_" + t + (c != null && prefix != "PK" ? "_" + c : string.Empty);

                // The group keeps the order of the most likely names.
                yield return new Suggestion(SuggestionKind.Index, name, SqlIdentifier.Quote(name, quote), name, null, text + " constraint name", i);
            }
        }

        /// <summary>The columns an INSERT can supply, after "INSERT INTO table (", and an item that inserts all of them.</summary>
        private static IEnumerable<Suggestion> InsertColumnSuggestions(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            var source = context.Sources.FirstOrDefault();
            var dbObject = source == null ? null : Readiness.Resolve(source, metadata);
            var columns = dbObject == null ? null : context.IndexColumns
                ? metadata.GetColumns(dbObject)?.Where(c => !context.ExcludedColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList()
                : InsertBuilder.InsertableColumns(metadata.GetColumns(dbObject));
            if (columns == null || columns.Count == 0)
            {
                yield break;
            }

            if (context.FirstInsertColumn && options.Inserts.ColumnsMode == InsertColumnsMode.AfterParenthesis)
            {
                yield return new Suggestion(
                    SuggestionKind.Column,
                    "(all columns)",
                    InsertBuilder.ColumnsAfterParenthesis(columns, options.Inserts, options.Editor, context.QuoteIdentifiers),
                    "all columns",
                    null,
                    "all insertable columns of " + dbObject + " (not identity, computed or rowversion)",
                    -1,
                    isBlock: true);
            }

            if (context.KeyColumnsOfReference)
            {
                // REFERENCES t (|: the primary key first (all of its columns as one item when there are several).
                var key = metadata.GetPrimaryKey(dbObject);
                if (key.Count > 1)
                {
                    yield return new Suggestion(SuggestionKind.Column, "(primary key)", string.Join(", ", key.Select(k => SqlIdentifier.Quote(k, context.QuoteIdentifiers))), "primary key", null,
                        "the primary key columns of " + dbObject, -2);
                }

                foreach (var column in columns)
                {
                    var isKey = key.Contains(column.Name, StringComparer.OrdinalIgnoreCase);
                    yield return ColumnSuggestion(column, SqlIdentifier.Quote(column.Name, context.QuoteIdentifiers), null, dbObject.Name, isKey ? -1 : 0, isKey ? KeyKind.Primary : KeyKind.None);
                }

                yield break;
            }

            foreach (var column in columns)
            {
                yield return ColumnSuggestion(column, SqlIdentifier.Quote(column.Name, context.QuoteIdentifiers), null, dbObject.Name, 0, KeyKind.None);
            }
        }

        /// <summary>Stored procedures after EXEC; choosing one inserts its parameters (and DECLARE lines for the OUTPUT ones).</summary>
        private static IEnumerable<Suggestion> ProcedureSuggestions(CaretContext context, IDatabaseMetadata metadata, SuggestionOptions options)
        {
            if (metadata == null || context.Qualifier.Count > 1)
            {
                yield break;
            }

            var quote = context.QuoteIdentifiers;
            if (context.Qualifier.Count == 1)
            {
                var schema = context.Qualifier[0].Length == 0 ? metadata.DefaultSchema : context.Qualifier[0];
                foreach (var procedure in metadata.Procedures.Where(p => string.Equals(p.Schema, schema, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return ProcedureSuggestion(procedure, SqlIdentifier.Quote(procedure.Name, quote), null, options);
                }

                yield break;
            }

            foreach (var procedure in metadata.Procedures)
            {
                var insert = (options.Aliases.QualifyObjectNames ? SqlIdentifier.Quote(procedure.Schema, quote) + "." : string.Empty) + SqlIdentifier.Quote(procedure.Name, quote);
                yield return ProcedureSuggestion(procedure, insert, procedure.Schema, options);
            }

            foreach (var schema in metadata.Schemas)
            {
                yield return new Suggestion(SuggestionKind.Schema, schema, SqlIdentifier.Quote(schema, quote), schema, null, "schema", 1);
            }

            // The system procedures that are used all the time (#218); a procedure of the database with the same name hides them.
            foreach (var system in SystemProcedures.All.Where(s => !metadata.Procedures.Any(p => string.Equals(p.Name, s.Name, StringComparison.OrdinalIgnoreCase))))
            {
                yield return ProcedureSuggestion(system, system.Name, "system", options, 2);
            }
        }

        private static Suggestion ProcedureSuggestion(DbProcedure procedure, string nameText, string suffix, SuggestionOptions options, int group = 0)
        {
            var parameters = procedure.Parameters;
            var signature = string.Join(", ", parameters.Select(p => p.Name + " " + p.TypeName + (p.IsOutput ? " OUTPUT" : string.Empty) + (p.HasDefault ? " = " + (p.DefaultText ?? "...") : string.Empty)));
            var declarations = InsertBuilder.OutputDeclarations(parameters, options.Editor);
            var suggestion = new Suggestion(
                SuggestionKind.Procedure,
                procedure.Name,
                InsertBuilder.ProcedureCall(nameText, parameters, options.Editor),
                procedure.Name,
                suffix,
                (procedure.Schema == SystemProcedures.Schema ? "system procedure " + procedure.Name : "procedure " + procedure) + (parameters.Count > 0 ? " (" + signature + ")" : string.Empty),
                group,
                beforeLine: declarations.Length > 0 ? declarations : null,
                isBlock: parameters.Count > 0);
            return procedure.Schema == SystemProcedures.Schema ? suggestion : suggestion.WithDefinition(procedure.Schema, procedure.Name);
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

            var alias = AliasGenerator.Generate(objectName, context.ReferenceNamesInUse(), options.Aliases);
            return AliasDefinition(alias, options, context.QuoteIdentifiers);
        }

        /// <summary>" AS alias" or " alias" as set in the options.</summary>
        internal static string AliasDefinition(string alias, SuggestionOptions options, bool quoteIdentifiers)
        {
            var quoted = SqlIdentifier.Quote(alias, quoteIdentifiers);
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

        /// <summary>schema.| in an expression: the scalar functions of the schema.</summary>
        private static IEnumerable<Suggestion> SchemaFunctionSuggestions(CaretContext context, IDatabaseMetadata metadata)
        {
            var schema = context.Qualifier[0].Length == 0 ? metadata.DefaultSchema : context.Qualifier[0];
            foreach (var function in metadata.Objects.Where(o => o.Kind == DbObjectKind.ScalarFunction && !o.IsSystem && string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase)))
            {
                yield return ScalarFunctionSuggestion(function, SqlIdentifier.Quote(function.Name, context.QuoteIdentifiers), null);
            }
        }

        /// <summary>A scalar function with its parentheses; the caret goes between them when the function has parameters.</summary>
        private static Suggestion ScalarFunctionSuggestion(DbObject function, string insertName, string suffix) =>
            new Suggestion(
                SuggestionKind.ScalarFunction,
                function.Name,
                insertName + "()",
                function.Name,
                suffix,
                "scalar function " + function + " (" + function.ParameterCount + (function.ParameterCount == 1 ? " parameter)" : " parameters)"),
                3,
                caretBack: function.ParameterCount > 0 ? 1 : 0).WithDefinition(function.Schema, function.Name);

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
            var isFunction = dbObject.Kind == DbObjectKind.TableFunction;
            return new Suggestion(
                kind,
                dbObject.Name,
                isFunction ? insertText + "()" : insertText,
                dbObject.Name,
                suffix,
                description + " " + dbObject + target,
                0,
                caretBack: isFunction && dbObject.ParameterCount > 0 ? 1 : 0)
                .WithDefinition(dbObject.Kind == DbObjectKind.Synonym ? null : dbObject.Schema, dbObject.Kind == DbObjectKind.Synonym ? null : dbObject.Name);
        }

        /// <summary>Columns of one table source and how to describe their owner.</summary>
        internal sealed class SourceColumns
        {
            public SourceColumns(TableSource source, IReadOnlyList<DbColumn> columns, string owner, DbObject dbObject = null, IDatabaseMetadata metadata = null)
            {
                Source = source;
                Columns = columns;
                Owner = owner;
                Object = dbObject;
                if (dbObject != null && metadata != null)
                {
                    primary = new HashSet<string>(metadata.GetPrimaryKey(dbObject), StringComparer.OrdinalIgnoreCase);
                    foreign = new HashSet<string>(
                        metadata.ForeignKeys.Where(k => k.ChildObjectId == dbObject.Id).SelectMany(k => k.Columns.Select(c => c.Column)),
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            public TableSource Source { get; }

            public IReadOnlyList<DbColumn> Columns { get; }

            public string Owner { get; }

            /// <summary>The table or view the columns belong to, when it is an object of the database.</summary>
            public DbObject Object { get; }

            private readonly HashSet<string> primary = new HashSet<string>();
            private readonly HashSet<string> foreign = new HashSet<string>();

            public KeyKind KeyOf(DbColumn column) => primary.Contains(column.Name) ? KeyKind.Primary : foreign.Contains(column.Name) ? KeyKind.Foreign : KeyKind.None;
        }

        /// <summary>Columns of a source defined in the script or found in the database; null if they are not known (yet).</summary>
        internal static SourceColumns ResolveColumns(TableSource source, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
        {
            var scripted = script.Resolve(source, metadata);
            if (scripted != null)
            {
                var owner = source.Kind == TableSourceKind.Derived ? "derived table" : string.Join(".", source.NameParts);
                return new SourceColumns(source, scripted.Columns, owner);
            }

            if (source.Kind == TableSourceKind.Object && source.NameParts.Count == 4 && options.RemoteColumns != null)
            {
                // server.database.schema.table: on a linked server, loaded when the table is used.
                var remote = options.RemoteColumns(source);
                return remote == null ? null : new SourceColumns(source, remote, string.Join(".", source.NameParts));
            }

            var dbObject = Readiness.Resolve(source, metadata);
            var columns = dbObject != null ? metadata.GetColumns(dbObject) : null;
            return columns == null ? null : new SourceColumns(source, columns, dbObject.ToString(), dbObject, metadata);
        }

        /// <summary>The columns of a source in the script or the database, or null if they are not known (yet).</summary>
        internal static IReadOnlyList<DbColumn> ColumnsOf(TableSource source, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options) =>
            ResolveColumns(source, metadata, script ?? ScriptModel.Empty, options ?? SuggestionOptions.Default)?.Columns;

        private static IEnumerable<Suggestion> ExpressionSuggestions(CaretContext context, IDatabaseMetadata metadata, ScriptModel script, SuggestionOptions options)
        {
            var quote = context.QuoteIdentifiers;
            if (context.QualifiedSource != null)
            {
                var qualified = ResolveColumns(context.QualifiedSource, metadata, script, options);
                foreach (var column in qualified?.Columns ?? Array.Empty<DbColumn>())
                {
                    yield return ColumnSuggestion(column, SqlIdentifier.Quote(column.Name, quote), null, qualified.Owner, 0, qualified.KeyOf(column)).WithColumnOwner(qualified.Object);
                }

                yield break;
            }

            var resolved = context.Sources
                .Select(s => ResolveColumns(s, metadata, script, options))
                .Where(x => x != null)
                .ToList();

            // In a condition: the operators that fit the column before the caret, or after a comparison the values of its type first.
            var operand = context.ConditionOperand == null ? null : FindOperandColumn(context.ConditionOperand, resolved);
            var valueType = operand != null && context.AfterComparison ? OperatorSuggestions.Classify(operand.TypeName) : TypeClass.Unknown;
            var operators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (operand != null && !context.AfterComparison)
            {
                foreach (var op in OperatorSuggestions.Build(operand))
                {
                    operators.Add(op.DisplayText);
                    yield return op;
                }
            }

            // GROUP BY |: one item repeats the select-list items that are not aggregates.
            if (context.Text != null && context.TypedPrefix.Length == 0 && ColumnClauses.At(context.Text, context.Caret) == ColumnClause.GroupBy)
            {
                var items = ColumnClauses.NonAggregatedItems(context.Text, context.Caret);
                if (items.Count > 0)
                {
                    yield return new Suggestion(SuggestionKind.Column, "All non-aggregated columns", FormattedList.Join(items, options.Editor.LinePrefix, options.Editor), "all non-aggregated columns", null,
                        "the items of the select list that are not aggregates: " + string.Join(", ", items), -2, isBlock: true);
                }
            }

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
                    var mismatch = valueType != TypeClass.Unknown && OperatorSuggestions.Classify(column.TypeName) != valueType;
                    yield return ColumnSuggestion(column, insert, reference, x.Owner, inner && !mismatch ? 0 : 2, x.KeyOf(column)).WithColumnOwner(x.Object);
                }
            }

            foreach (var source in context.Sources.Where(s => s.ReferenceName != null)
                         .GroupBy(s => s.ReferenceName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                var target = source.Kind == TableSourceKind.Derived ? "derived table" : string.Join(".", source.NameParts);
                yield return new Suggestion(SuggestionKind.Alias, source.ReferenceName, SqlIdentifier.Quote(source.ReferenceName, quote),
                    source.ReferenceName, source.Alias != null ? target : null, (source.Alias != null ? "alias " : "source ") + target, 1);
            }

            if (context.JoinTarget != null)
            {
                foreach (var condition in JoinSuggestions.Conditions(context, metadata, options))
                {
                    yield return condition;
                }
            }

            if (context.SelectWithoutFrom)
            {
                foreach (var column in AllColumnSuggestions(context, metadata, options))
                {
                    yield return column;
                }
            }

            foreach (var variable in VariableSuggestions(context, script, 2, valueType))
            {
                yield return variable;
            }

            foreach (var snippet in SnippetSuggestions(context, options))
            {
                yield return snippet;
            }

            foreach (var function in FunctionSuggestions(context, 3, valueType == TypeClass.Date ? DateFunctions : InCatch(context) ? ErrorFunctions : null))
            {
                yield return function;
            }

            foreach (var function in (metadata?.Objects ?? System.Array.Empty<DbObject>()).Where(o => o.Kind == DbObjectKind.ScalarFunction && !o.IsSystem))
            {
                yield return ScalarFunctionSuggestion(function, SqlIdentifier.Quote(function.Schema, quote) + "." + SqlIdentifier.Quote(function.Name, quote), function.Schema);
            }

            foreach (var keyword in KeywordSuggestions(context, 4, inExpression: true))
            {
                // The operator items already offer IN, LIKE, BETWEEN ... with the right text; the plain keyword would only duplicate them (and could be the one preselected).
                if (operators.Contains(keyword.DisplayText))
                {
                    continue;
                }

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
            var keys = new KeyLookup(metadata);
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
                    SqlIdentifier.Quote(table.Name, quote) + (alias != null ? AliasDefinition(alias, options, context.QuoteIdentifiers) : string.Empty);
                var column = match.Value;
                var type = column.TypeName.Length > 0 ? " " + column.TypeName + (column.IsNullable ? " null" : " not null") : string.Empty;
                yield return new Suggestion(SuggestionKind.Column, column.Name, insert, column.Name, table.Name,
                    column.Name + type + " (" + table + "), adds " + from, 0, from, type.Trim(), keys.KeyOf(table, column.Name));
            }
        }

        /// <summary>Primary and foreign key columns per table, computed once per table.</summary>
        private sealed class KeyLookup
        {
            private readonly IDatabaseMetadata metadata;
            private readonly Dictionary<int, KeyValuePair<HashSet<string>, HashSet<string>>> cache = new Dictionary<int, KeyValuePair<HashSet<string>, HashSet<string>>>();

            public KeyLookup(IDatabaseMetadata metadata)
            {
                this.metadata = metadata;
            }

            public KeyKind KeyOf(DbObject table, string column)
            {
                if (!cache.TryGetValue(table.Id, out var sets))
                {
                    sets = new KeyValuePair<HashSet<string>, HashSet<string>>(
                        new HashSet<string>(metadata.GetPrimaryKey(table), StringComparer.OrdinalIgnoreCase),
                        new HashSet<string>(
                            metadata.ForeignKeys.Where(k => k.ChildObjectId == table.Id).SelectMany(k => k.Columns.Select(c => c.Column)),
                            StringComparer.OrdinalIgnoreCase));
                    cache[table.Id] = sets;
                }

                return sets.Key.Contains(column) ? KeyKind.Primary : sets.Value.Contains(column) ? KeyKind.Foreign : KeyKind.None;
            }
        }

        private static Suggestion ColumnSuggestion(DbColumn column, string insertText, string suffix, string owner, int group, KeyKind key)
        {
            var type = column.TypeName.Length > 0 ? " " + column.TypeName + (column.IsNullable ? " null" : " not null") : string.Empty;
            return new Suggestion(SuggestionKind.Column, column.Name, insertText, column.Name, suffix, column.Name + type + " (" + owner + ")", group, null, type.Trim(), key);
        }
    }
}
