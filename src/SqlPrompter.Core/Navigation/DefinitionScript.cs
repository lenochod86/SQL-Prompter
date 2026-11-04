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
    }

    /// <summary>Builds the scripts that F12 opens.</summary>
    public static class DefinitionScript
    {
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

        private static string Quote(string identifier) => "[" + (identifier ?? string.Empty).Replace("]", "]]") + "]";
    }
}
