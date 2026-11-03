using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>
    /// Tells whether executing a script may change the objects the completion knows about,
    /// so that the metadata of the window's database should be refreshed. Comments and strings are ignored.
    /// </summary>
    public static class SchemaChangeDetector
    {
        /// <summary>Object kinds whose CREATE, ALTER or DROP changes what completion offers.</summary>
        private static readonly HashSet<string> ObjectKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TABLE", "VIEW", "PROC", "PROCEDURE", "FUNCTION", "TRIGGER", "SCHEMA", "SYNONYM", "TYPE", "SEQUENCE",
        };

        public static bool ChangesSchema(string script)
        {
            if (string.IsNullOrEmpty(script))
            {
                return false;
            }

            var tokens = Lexer.Tokenize(script).Where(t => t.Kind != TokenKind.Comment).ToList();
            var parenDepth = 0;
            var selectDepths = new Stack<int>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    parenDepth++;
                    continue;
                }

                if (token.Kind == TokenKind.CloseParen)
                {
                    parenDepth = Math.Max(0, parenDepth - 1);
                    while (selectDepths.Count > 0 && selectDepths.Peek() > parenDepth)
                    {
                        selectDepths.Pop();
                    }

                    continue;
                }

                if (token.Kind == TokenKind.Semicolon)
                {
                    parenDepth = 0;
                    selectDepths.Clear();
                    continue;
                }

                if (token.Kind != TokenKind.Word)
                {
                    continue;
                }

                switch (token.Upper)
                {
                    case "CREATE":
                    case "ALTER":
                    case "DROP":
                        if (IsObjectDefinition(tokens, i + 1))
                        {
                            return true;
                        }

                        break;
                    case "SP_RENAME":
                        return true;
                    case "SELECT":
                        selectDepths.Push(parenDepth);
                        break;
                    case "INTO":
                        // SELECT … INTO creates a table; INSERT INTO and MERGE INTO do not.
                        var previous = i > 0 ? tokens[i - 1].Upper : null;
                        if (previous != "INSERT" && previous != "MERGE" && selectDepths.Count > 0 && selectDepths.Peek() == parenDepth &&
                            i + 1 < tokens.Count && tokens[i + 1].IsNamePart)
                        {
                            return true;
                        }

                        break;
                    case "FROM":
                    case "UNION":
                    case "EXCEPT":
                    case "INTERSECT":
                        // INTO is only valid between the select list and FROM.
                        if (selectDepths.Count > 0 && selectDepths.Peek() == parenDepth)
                        {
                            selectDepths.Pop();
                        }

                        break;
                }
            }

            return false;
        }

        /// <summary>CREATE / ALTER / DROP [OR ALTER] [UNIQUE] [CLUSTERED …] object kind.</summary>
        private static bool IsObjectDefinition(List<Token> tokens, int index)
        {
            for (var i = index; i < tokens.Count && i < index + 3; i++)
            {
                var word = tokens[i].Upper;
                if (word == null)
                {
                    return false;
                }

                if (ObjectKinds.Contains(word))
                {
                    return true;
                }

                if (word != "OR" && word != "ALTER")
                {
                    return false;
                }
            }

            return false;
        }
    }
}
