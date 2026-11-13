using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;

namespace SqlPrompter.Core.Analysis
{
    public enum ScriptTableKind
    {
        /// <summary>Common table expression: WITH name AS (SELECT …).</summary>
        Cte,

        /// <summary>#table from CREATE TABLE or SELECT … INTO.</summary>
        TempTable,

        /// <summary>DECLARE @t TABLE (…).</summary>
        TableVariable,
    }

    /// <summary>Variable or procedure/function parameter defined in the script.</summary>
    public sealed class ScriptVariable
    {
        public ScriptVariable(string name, string typeName, bool isParameter)
        {
            Name = name;
            TypeName = typeName ?? string.Empty;
            IsParameter = isParameter;
        }

        /// <summary>Name including the "@".</summary>
        public string Name { get; }

        /// <summary>Type as written in the script, e.g. varchar(50); empty if unknown.</summary>
        public string TypeName { get; }

        public bool IsParameter { get; }

        public override string ToString() => Name + " " + TypeName;
    }

    /// <summary>A row source defined in the script: CTE, temporary table or table variable.</summary>
    public sealed class ScriptTable
    {
        internal ScriptTable(ScriptTableKind kind, string name, int position)
        {
            Kind = kind;
            Name = name;
            Position = position;
        }

        public ScriptTableKind Kind { get; }

        /// <summary>Name as used in the query: cte, #temp or @variable.</summary>
        public string Name { get; }

        /// <summary>Position of the name in the batch.</summary>
        public int Position { get; }

        public string Description
        {
            get
            {
                switch (Kind)
                {
                    case ScriptTableKind.Cte: return "common table expression " + Name;
                    case ScriptTableKind.TempTable: return "temporary table " + Name;
                    default: return "table variable " + Name;
                }
            }
        }

        /// <summary>Columns declared explicitly (CREATE TABLE, DECLARE … TABLE), otherwise null.</summary>
        internal IReadOnlyList<DbColumn> Declared { get; set; }

        /// <summary>Column names of a CTE, WITH name (a, b) AS (…), otherwise null.</summary>
        internal IReadOnlyList<string> ColumnNames { get; set; }

        /// <summary>Token range of the defining query: the select starts at From, its list ends at ItemsTo, its sources at SourcesTo.</summary>
        internal int From { get; set; } = -1;

        internal int ItemsTo { get; set; }

        internal int SourcesTo { get; set; }

        public override string ToString() => Description;
    }

    /// <summary>Columns of a script-defined row source.</summary>
    public sealed class ScriptColumns
    {
        internal ScriptColumns(IReadOnlyList<DbColumn> columns, IReadOnlyList<DbObject> missingColumns, bool isComplete)
        {
            Columns = columns;
            MissingColumns = missingColumns;
            IsComplete = isComplete;
        }

        public IReadOnlyList<DbColumn> Columns { get; }

        /// <summary>Database objects whose columns are needed for the result (e.g. SELECT * FROM dbo.Users) but not loaded yet.</summary>
        public IReadOnlyList<DbObject> MissingColumns { get; }

        /// <summary>False if some source could not be resolved, so the columns may be incomplete.</summary>
        public bool IsComplete { get; }
    }

    /// <summary>
    /// Definitions found in one batch before the caret: CTEs, temporary tables, table variables,
    /// variables and parameters. Built from text only, so it can be computed on a background thread;
    /// columns that depend on the database (SELECT *) are resolved against metadata on request.
    /// </summary>
    public sealed class ScriptModel
    {
        private const int MaxDepth = 8;

        public static readonly ScriptModel Empty = new ScriptModel(string.Empty, new List<Token>(), new List<ScriptTable>(), new List<ScriptVariable>());

        private readonly string text;
        private readonly List<Token> tokens;

        private ScriptModel(string text, List<Token> tokens, List<ScriptTable> tables, List<ScriptVariable> variables)
        {
            this.text = text;
            this.tokens = tokens;
            Tables = tables;
            Variables = variables;
        }

        /// <summary>CTEs of the statement at the caret, temporary tables and table variables defined before it, in script order.</summary>
        public IReadOnlyList<ScriptTable> Tables { get; }

        /// <summary>Variables and parameters declared before the caret; a later declaration of the same name wins.</summary>
        public IReadOnlyList<ScriptVariable> Variables { get; }

        public static ScriptModel Build(string text, int caret)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Empty;
            }

