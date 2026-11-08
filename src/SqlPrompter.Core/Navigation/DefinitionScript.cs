using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>A column as it appears in a CREATE TABLE script.</summary>
    public sealed class ScriptTableColumn
    {
        public ScriptTableColumn(string name, string typeText, bool isNullable, string identityText)
        {
            Name = name;
            TypeText = typeText;
            IsNullable = isNullable;
            IdentityText = identityText;
        }

        public string Name { get; }

        /// <summary>Type with its size, e.g. nvarchar(50) or decimal(18,2).</summary>
        public string TypeText { get; }

        public bool IsNullable { get; }

        /// <summary>"IDENTITY(1,1)" for an identity column, otherwise null.</summary>
        public string IdentityText { get; }

        /// <summary>The collation of a character column when it differs from the one of the database; null otherwise.</summary>
        public string Collation { get; set; }

        /// <summary>The expression of a computed column (as stored, with its parentheses); null for an ordinary column.</summary>
        public string ComputedText { get; set; }

        public bool IsPersisted { get; set; }

        /// <summary>The name and the expression of the default of the column; null for none.</summary>
        public string DefaultName { get; set; }

        public string DefaultText { get; set; }
    }

    /// <summary>Builds the scripts that F12 opens.</summary>
    public static class DefinitionScript
    {
        /// <summary>
        /// SQL Server stores the text of a module as it was written, often with blank lines in front of CREATE; they would only push the script
        /// down in the new window or in the editor. Lines that hold nothing but white space are removed from the start; the indent of the first
        /// line with text stays.
        /// </summary>
        public static string WithoutLeadingBlankLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            var cut = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    cut = i + 1;
                }
                else if (!char.IsWhiteSpace(text[i]))
                {
                    break;
                }
            }

            return text.Substring(cut);
        }

        /// <summary>
        /// Turns the stored text of a procedure, view, function or trigger into CREATE OR ALTER: the first CREATE (outside comments)
        /// gets OR ALTER. A text that already says CREATE OR ALTER, or does not start with CREATE, is returned unchanged.
        /// </summary>
        public static string ToCreateOrAlter(string definition)
        {
            if (string.IsNullOrEmpty(definition))
            {
                return definition ?? string.Empty;
            }

            definition = WithoutLeadingBlankLines(definition);

            var tokens = Lexer.Tokenize(definition);
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.Comment)
                {
                    continue;
                }

                if (!tokens[i].IsWord("CREATE"))
                {
                    return definition;
                }

                var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
                if (next != null && next.IsWord("OR"))
                {
                    return definition;
                }

                return definition.Insert(tokens[i].End, " OR ALTER");
            }

            return definition;
        }

        /// <summary>
        /// Turns the stored text of a procedure, view, function or trigger into ALTER (or, with <paramref name="createOrAlter"/>, CREATE OR ALTER): the first
        /// CREATE (outside comments), with an OR ALTER after it when there is one, is replaced; comments, the case of the other words and the rest of
        /// the text are kept. A text that does not start with CREATE is returned unchanged.
        /// </summary>
        public static string ToAlter(string definition, bool createOrAlter = false)
        {
            definition = WithoutLeadingBlankLines(definition);
            if (createOrAlter || string.IsNullOrEmpty(definition))
            {
                return createOrAlter ? ToCreateOrAlter(definition) : definition ?? string.Empty;
            }

            var tokens = Lexer.Tokenize(definition);
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.Comment)
                {
                    continue;
                }

                if (!tokens[i].IsWord("CREATE"))
                {
                    return definition;
                }

                var end = tokens[i].End;
                if (i + 2 < tokens.Count && tokens[i + 1].IsWord("OR") && tokens[i + 2].IsWord("ALTER"))
                {
                    end = tokens[i + 2].End;
                }

                var alter = tokens[i].Text == tokens[i].Text.ToLowerInvariant() ? "alter" : "ALTER";
                return definition.Substring(0, tokens[i].Start) + alter + definition.Substring(end);
            }

            return definition;
        }

        /// <summary>
        /// Where the caret goes in a module script: the start of the line after the first AS of the header (not the AS of a parameter type);
        /// the end of the text when there is none.
        /// </summary>
        public static int BodyStart(string script)
        {
            script = script ?? string.Empty;
            var tokens = Lexer.Tokenize(script);
            var depth = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (depth == 0 && tokens[i].IsWord("AS") && (i == 0 || tokens[i - 1].Kind != TokenKind.Variable))
                {
                    var newLine = script.IndexOf('\n', tokens[i].End);
                    return newLine < 0 ? script.Length : newLine + 1;
                }
            }

            return script.Length;
        }
        /// <summary>A CREATE TABLE script with the columns and the primary key; indexes, defaults and foreign keys are not included.</summary>
        public static string ForTable(string schema, string name, IReadOnlyList<ScriptTableColumn> columns, string primaryKeyName, IReadOnlyList<string> primaryKeyColumns)
        {
            var lines = new List<string>();
            foreach (var column in columns)
            {
                lines.Add("    " + Quote(column.Name) + " " + column.TypeText
                    + (column.IdentityText != null ? " " + column.IdentityText : string.Empty)
                    + (column.IsNullable ? " NULL" : " NOT NULL"));
            }

            if (primaryKeyColumns != null && primaryKeyColumns.Count > 0)
            {
                lines.Add("    CONSTRAINT " + Quote(primaryKeyName ?? ("PK_" + name)) + " PRIMARY KEY (" + string.Join(", ", primaryKeyColumns.Select(Quote)) + ")");
            }

            var text = new StringBuilder();
            text.Append("CREATE TABLE ").Append(Quote(schema)).Append('.').Append(Quote(name)).Append("\r\n(\r\n");
            text.Append(string.Join(",\r\n", lines));
            text.Append("\r\n);\r\n");
            return text.ToString();
        }

        /// <summary>
        /// The complete script of a table (#351), runnable on an empty database: <c>CREATE TABLE</c> with the columns (identity, computed columns,
        /// collation where it differs, named defaults), the primary key, unique and check constraints; then the foreign keys as <c>ALTER TABLE</c>, the
        /// indexes, and the triggers (each batch ends with <c>GO</c>). The tables that point to this one are listed as a comment.
        /// </summary>
        public static string ForTable(TableScriptModel table)
        {
            const string nl = "\r\n";
            var lines = new List<string>();
            foreach (var column in table.Columns)
            {
                lines.Add("    " + ColumnText(column));
            }

            foreach (var key in table.Indexes.Where(i => i.IsPrimaryKey || i.IsUniqueConstraint))
            {
                lines.Add("    CONSTRAINT " + Quote(key.Name) + (key.IsPrimaryKey ? " PRIMARY KEY " : " UNIQUE ") + (key.IsClustered ? "CLUSTERED " : "NONCLUSTERED ")
                    + "(" + string.Join(", ", key.KeyColumns) + ")" + WithOptions(key));
            }

            foreach (var check in table.Checks)
            {
                lines.Add("    CONSTRAINT " + Quote(check.Name) + " CHECK " + Parenthesized(check.Definition));
            }

            var text = new StringBuilder();
            var target = Quote(table.Schema) + "." + Quote(table.Name);
            text.Append("CREATE TABLE ").Append(target).Append(nl).Append('(').Append(nl);
            text.Append(string.Join("," + nl, lines));
            text.Append(nl).Append(");").Append(nl);

            foreach (var check in table.Checks.Where(c => c.IsDisabled))
            {
                text.Append("ALTER TABLE ").Append(target).Append(" NOCHECK CONSTRAINT ").Append(Quote(check.Name)).Append(';').Append(nl);
            }

            if (table.ForeignKeys.Count > 0)
            {
                text.Append(nl);
            }

            foreach (var fk in table.ForeignKeys)
            {
                text.Append("ALTER TABLE ").Append(target).Append(fk.IsNotTrusted ? " WITH NOCHECK" : " WITH CHECK").Append(" ADD CONSTRAINT ").Append(Quote(fk.Name))
                    .Append(" FOREIGN KEY (").Append(string.Join(", ", fk.Columns.Select(Quote))).Append(") REFERENCES ")
                    .Append(Quote(fk.ReferencedSchema)).Append('.').Append(Quote(fk.ReferencedTable)).Append(" (").Append(string.Join(", ", fk.ReferencedColumns.Select(Quote))).Append(')')
                    .Append(Action(" ON DELETE ", fk.DeleteAction)).Append(Action(" ON UPDATE ", fk.UpdateAction))
                    .Append(fk.IsNotForReplication ? " NOT FOR REPLICATION" : string.Empty).Append(';').Append(nl);
                if (fk.IsDisabled)
                {
                    text.Append("ALTER TABLE ").Append(target).Append(" NOCHECK CONSTRAINT ").Append(Quote(fk.Name)).Append(';').Append(nl);
                }
            }

            var indexes = table.Indexes.Where(i => !i.IsPrimaryKey && !i.IsUniqueConstraint).ToList();
            if (indexes.Count > 0)
            {
                text.Append(nl);
            }

            foreach (var index in indexes)
            {
                text.Append("CREATE ").Append(index.IsUnique ? "UNIQUE " : string.Empty).Append(index.IsClustered ? "CLUSTERED" : "NONCLUSTERED").Append(" INDEX ").Append(Quote(index.Name))
                    .Append(" ON ").Append(target).Append(" (").Append(string.Join(", ", index.KeyColumns)).Append(')');
                if (index.IncludedColumns.Count > 0)
                {
                    text.Append(" INCLUDE (").Append(string.Join(", ", index.IncludedColumns)).Append(')');
                }

                if (!string.IsNullOrEmpty(index.Filter))
                {
                    text.Append(" WHERE ").Append(index.Filter);
                }

                text.Append(WithOptions(index)).Append(';').Append(nl);
                if (index.IsDisabled)
                {
                    text.Append("ALTER INDEX ").Append(Quote(index.Name)).Append(" ON ").Append(target).Append(" DISABLE;").Append(nl);
                }
            }

            foreach (var trigger in table.Triggers)
            {
                text.Append(nl).Append("GO").Append(nl).Append(WithoutLeadingBlankLines(trigger.Definition).TrimEnd()).Append(nl).Append("GO").Append(nl);
            }

            if (table.ReferencedBy.Count > 0)
            {
                text.Append(nl).Append("-- Referenced by: ").Append(string.Join(", ", table.ReferencedBy.Select(r => Quote(r.Schema) + "." + Quote(r.Table) + " (" + r.ForeignKey + ")"))).Append(nl);
            }

            return text.ToString();
        }

        private static string ColumnText(ScriptTableColumn column)
        {
            if (column.ComputedText != null)
            {
                return Quote(column.Name) + " AS " + Parenthesized(column.ComputedText) + (column.IsPersisted ? " PERSISTED" : string.Empty) + (column.IsNullable ? string.Empty : " NOT NULL");
            }

            return Quote(column.Name) + " " + column.TypeText
                + (column.Collation != null ? " COLLATE " + column.Collation : string.Empty)
                + (column.IdentityText != null ? " " + column.IdentityText : string.Empty)
                + (column.IsNullable ? " NULL" : " NOT NULL")
                + (column.DefaultText != null ? " CONSTRAINT " + Quote(column.DefaultName) + " DEFAULT " + Parenthesized(column.DefaultText) : string.Empty);
        }

        /// <summary>SQL Server stores an expression with its own parentheses; text without them gets them.</summary>
        private static string Parenthesized(string expression)
        {
            expression = (expression ?? string.Empty).Trim();
            return expression.StartsWith("(", StringComparison.Ordinal) && expression.EndsWith(")", StringComparison.Ordinal) ? expression : "(" + expression + ")";
        }

        private static string WithOptions(TableIndexScript index) => index.Options.Count == 0 ? string.Empty : " WITH (" + string.Join(", ", index.Options) + ")";

        private static string Action(string prefix, string action) =>
            string.IsNullOrEmpty(action) || action == "NO_ACTION" ? string.Empty : prefix + action.Replace('_', ' ');

        private static string Quote(string identifier) => "[" + (identifier ?? string.Empty).Replace("]", "]]") + "]";
    }
}
