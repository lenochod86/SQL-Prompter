using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>The text of the tooltip that appears when the mouse rests on a name (#220).</summary>
    public sealed class HoverText
    {
        public HoverText(int start, int length, string text, string descriptionSchema = null, string descriptionObject = null, string descriptionColumn = null, int linesInSsms = 0, string database = null)
        {
            Database = database;
            LinesInSsms = linesInSsms;
            Start = start;
            Length = length;
            Text = text;
            DescriptionSchema = descriptionSchema;
            DescriptionObject = descriptionObject;
            DescriptionColumn = descriptionColumn;
        }

        /// <summary>The database object (and column) whose description from the database can be added to the tooltip (#206); null when it is not an object of the database.</summary>
        /// <summary>The database of the object when it is not the one of the window (a three-part name, #443); null for the window's own database.</summary>
        public string Database { get; }

        public string DescriptionSchema { get; }

        public string DescriptionObject { get; }

        public string DescriptionColumn { get; }

        /// <summary>The number of leading lines of the text that the quick info of SSMS already shows itself (#422): the kind and name of a table or view (1), the kind, name and parameters of a procedure or function (2); the tooltip leaves them out.</summary>
        public int LinesInSsms { get; }

        /// <summary>The name under the mouse, as a span of the batch.</summary>
        public int Start { get; }

        public int Length { get; }

        public string Text { get; }
    }

    /// <summary>
    /// Tells what the name at a position is, from the metadata that is loaded and the sources of the statement (it never waits for the database):
    /// an alias and the table it stands for, a table or view with its columns and key, a column with its type, a procedure with its parameters,
    /// a function, a variable with its declaration. Null when nothing is known about the name.
    /// </summary>
    public static class HoverInfo
    {
        /// <param name="otherDatabase">The loaded metadata of another database of the server (null while it loads), for names such as <c>Db.dbo.Table</c> and aliases of them (#443).</param>
        public static HoverText At(string batch, int offset, IDatabaseMetadata metadata, Func<string, IDatabaseMetadata> otherDatabase = null)
        {
            if (string.IsNullOrEmpty(batch) || metadata == null)
            {
                return null;
            }

            var tokens = Lexer.Tokenize(batch);
            var index = -1;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Start <= offset && offset < tokens[i].End)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return null;
            }

            var token = tokens[index];
            if (token.Kind == TokenKind.Variable || token.Kind == TokenKind.Number || token.Kind == TokenKind.String)
            {
                // The name of a parameter in a call of a procedure (EXEC proc @p = 1), or a literal value that goes to one by position (#453).
                var parameter = ExecParameter(tokens, token, metadata, otherDatabase);
                if (parameter != null)
                {
                    return parameter;
                }
            }

            if (token.Kind == TokenKind.Variable)
            {
                var declaration = Declaration(tokens, batch, token);
                return declaration == null ? null : new HoverText(token.Start, (token.End - token.Start), declaration);
            }

            if (!token.IsNamePart || (token.Kind == TokenKind.Word && (Keywords.IsReserved(token.Text) || token.Text.StartsWith("#", StringComparison.Ordinal))))
            {
                return null;
            }

            // The dotted name that ends with this part.
            var parts = new List<Token> { token };
            var at = index;
            while (at >= 2 && tokens[at - 1].Kind == TokenKind.Dot && tokens[at - 2].IsNamePart && parts.Count < 4)
            {
                at -= 2;
                parts.Insert(0, tokens[at]);
            }

            var names = parts.Select(p => p.IdentifierValue).ToList();
            var sources = ContextAnalyzer.SourcesAt(batch, parts[0].Start);
            var script = ScriptModel.Build(batch, parts[0].Start);
            var database = ForeignDatabaseOf(names, sources, metadata);
            if (database != null)
            {
                // A table of another database: the tooltip comes from the metadata of that database, and says nothing while it loads.
                metadata = otherDatabase?.Invoke(database);
                if (metadata == null)
                {
                    return null;
                }
            }

            var text = Describe(names, sources, metadata, script, out var descriptionSchema, out var descriptionObject, out var descriptionColumn);
            var linesInSsms = 0;
            if (text != null && descriptionObject != null && descriptionColumn == null)
            {
                if (text.StartsWith("Procedure ", StringComparison.Ordinal) || text.StartsWith("Scalar function ", StringComparison.Ordinal) || text.StartsWith("Table function ", StringComparison.Ordinal))
                {
                    linesInSsms = 2;
                }
                else if (text.StartsWith("Table ", StringComparison.Ordinal) || text.StartsWith("View ", StringComparison.Ordinal))
                {
                    linesInSsms = 1;
                }
            }

            return text == null ? null : new HoverText(token.Start, (token.End - token.Start), text, descriptionSchema, descriptionObject, descriptionColumn, linesInSsms, database);
        }

        /// <summary>
        /// The tooltip of a parameter in <c>EXEC [@r =] proc @p = value, ...</c>: the name left of the equals sign, or a literal given by position
        /// before any named one. Null elsewhere (a variable on the right side keeps the tooltip of the variable).
        /// </summary>
        private static HoverText ExecParameter(IReadOnlyList<Token> all, Token token, IDatabaseMetadata metadata, Func<string, IDatabaseMetadata> otherDatabase)
        {
            var code = all.Where(t => t.Kind != TokenKind.Comment).ToList();
            var at = code.IndexOf(token);
            if (at < 0)
            {
                return null;
            }

            var bounds = StatementSplitter.Split(code);
            var start = 0;
            var end = code.Count;
            for (var b = 0; b < bounds.Count; b++)
            {
                if (bounds[b] <= at)
                {
                    start = bounds[b];
                    end = b + 1 < bounds.Count ? bounds[b + 1] : code.Count;
                }
            }

            if (!(code[start].IsWord("EXEC") || code[start].IsWord("EXECUTE")))
            {
                return null;
            }

            var i = start + 1;
            if (i + 1 < end && code[i].Kind == TokenKind.Variable && code[i + 1].Kind == TokenKind.Operator && code[i + 1].Text == "=")
            {
                i += 2;
            }

            var name = new List<string>();
            while (i < end && code[i].IsNamePart && name.Count < 4)
            {
                name.Add(code[i].IdentifierValue);
                if (i + 1 < end && code[i + 1].Kind == TokenKind.Dot)
                {
                    i += 2;
                }
                else
                {
                    i++;
                    break;
                }
            }

            if (name.Count == 0 || name.Count > 3 || at < i)
            {
                return null;
            }

            // The arguments split at the commas of the top level: which one the token is in and whether it starts it.
            var depth = 0;
            var position = 0;
            var named = false;
            var segmentStart = i;
            for (var k = i; k <= at; k++)
            {
                if (code[k].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (code[k].Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (depth == 0 && code[k].Kind == TokenKind.Comma)
                {
                    named |= IsNamedArgument(code, segmentStart, end);
                    position++;
                    segmentStart = k + 1;
                }
            }

            if (segmentStart != at || depth != 0)
            {
                return null;
            }

            var isNamed = IsNamedArgument(code, at, end);
            if (token.Kind == TokenKind.Variable && !isNamed)
            {
                return null;
            }

            if (token.Kind != TokenKind.Variable && (isNamed || named))
            {
                return null;
            }

            var procedureMetadata = metadata;
            if (name.Count == 3 && !string.Equals(name[0], metadata.DatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                procedureMetadata = otherDatabase?.Invoke(name[0]);
                if (procedureMetadata == null)
                {
                    return null;
                }
            }

            var procedureName = name[name.Count - 1];
            var schema = name.Count >= 2 ? name[name.Count - 2] : null;
            var candidates = procedureMetadata.Procedures.Where(p => string.Equals(p.Name, procedureName, StringComparison.OrdinalIgnoreCase) &&
                (schema == null || string.Equals(p.Schema, schema, StringComparison.OrdinalIgnoreCase))).ToList();
            var procedure = candidates.FirstOrDefault(p => string.Equals(p.Schema, procedureMetadata.DefaultSchema, StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
            if (procedure == null)
            {
                return null;
            }

            DbParameter parameter;
            var number = 0;
            if (isNamed)
            {
                number = procedure.Parameters.ToList().FindIndex(p => string.Equals(p.Name, token.Text, StringComparison.OrdinalIgnoreCase)) + 1;
                parameter = number > 0 ? procedure.Parameters[number - 1] : null;
            }
            else
            {
                number = position + 1;
                parameter = number <= procedure.Parameters.Count ? procedure.Parameters[number - 1] : null;
            }

            if (parameter == null)
            {
                return new HoverText(token.Start, token.End - token.Start,
                    (isNamed ? "No parameter " + token.Text : "No parameter " + number) + " in " + procedure + "\n(" + procedure.Parameters.Count + " parameters)",
                    database: procedureMetadata == metadata ? null : name[0]);
            }

            var details = parameter.TypeName + ", parameter " + number + " of " + procedure.Parameters.Count +
                (parameter.IsOutput ? ", OUTPUT" : string.Empty) +
                (parameter.HasDefault ? ", default = " + (parameter.DefaultText ?? "default") : ", required");
            return new HoverText(token.Start, token.End - token.Start, "parameter " + parameter.Name + " of " + procedure + "\n" + details,
                database: procedureMetadata == metadata ? null : name[0]);
        }

        /// <summary>The argument that starts at <paramref name="from"/> is <c>@name = value</c>.</summary>
        private static bool IsNamedArgument(List<Token> code, int from, int end) =>
            from + 1 < end && code[from].Kind == TokenKind.Variable && code[from + 1].Kind == TokenKind.Operator && code[from + 1].Text == "=";

        /// <summary>The database of the object a name stands for when it is not the one of the window: <c>Db.dbo.T</c>, <c>x.Col</c> or <c>x</c> for an alias of such a table.</summary>
        private static string ForeignDatabaseOf(IReadOnlyList<string> names, IReadOnlyList<TableSource> sources, IDatabaseMetadata metadata)
        {
            if (names.Count == 3)
            {
                return string.Equals(names[0], metadata.DatabaseName, StringComparison.OrdinalIgnoreCase) ? null : names[0];
            }

            if (names.Count > 2)
            {
                return null;
            }

            var qualifier = names[names.Count - (names.Count == 2 ? 2 : 1)];
            var source = sources.FirstOrDefault(s => string.Equals(s.ReferenceName, qualifier, StringComparison.OrdinalIgnoreCase));
            return source != null && Readiness.IsOtherDatabase(source, metadata) ? source.Database : null;
        }

        private static string Describe(IReadOnlyList<string> names, IReadOnlyList<TableSource> sources, IDatabaseMetadata metadata, ScriptModel script,
            out string dSchema, out string dObject, out string dColumn)
        {
            dSchema = null;
            dObject = null;
            dColumn = null;
            var last = names[names.Count - 1];
            if (names.Count >= 2)
            {
                // alias.column or table.column
                var qualifier = names[names.Count - 2];
                var source = sources.FirstOrDefault(s => string.Equals(s.ReferenceName, qualifier, StringComparison.OrdinalIgnoreCase));
                var owner = source == null ? null : Readiness.Resolve(source, metadata);
                var columns = owner == null ? null : metadata.GetColumns(owner);
                var column = columns?.FirstOrDefault(c => string.Equals(c.Name, last, StringComparison.OrdinalIgnoreCase));
                if (column != null)
                {
                    dSchema = owner.Schema;
                    dObject = owner.Name;
                    dColumn = column.Name;
                    return "Column " + qualifier + "." + column.Name + ": " + column.TypeName + (column.IsNullable ? " NULL" : " NOT NULL") + Flags(column) + Key(metadata, owner, column.Name) + "\nof " + owner.Schema + "." + owner.Name;
                }

                // A column of a CTE, a derived table, a temporary table or a table variable of the script.
                if (source != null && owner == null)
                {
                    var scripted = script.Resolve(source, metadata);
                    var scriptedColumn = scripted?.Columns.FirstOrDefault(c => string.Equals(c.Name, last, StringComparison.OrdinalIgnoreCase));
                    if (scriptedColumn != null)
                    {
                        return "Column " + qualifier + "." + scriptedColumn.Name + (scriptedColumn.TypeName.Length > 0 ? ": " + scriptedColumn.TypeName : string.Empty) + "\nof " + ScriptSourceName(source, script);
                    }
                }
            }
            else
            {
                var alias = sources.FirstOrDefault(s => s.Alias != null && string.Equals(s.Alias, last, StringComparison.OrdinalIgnoreCase));
                if (alias != null)
                {
                    var target = Readiness.Resolve(alias, metadata);
                    if (target != null)
                    {
                        dSchema = target.Schema;
                        dObject = target.Name;
                        return "Alias " + last + " for " + Kind(target) + " " + target.Schema + "." + target.Name;
                    }

                    var isScripted = alias.Kind != TableSourceKind.Object || script.Tables.Any(t => string.Equals(t.Name, alias.ObjectName, StringComparison.OrdinalIgnoreCase));
                    return isScripted ? "Alias " + last + " for " + ScriptSourceName(alias, script) + ScriptColumnList(alias, script, metadata) : "Alias " + last;
                }

                // A column without a qualifier, when exactly one source has it.
                var owners = sources.Select(s => Readiness.Resolve(s, metadata)).Where(o => o != null).Distinct().ToList();
                var matches = owners.Select(o => new { Owner = o, Column = metadata.GetColumns(o)?.FirstOrDefault(c => string.Equals(c.Name, last, StringComparison.OrdinalIgnoreCase)) })
                    .Where(x => x.Column != null).ToList();
                if (matches.Count == 1 && metadata.FindObject(null, last) == null)
                {
                    var x = matches[0];
                    dSchema = x.Owner.Schema;
                    dObject = x.Owner.Name;
                    dColumn = x.Column.Name;
                    return "Column " + x.Column.Name + ": " + x.Column.TypeName + (x.Column.IsNullable ? " NULL" : " NOT NULL") + Flags(x.Column) + Key(metadata, x.Owner, x.Column.Name) + "\nof " + x.Owner.Schema + "." + x.Owner.Name;
                }
            }

            // The name of a CTE, a temporary table or a table variable of the script, used as a source.
            if (names.Count == 1)
            {
                var scriptSource = sources.FirstOrDefault(s => s.Alias == null && string.Equals(s.ReferenceName, last, StringComparison.OrdinalIgnoreCase));
                var scriptTable = script.Tables.FirstOrDefault(t => string.Equals(t.Name, last, StringComparison.OrdinalIgnoreCase));
                if (scriptTable != null)
                {
                    return char.ToUpperInvariant(scriptTable.Description[0]) + scriptTable.Description.Substring(1) + (scriptSource != null ? ScriptColumnList(scriptSource, script, metadata) : string.Empty);
                }
            }

            // An object: one to three parts, the schema and the name being the last two.
            var schema = names.Count >= 2 ? names[names.Count - 2] : null;
            var objectName = last;
            var dbObject = metadata.FindObject(schema, objectName);
            if (dbObject != null)
            {
                dSchema = dbObject.Schema;
                dObject = dbObject.Name;
                var target = Readiness.FollowSynonym(dbObject, metadata);
                var head = Kind(dbObject) + " " + dbObject.Schema + "." + dbObject.Name;
                if (dbObject.Kind == DbObjectKind.Synonym && target != null && !ReferenceEquals(target, dbObject))
                {
                    head += " -> " + target.Schema + "." + target.Name;
                }

                var columns = metadata.GetColumns(target ?? dbObject);
                if (columns != null && (dbObject.Kind == DbObjectKind.Table || dbObject.Kind == DbObjectKind.View || dbObject.Kind == DbObjectKind.Synonym))
                {
                    var key = metadata.GetPrimaryKey(target ?? dbObject);
                    return head + "\n" + columns.Count + " columns" + (key.Count > 0 ? ", primary key (" + string.Join(", ", key) + ")" : string.Empty);
                }

                if (dbObject.Kind == DbObjectKind.TableFunction || dbObject.Kind == DbObjectKind.ScalarFunction)
                {
                    var parameters = metadata.GetFunctionParameters(dbObject);
                    return head + "\n(" + string.Join(", ", parameters.Select(Parameter)) + ")";
                }

                return head;
            }

            var procedure = metadata.Procedures.FirstOrDefault(p => string.Equals(p.Name, objectName, StringComparison.OrdinalIgnoreCase) &&
                (schema == null || string.Equals(p.Schema, schema, StringComparison.OrdinalIgnoreCase)));
            if (procedure != null)
            {
                dSchema = procedure.Schema;
                dObject = procedure.Name;
                return "Procedure " + procedure.Schema + "." + procedure.Name + "\n(" + string.Join(", ", procedure.Parameters.Select(Parameter)) + ")";
            }

            return null;
        }

        /// <summary>What a source of the script is: a subquery, a CTE, a temporary table or a table variable.</summary>
        private static string ScriptSourceName(TableSource source, ScriptModel script)
        {
            if (source.Kind == TableSourceKind.Derived)
            {
                return "a subquery";
            }

            var table = script.Tables.FirstOrDefault(t => string.Equals(t.Name, source.ObjectName, StringComparison.OrdinalIgnoreCase));
            return table != null ? table.Description : source.Kind == TableSourceKind.TempTable ? "a temporary table" : "a table variable";
        }

        /// <summary>The column names of a script source on a new line, at most twelve.</summary>
        private static string ScriptColumnList(TableSource source, ScriptModel script, IDatabaseMetadata metadata)
        {
            var columns = script.Resolve(source, metadata)?.Columns;
            if (columns == null || columns.Count == 0)
            {
                return string.Empty;
            }

            return "\ncolumns: " + string.Join(", ", columns.Take(12).Select(c => c.Name)) + (columns.Count > 12 ? ", ... (" + columns.Count + ")" : string.Empty);
        }

        private static string Parameter(DbParameter p) => p.Name + " " + p.TypeName + (p.IsOutput ? " OUTPUT" : string.Empty) + (p.HasDefault ? " = " + (p.DefaultText ?? "default") : string.Empty);

        private static string Flags(DbColumn column) =>
            (column.IsIdentity ? ", identity" : string.Empty) + (column.IsComputed ? ", computed" : string.Empty) + (column.IsRowVersion ? ", rowversion" : string.Empty);

        private static string Key(IDatabaseMetadata metadata, DbObject owner, string column) =>
            metadata.GetPrimaryKey(owner).Contains(column, StringComparer.OrdinalIgnoreCase) ? ", primary key" : string.Empty;

        private static string Kind(DbObject o)
        {
            switch (o.Kind)
            {
                case DbObjectKind.Table: return "Table";
                case DbObjectKind.View: return "View";
                case DbObjectKind.TableFunction: return "Table function";
                case DbObjectKind.ScalarFunction: return "Scalar function";
                default: return "Synonym";
            }
        }

        /// <summary>DECLARE @x type ... : the text from the variable to the next comma, equals sign or end of the statement.</summary>
        private static string Declaration(IReadOnlyList<Token> tokens, string batch, Token variable)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("DECLARE"))
                {
                    continue;
                }

                for (var k = i + 1; k < tokens.Count && tokens[k].Kind != TokenKind.Semicolon && !(k > i + 1 && (tokens[k].IsWord("SELECT") || tokens[k].IsWord("SET") || tokens[k].IsWord("INSERT") || tokens[k].IsWord("UPDATE") || tokens[k].IsWord("DELETE"))); k++)
                {
                    if (tokens[k].Kind == TokenKind.Variable && string.Equals(tokens[k].Text, variable.Text, StringComparison.OrdinalIgnoreCase) && (k == i + 1 || tokens[k - 1].Kind == TokenKind.Comma))
                    {
                        var end = k + 1;
                        var depth = 0;
                        while (end < tokens.Count && !(depth == 0 && (tokens[end].Kind == TokenKind.Comma || tokens[end].Kind == TokenKind.Semicolon || (tokens[end].Kind == TokenKind.Operator && tokens[end].Text == "="))))
                        {
                            if (tokens[end].Kind == TokenKind.OpenParen)
                            {
                                depth++;
                            }
                            else if (tokens[end].Kind == TokenKind.CloseParen)
                            {
                                depth--;
                            }

                            end++;
                        }

                        if (end == k + 1)
                        {
                            return null;
                        }

                        var type = batch.Substring(tokens[k + 1].Start, tokens[end - 1].End - tokens[k + 1].Start).Trim();
                        return "Variable " + variable.Text + ": " + (type.StartsWith("AS ", StringComparison.OrdinalIgnoreCase) ? type.Substring(3) : type);
                    }
                }
            }

            return null;
        }
    }
}