            return new Builder(text, caret).Build();
        }

        /// <summary>
        /// Columns of a CTE, temporary table, table variable or derived table; null if the source is not defined in the script.
        /// Metadata is optional: without it only columns that need no database lookup are returned.
        /// </summary>
        public ScriptColumns Resolve(TableSource source, IDatabaseMetadata metadata) => Resolve(source, metadata, 0);

        private ScriptColumns Resolve(TableSource source, IDatabaseMetadata metadata, int depth)
        {
            if (tokens.Count == 0)
            {
                return null; // no script model: nothing is defined in the script
            }

            ScriptTable table = null;
            switch (source.Kind)
            {
                case TableSourceKind.Derived:
                    if (source.ColumnNames != null)
                    {
                        return Names(source.ColumnNames);
                    }

                    if (source.BodyStart < 0)
                    {
                        return null;
                    }

                    var from = FirstTokenAtOrAfter(source.BodyStart);
                    var to = FirstTokenAtOrAfter(source.BodyEnd);
                    return ResolveQuery(from, to, to, metadata, depth);
                case TableSourceKind.Variable:
                    table = Find(ScriptTableKind.TableVariable, source.ObjectName);
                    break;
                case TableSourceKind.TempTable:
                    table = Find(ScriptTableKind.TempTable, source.ObjectName);
                    break;
                default:
                    if (source.NameParts.Count == 1)
                    {
                        table = Find(ScriptTableKind.Cte, source.ObjectName);
                    }

                    break;
            }

            return table == null ? null : ResolveTable(table, metadata, depth);
        }

        /// <summary>Columns of a table defined in the script.</summary>
        public ScriptColumns Resolve(ScriptTable table, IDatabaseMetadata metadata) => ResolveTable(table, metadata, 0);

        private ScriptColumns ResolveTable(ScriptTable table, IDatabaseMetadata metadata, int depth)
        {
            if (table.Declared != null)
            {
                return new ScriptColumns(table.Declared, Array.Empty<DbObject>(), true);
            }

            if (table.ColumnNames != null)
            {
                return Names(table.ColumnNames);
            }

            return table.From >= 0
                ? ResolveQuery(table.From, table.ItemsTo, table.SourcesTo, metadata, depth)
                : new ScriptColumns(Array.Empty<DbColumn>(), Array.Empty<DbObject>(), false);
        }

        private static ScriptColumns Names(IEnumerable<string> names) =>
            new ScriptColumns(names.Select(n => new DbColumn(n, string.Empty, true)).ToList(), Array.Empty<DbObject>(), true);

        private ScriptTable Find(ScriptTableKind kind, string name)
        {
            for (var i = Tables.Count - 1; i >= 0; i--)
            {
                if (Tables[i].Kind == kind && string.Equals(Tables[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Tables[i];
                }
            }

            return null;
        }

        private int FirstTokenAtOrAfter(int offset)
        {
            int low = 0, high = tokens.Count;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (tokens[mid].Start < offset)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        /// <summary>Columns of the first SELECT in tokens [from, to): named items plus expanded stars.</summary>
        private ScriptColumns ResolveQuery(int from, int itemsTo, int sourcesTo, IDatabaseMetadata metadata, int depth)
        {
            var columns = new List<DbColumn>();
            var missing = new List<DbObject>();
            var complete = true;
            if (depth > MaxDepth || from < 0 || from >= tokens.Count)
            {
                return new ScriptColumns(columns, missing, false);
            }

            var limit = Math.Min(Math.Max(itemsTo, sourcesTo), tokens.Count);
            var s = new ContextAnalyzer.Structure(tokens, from, limit);
            var select = -1;
            for (var i = from; i < limit; i++)
            {
                if (s.Depth[i] == 0 && tokens[i].IsWord("SELECT"))
                {
                    select = i;
                    break;
                }
            }

            if (select < 0)
            {
                return new ScriptColumns(columns, missing, false);
            }

            // Only the first block of a UNION names the columns.
            var blockEnd = limit;
            for (var i = select + 1; i < limit; i++)
            {
                var word = s.Depth[i] == 0 ? tokens[i].Upper : null;
                if (word == "UNION" || word == "EXCEPT" || word == "INTERSECT")
                {
                    blockEnd = i;
                    break;
                }
            }

            var items = ParseItems(s, select + 1, Math.Min(itemsTo, blockEnd));
            List<TableSource> sources = null;
            foreach (var item in items)
            {
                if (!item.IsStar)
                {
                    columns.Add(new DbColumn(item.Name, string.Empty, true));
                    continue;
                }

                if (sources == null)
                {
                    sources = ContextAnalyzer.ParseSources(s, select, Math.Min(sourcesTo, blockEnd), 0, 0);
                }

                var matching = sources.Where(src => item.Qualifier.Length == 0 ||
                    string.Equals(src.Alias, item.Qualifier, StringComparison.OrdinalIgnoreCase) ||
                    (src.Alias == null && string.Equals(src.ObjectName, item.Qualifier, StringComparison.OrdinalIgnoreCase))).ToList();
                if (matching.Count == 0)
                {
                    complete = false;
                }

                foreach (var source in matching)
                {
                    complete &= AddSourceColumns(source, metadata, depth + 1, columns, missing);
                }
            }

            return new ScriptColumns(columns, missing, complete);
        }

        private bool AddSourceColumns(TableSource source, IDatabaseMetadata metadata, int depth, List<DbColumn> columns, List<DbObject> missing)
        {
            var scripted = Resolve(source, metadata, depth);
            if (scripted != null)
            {
                columns.AddRange(scripted.Columns);
                missing.AddRange(scripted.MissingColumns.Where(m => !missing.Contains(m)));
                return scripted.IsComplete;
            }

            var dbObject = Readiness.Resolve(source, metadata);
            var loaded = dbObject != null ? metadata.GetColumns(dbObject) : null;
            if (loaded == null)
            {
                if (dbObject != null && !missing.Contains(dbObject))
                {
                    missing.Add(dbObject);
                }

                return false;
            }

            columns.AddRange(loaded);
            return true;
        }

        private struct QueryItem
        {
            public string Name;
            public bool IsStar;
            public string Qualifier;
        }

        private static readonly HashSet<string> ListEnd = new HashSet<string>(StringComparer.Ordinal)
        {
            "FROM", "INTO", "WHERE", "GROUP", "HAVING", "ORDER", "UNION", "EXCEPT", "INTERSECT", "FOR", "OPTION", "WINDOW",
        };

        /// <summary>Splits the select list after SELECT in [from, to) and names its items.</summary>
        private List<QueryItem> ParseItems(ContextAnalyzer.Structure s, int from, int to)
        {
            var items = new List<QueryItem>();
            var i = from;
            if (i < to && (tokens[i].IsWord("DISTINCT") || tokens[i].IsWord("ALL")))
            {
                i++;
            }

            if (i < to && tokens[i].IsWord("TOP"))
            {
                i++;
                if (i < to && tokens[i].Kind == TokenKind.OpenParen)
                {
                    i = s.Match[i] >= 0 ? s.Match[i] + 1 : to;
                }
                else if (i < to)
                {
                    i++;
                }

                if (i < to && tokens[i].IsWord("PERCENT"))
                {
                    i++;
                }

                if (i + 1 < to && tokens[i].IsWord("WITH") && tokens[i + 1].IsWord("TIES"))
                {
                    i += 2;
                }
            }

            var itemStart = i;
            for (; i <= to; i++)
            {
                var atEnd = i >= to || (s.Depth[i] == 0 && (tokens[i].Kind == TokenKind.Comma || ListEnd.Contains(tokens[i].Upper ?? string.Empty)));
                if (!atEnd)
                {
                    continue;
                }

                AddItem(items, itemStart, i);
                if (i >= to || tokens[i].Kind != TokenKind.Comma)
                {
                    break;
                }

                itemStart = i + 1;
            }

            return items;
        }

        private void AddItem(List<QueryItem> items, int a, int b)
        {
            var n = b - a;
            if (n <= 0)
            {
                return;
            }

            var last = tokens[b - 1];
            if (last.Kind == TokenKind.Operator && last.Text == "*")
            {
                if (n == 1)
                {
                    items.Add(new QueryItem { IsStar = true, Qualifier = string.Empty });
                }
                else if (n >= 3 && tokens[b - 2].Kind == TokenKind.Dot && tokens[b - 3].IsNamePart)
                {
                    items.Add(new QueryItem { IsStar = true, Qualifier = tokens[b - 3].IdentifierValue });
                }

                return;
            }

            // alias = expression
            if (n >= 3 && tokens[a + 1].Kind == TokenKind.Operator && tokens[a + 1].Text == "=")
            {
                if (tokens[a].IsNamePart)
                {
                    items.Add(new QueryItem { Name = tokens[a].IdentifierValue });
                }

                return;
            }

            // expression AS alias
            if (n >= 3 && tokens[b - 2].IsWord("AS") && (last.IsNamePart || last.Kind == TokenKind.String))
            {
                items.Add(new QueryItem { Name = last.Kind == TokenKind.String ? last.Text.Trim('\'') : last.IdentifierValue });
                return;
            }

            // column, alias.column, schema.alias.column
            if (n % 2 == 1 && IsDottedName(a, b) && last.IsIdentifier)
            {
                items.Add(new QueryItem { Name = last.IdentifierValue });
                return;
            }

            // expression alias
            if (n >= 2 && last.IsIdentifier && ContextAnalyzer.IsOperand(tokens[b - 2]))
            {
                items.Add(new QueryItem { Name = last.IdentifierValue });
            }
        }

        private bool IsDottedName(int a, int b)
        {
            for (var i = a; i < b; i++)
            {
                if ((i - a) % 2 == 0 ? !tokens[i].IsNamePart : tokens[i].Kind != TokenKind.Dot)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class Builder
        {
            private static readonly HashSet<string> ConstraintWords = new HashSet<string>(StringComparer.Ordinal)
            {
                "CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "INDEX", "PERIOD", "CLUSTERED", "NONCLUSTERED",
            };

            private readonly string text;
            private readonly int caret;
            private readonly List<ScriptTable> tables = new List<ScriptTable>();
            private readonly List<ScriptVariable> variables = new List<ScriptVariable>();
            private List<Token> tokens;
            private int[] depth;
            private int[] match;

            public Builder(string text, int caret)
            {
                this.text = text;
                this.caret = caret;
            }

            public ScriptModel Build()
            {
                tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.Comment).ToList();
                depth = new int[tokens.Count];
                match = Enumerable.Repeat(-1, tokens.Count).ToArray();
                var bounds = StatementSplitter.Split(tokens);
                for (var b = 0; b < bounds.Count; b++)
                {
                    var start = bounds[b];
                    var end = b + 1 < bounds.Count ? bounds[b + 1] : tokens.Count;
                    if (tokens[start].Start >= caret)
                    {
                        break;
                    }

                    _ = new ContextAnalyzer.Structure(tokens, start, end, depth, match); // fills depth and match for the statement
                    var containsCaret = end >= tokens.Count || caret <= tokens[end].Start;
                    ProcessStatement(start, end, containsCaret);
                }

                // A later declaration of the same variable replaces the earlier one.
                var unique = new List<ScriptVariable>();
                foreach (var variable in variables)
                {
                    unique.RemoveAll(v => string.Equals(v.Name, variable.Name, StringComparison.OrdinalIgnoreCase));
                    unique.Add(variable);
                }

                return new ScriptModel(text, tokens, tables, unique);
            }

            private void ProcessStatement(int start, int end, bool containsCaret)
            {
                var main = start;
                while (main < end && tokens[main].Kind == TokenKind.Semicolon)
                {
                    main++;
                }

                if (main >= end)
                {
                    return;
                }

                if (tokens[main].IsWord("WITH") && main + 1 < end && tokens[main + 1].Kind != TokenKind.OpenParen)
                {
                    main = ParseCtes(main, end, containsCaret);
                    if (main >= end)
                    {
                        return;
                    }
                }

                switch (tokens[main].Upper)
                {
                    case "DECLARE":
                        ParseDeclare(main, end);
                        break;
                    case "CREATE":
                    case "ALTER":
                        ParseCreate(main, end);
                        break;
                    case "SELECT":
                        ParseSelectInto(main, end);
                        break;
                }
            }

            /// <summary>Reads WITH a AS (…), b (x, y) AS (…) and returns the index of the statement after the list.</summary>
            private int ParseCtes(int with, int end, bool containsCaret)
            {
                var i = with + 1;
                while (i < end && tokens[i].IsNamePart)
                {
                    var name = tokens[i];
                    i++;
                    List<string> names = null;
                    if (i < end && tokens[i].Kind == TokenKind.OpenParen)
                    {
                        var close = match[i];
                        if (close < 0)
                        {
                            return end;
                        }

                        names = new List<string>();
                        for (var k = i + 1; k < close; k++)
                        {
                            if (tokens[k].IsNamePart)
                            {
                                names.Add(tokens[k].IdentifierValue);
                            }
                        }

                        i = close + 1;
                    }

                    if (i >= end || !tokens[i].IsWord("AS") || i + 1 >= end || tokens[i + 1].Kind != TokenKind.OpenParen)
                    {
                        return end;
                    }

                    var open = i + 1;
                    var bodyEnd = match[open] >= 0 ? match[open] : end;
                    if (containsCaret && name.Start < caret)
                    {
                        tables.Add(new ScriptTable(ScriptTableKind.Cte, name.IdentifierValue, name.Start)
                        {
                            ColumnNames = names,
                            From = open + 1,
                            ItemsTo = bodyEnd,
                            SourcesTo = bodyEnd,
                        });
                    }

                    if (match[open] < 0)
                    {
                        return end;
                    }

                    i = match[open] + 1;
                    if (i < end && tokens[i].Kind == TokenKind.Comma)
                    {
                        i++;
                        continue;
                    }

                    break;
                }

                return i;
            }

            private void ParseDeclare(int declare, int end)
            {
                var i = declare + 1;
                while (i < end && tokens[i].Kind == TokenKind.Variable)
                {
                    var name = tokens[i];
                    i++;
                    if (i < end && tokens[i].IsWord("AS"))
                    {
                        i++;
                    }

                    if (i < end && tokens[i].IsWord("TABLE"))
                    {
                        i++;
                        if (i < end && tokens[i].Kind == TokenKind.OpenParen)
                        {
                            var close = match[i] >= 0 ? match[i] : end;
                            if (name.Start < caret)
                            {
                                tables.Add(new ScriptTable(ScriptTableKind.TableVariable, name.Text, name.Start)
                                {
                                    Declared = ParseColumnDefinitions(i + 1, close),
                                });
                            }

                            i = match[i] >= 0 ? match[i] + 1 : end;
                        }
                    }
                    else
                    {
                        var typeEnd = ScanType(i, end);
                        if (name.Start < caret)
                        {
                            variables.Add(new ScriptVariable(name.Text, TypeText(i, typeEnd), false));
                        }

                        i = SkipToComma(typeEnd, end);
                    }

                    if (i < end && tokens[i].Kind == TokenKind.Comma)
                    {
                        i++;
                        continue;
                    }

                    break;
                }
            }

            private void ParseCreate(int create, int end)
            {
                var i = create + 1;
                if (i + 1 < end && tokens[i].IsWord("OR") && tokens[i + 1].IsWord("ALTER"))
                {
                    i += 2;
                }

                if (i >= end)
                {
                    return;
                }

                if (tokens[i].IsWord("TABLE"))
                {
                    i++;
                    if (i + 1 < end && tokens[i].Kind == TokenKind.Word && tokens[i].Text.StartsWith("#", StringComparison.Ordinal) &&
                        tokens[i + 1].Kind == TokenKind.OpenParen)
                    {
                        var name = tokens[i];
                        var open = i + 1;
                        if (name.Start < caret)
                        {
                            tables.Add(new ScriptTable(ScriptTableKind.TempTable, name.Text, name.Start)
                            {
                                Declared = ParseColumnDefinitions(open + 1, match[open] >= 0 ? match[open] : end),
                            });
                        }
                    }

                    return;
                }

                if (tokens[i].IsWord("PROC") || tokens[i].IsWord("PROCEDURE") || tokens[i].IsWord("FUNCTION"))
                {
                    ParseParameters(i + 1, end);
                }
            }

            private void ParseParameters(int i, int end)
            {
                while (i < end && (tokens[i].IsNamePart || tokens[i].Kind == TokenKind.Dot))
                {
                    i++;
                }

                var limit = end;
                if (i < end && tokens[i].Kind == TokenKind.OpenParen)
                {
                    limit = match[i] >= 0 ? match[i] : end;
                    i++;
                }

                while (i < limit && tokens[i].Kind == TokenKind.Variable)
                {
                    var name = tokens[i];
                    i++;
                    if (i < limit && tokens[i].IsWord("AS"))
                    {
                        i++;
                    }

                    var typeEnd = ScanType(i, limit);
                    if (name.Start < caret)
                    {
                        variables.Add(new ScriptVariable(name.Text, TypeText(i, typeEnd), true));
                    }

                    i = SkipToComma(typeEnd, limit);
                    if (i < limit && tokens[i].Kind == TokenKind.Comma)
                    {
                        i++;
                        continue;
                    }

                    break;
                }
            }

            /// <summary>SELECT … INTO #table FROM …</summary>
            private void ParseSelectInto(int select, int end)
            {
                for (var i = select + 1; i < end; i++)
                {
                    if (depth[i] != 0)
                    {
                        continue;
                    }

                    if (tokens[i].IsWord("FROM"))
                    {
                        return;
                    }

                    if (tokens[i].IsWord("INTO"))
                    {
                        if (i + 1 < end && tokens[i + 1].Kind == TokenKind.Word &&
                            tokens[i + 1].Text.StartsWith("#", StringComparison.Ordinal) && tokens[i + 1].Start < caret)
                        {
                            tables.Add(new ScriptTable(ScriptTableKind.TempTable, tokens[i + 1].Text, tokens[i + 1].Start)
                            {
                                From = select,
                                ItemsTo = i,
                                SourcesTo = end,
                            });
                        }

                        return;
                    }
                }
            }

            /// <summary>Column definitions of CREATE TABLE and DECLARE … TABLE between the parentheses [from, to).</summary>
            private List<DbColumn> ParseColumnDefinitions(int from, int to)
            {
                var columns = new List<DbColumn>();
                var i = from;
                while (i < to)
                {
                    var k = i;
                    while (k < to && tokens[k].Kind != TokenKind.Comma)
                    {
                        k = tokens[k].Kind == TokenKind.OpenParen && match[k] >= 0 && match[k] < to ? match[k] + 1 : k + 1;
                    }

                    AddColumnDefinition(columns, i, k);
                    i = k + 1;
                }

                return columns;
            }

            private void AddColumnDefinition(List<DbColumn> columns, int a, int b)
            {
                if (a >= b || !tokens[a].IsNamePart || (tokens[a].Kind == TokenKind.Word && ConstraintWords.Contains(tokens[a].Upper)))
                {
                    return;
                }

                var type = string.Empty;
                var typeEnd = a + 1;
                if (a + 1 < b && !tokens[a + 1].IsWord("AS"))
                {
                    typeEnd = ScanType(a + 1, b);
                    type = TypeText(a + 1, typeEnd);
                }

                var nullable = true;
                var identity = false;
                for (var i = typeEnd; i < b; i++)
                {
                    if (i + 1 < b && tokens[i].IsWord("NOT") && tokens[i + 1].IsWord("NULL"))
                    {
                        nullable = false;
                    }

                    if (tokens[i].IsWord("IDENTITY"))
                    {
                        identity = true;
                    }
                }

                // name AS expression is a computed column; a rowversion column cannot be inserted either.
                var computed = a + 1 < b && tokens[a + 1].IsWord("AS");
                var rowVersion = type.Equals("timestamp", StringComparison.OrdinalIgnoreCase) || type.Equals("rowversion", StringComparison.OrdinalIgnoreCase);
                columns.Add(new DbColumn(tokens[a].IdentifierValue, type, nullable, isIdentity: identity, isComputed: computed, isRowVersion: rowVersion));
            }

            /// <summary>Returns the index after a data type starting at i: name, optional schema, optional (length).</summary>
            private int ScanType(int i, int limit)
            {
                if (i >= limit || !tokens[i].IsNamePart)
                {
                    return i;
                }

                i++;
                while (i + 1 < limit && tokens[i].Kind == TokenKind.Dot && tokens[i + 1].IsNamePart)
                {
                    i += 2;
                }

                if (i < limit && tokens[i].Kind == TokenKind.OpenParen)
                {
                    i = match[i] >= 0 && match[i] < limit ? match[i] + 1 : limit;
                }

                return i;
            }

            private string TypeText(int from, int to) =>
                to > from ? text.Substring(tokens[from].Start, tokens[to - 1].End - tokens[from].Start) : string.Empty;

            /// <summary>Skips a default value and modifiers up to the next comma at the same level (or limit).</summary>
            private int SkipToComma(int i, int limit)
            {
                while (i < limit && tokens[i].Kind != TokenKind.Comma && !tokens[i].IsWord("AS"))
                {
                    i = tokens[i].Kind == TokenKind.OpenParen && match[i] >= 0 ? match[i] + 1 : i + 1;
                }

                return i;
            }
        }
    }
}
