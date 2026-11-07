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
        public HoverText(int start, int length, string text, string descriptionSchema = null, string descriptionObject = null, string descriptionColumn = null)
        {
            Start = start;
            Length = length;
            Text = text;
            DescriptionSchema = descriptionSchema;
            DescriptionObject = descriptionObject;
            DescriptionColumn = descriptionColumn;
        }

        /// <summary>The database object (and column) whose description from the database can be added to the tooltip (#206); null when it is not an object of the database.</summary>
        public string DescriptionSchema { get; }

        public string DescriptionObject { get; }

        public string DescriptionColumn { get; }

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
        public static HoverText At(string batch, int offset, IDatabaseMetadata metadata)
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
            var text = Describe(names, sources, metadata, script, out var descriptionSchema, out var descriptionObject, out var descriptionColumn);
            return text == null ? null : new HoverText(token.Start, (token.End - token.Start), text, descriptionSchema, descriptionObject, descriptionColumn);
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
