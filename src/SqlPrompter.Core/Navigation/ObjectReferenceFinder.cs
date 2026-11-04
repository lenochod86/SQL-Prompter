using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>The object a name in a script refers to; the schema and the database are null when the name did not give them.</summary>
    public sealed class ObjectReference
    {
        public ObjectReference(string database, string schema, string name)
        {
            Database = database;
            Schema = schema;
            Name = name;
        }

        public string Database { get; }

        public string Schema { get; }

        public string Name { get; }

        public override string ToString() => string.Join(".", new[] { Database, Schema, Name }.Where(p => p != null));
    }

    /// <summary>Finds the table, view, procedure or function that the name under the caret stands for.</summary>
    public static class ObjectReferenceFinder
    {
        /// <summary>
        /// Reads the dotted name around the caret (up to the part the caret is in) and resolves an alias or a column qualifier to its table.
        /// Returns null when the caret is not on a name or the name is a derived table, a temporary table or a table variable.
        /// </summary>
        public static ObjectReference Find(string text, int caret)
        {
            var tokens = Lexer.Tokenize(text ?? string.Empty);
            var index = -1;
            for (var i = 0; i < tokens.Count; i++)
            {
                // The caret may be just after the last character of the name.
                if (tokens[i].IsNamePart && tokens[i].Start <= caret && caret <= tokens[i].End)
                {
                    index = i;
                    if (caret < tokens[i].End)
                    {
                        break;
                    }
                }
            }

            if (index < 0 || tokens[index].Kind == TokenKind.Word && tokens[index].Text.StartsWith("#", StringComparison.Ordinal))
            {
                return null;
            }

            var parts = new List<string> { tokens[index].IdentifierValue };
            var at = index;
            while (at >= 2 && tokens[at - 1].Kind == TokenKind.Dot && tokens[at - 2].IsNamePart && parts.Count < 3)
            {
                at -= 2;
                parts.Insert(0, tokens[at].IdentifierValue);
            }

            if (at >= 2 && tokens[at - 1].Kind == TokenKind.Dot && tokens[at - 2].IsNamePart)
            {
                // Four parts name an object on a linked server, which is not followed.
                return null;
            }

            var sources = ContextAnalyzer.Analyze(text, caret).Sources;
            if (parts.Count <= 2)
            {
                var alias = sources.FirstOrDefault(s => string.Equals(s.ReferenceName, parts[0], StringComparison.OrdinalIgnoreCase) && (parts.Count == 2 || s.Alias != null));
                if (alias != null)
                {
                    if (alias.Kind != TableSourceKind.Object)
                    {
                        return null;
                    }

                    return FromParts(alias.NameParts.ToList());
                }
            }

            return FromParts(parts);
        }

        private static ObjectReference FromParts(IReadOnlyList<string> parts)
        {
            switch (parts.Count)
            {
                case 1:
                    return new ObjectReference(null, null, parts[0]);
                case 2:
                    return new ObjectReference(null, parts[0], parts[1]);
                case 3:
                    return new ObjectReference(parts[0], parts[1], parts[2]);
                default:
                    return null;
            }
        }
    }
}
