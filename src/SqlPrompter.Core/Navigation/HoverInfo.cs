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
        public HoverText(int start, int length, string text)
        {
            Start = start;
            Length = length;
            Text = text;
        }

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
            var text = Describe(names, sources, metadata);
            return text == null ? null : new HoverText(token.Start, (token.End - token.Start), text);
        }

        private static string Describe(IReadOnlyList<string> names, IReadOnlyList<TableSource> sources, IDatabaseMetadata metadata)
        {
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
                    return "Column " + qualifier + "." + column.Name + ": " + column.TypeName + (column.IsNullable ? " NULL" : " NOT NULL") + Flags(column) + Key(metadata, owner, column.Name) + "\nof " + owner.Schema + "." + owner.Name;
                }
            }
            else
            {
                var alias = sources.FirstOrDefault(s => s.Alias != null && string.Equals(s.Alias, last, StringComparison.OrdinalIgnoreCase));
                if (alias != null)
                {
                    var target = Readiness.Resolve(alias, metadata);
                    return target != null
                        ? "Alias " + last + " for " + Kind(target) + " " + target.Schema + "." + target.Name
                        : "Alias " + last + (alias.Kind == TableSourceKind.Object ? string.Empty : " for a subquery or a CTE");
                }

                // A column without a qualifier, when exactly one source has it.
                var owners = sources.Select(s => Readiness.Resolve(s, metadata)).Where(o => o != null).Distinct().ToList();
                var matches = owners.Select(o => new { Owner = o, Column = metadata.GetColumns(o)?.FirstOrDefault(c => string.Equals(c.Name, last, StringComparison.OrdinalIgnoreCase)) })
                    .Where(x => x.Column != null).ToList();
                if (matches.Count == 1 && metadata.FindObject(null, last) == null)
                {
                    var x = matches[0];
                    return "Column " + x.Column.Name + ": " + x.Column.TypeName + (x.Column.IsNullable ? " NULL" : " NOT NULL") + Flags(x.Column) + Key(metadata, x.Owner, x.Column.Name) + "\nof " + x.Owner.Schema + "." + x.Owner.Name;
                }
            }

            // An object: one to three parts, the schema and the name being the last two.
            var schema = names.Count >= 2 ? names[names.Count - 2] : null;
            var objectName = last;
            var dbObject = metadata.FindObject(schema, objectName);
            if (dbObject != null)
            {
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
                return "Procedure " + procedure.Schema + "." + procedure.Name + "\n(" + string.Join(", ", procedure.Parameters.Select(Parameter)) + ")";
            }

            return null;
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
