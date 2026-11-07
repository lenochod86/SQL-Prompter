using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>What the caret in the definition of a table is waiting for (#213).</summary>
    internal sealed class TableDdlPosition
    {
        public TableDdlPosition(ContextKind kind, KeywordPosition keywords = null, string typeOfName = null, IReadOnlyList<string> table = null, bool sizes = false)
        {
            Kind = kind;
            Keywords = keywords ?? KeywordPosition.None;
            TypeOfName = typeOfName;
            Table = table;
            Sizes = sizes;
        }

        /// <summary><see cref="ContextKind.DataType"/>, <see cref="ContextKind.ObjectName"/> (REFERENCES), <see cref="ContextKind.InsertColumns"/> (the columns of Table), <see cref="ContextKind.Name"/> or a keyword kind.</summary>
        public ContextKind Kind { get; }

        public KeywordPosition Keywords { get; }

        /// <summary>For a data type: the column name, whose same-named columns give the types to offer first.</summary>
        public string TypeOfName { get; }

        /// <summary>For the columns of a table: the name parts of the table.</summary>
        public IReadOnlyList<string> Table { get; }

        /// <summary>The data types come with the usual lengths (nvarchar(100), decimal(18,2) ...).</summary>
        public bool Sizes { get; }

        /// <summary>The columns of Table are the key columns of a REFERENCES (the primary key comes first).</summary>
        public bool KeyColumnsOfReference { get; internal set; }

        /// <summary>For a constraint name: the table and the column it is named after.</summary>
        public string ConstraintTable { get; internal set; }

        public string ConstraintColumn { get; internal set; }

        /// <summary>For the name of an existing constraint (DROP, CHECK, NOCHECK CONSTRAINT): the table.</summary>
        public IReadOnlyList<string> ExistingConstraintsOf { get; internal set; }
    }

    /// <summary>
    /// CREATE TABLE and DECLARE @t TABLE column definitions, and ALTER TABLE (#213): after a column name the data types, then the column options
    /// (NOT NULL, IDENTITY, DEFAULT, PRIMARY KEY, REFERENCES ... ON DELETE CASCADE ...); after ALTER TABLE t the actions, the columns of t after
    /// ALTER COLUMN and DROP COLUMN.
    /// </summary>
    internal static class TableDdl
    {
        private static KeywordPosition Of(string[] first, string[] then = null, bool exclusive = false) =>
            new KeywordPosition(first, then ?? Array.Empty<string>(), exclusive);

        private static readonly string[] ColumnOptions = { "NOT NULL", "NULL", "IDENTITY(1,1)", "DEFAULT", "PRIMARY KEY", "UNIQUE", "REFERENCES", "CHECK", "COLLATE", "CONSTRAINT" };
        private static readonly string[] TableItems = { "CONSTRAINT", "PRIMARY KEY", "UNIQUE", "FOREIGN KEY", "CHECK", "INDEX" };
        private static readonly string[] ReferentialActions = { "CASCADE", "SET NULL", "SET DEFAULT", "NO ACTION" };

        /// <summary>The position of the caret (after the token at <paramref name="anchorIndex"/>) in a table definition or ALTER TABLE; null elsewhere.</summary>
        public static TableDdlPosition At(List<Token> tokens, int statementStart, int anchorIndex)
        {
            var position = AtCore(tokens, statementStart, anchorIndex);
            if (position != null && position.Kind == ContextKind.Name && tokens[anchorIndex].IsWord("CONSTRAINT"))
            {
                AddConstraintContext(position, tokens, statementStart, anchorIndex);
            }

            return position;
        }

        /// <summary>After CONSTRAINT: the table and column for a new name, or the table for the names of existing constraints.</summary>
        private static void AddConstraintContext(TableDdlPosition position, List<Token> tokens, int statementStart, int anchorIndex)
        {
            var head = tokens[statementStart];
            var alter = head.IsWord("ALTER");
            var table = new List<string>();
            var afterName = SkipName(tokens, statementStart + 2, anchorIndex, table);
            if (table.Count == 0 || tokens[statementStart + 1].IsWord("TABLE") == false)
            {
                return;
            }

            var previous = tokens[anchorIndex - 1];
            if (alter && (previous.IsWord("DROP") || previous.IsWord("CHECK") || previous.IsWord("NOCHECK")))
            {
                position.ExistingConstraintsOf = table;
                return;
            }

            position.ConstraintTable = table[table.Count - 1];
            if (alter)
            {
                // ALTER TABLE t ADD [COLUMN] col type CONSTRAINT |
                var k = afterName;
                if (k < anchorIndex && tokens[k].IsWord("ADD"))
                {
                    k++;
                    if (k < anchorIndex && tokens[k].IsWord("COLUMN"))
                    {
                        k++;
                    }

                    if (k < anchorIndex - 1 && tokens[k].IsNamePart && !tokens[k].IsWord("CONSTRAINT"))
                    {
                        position.ConstraintColumn = tokens[k].IdentifierValue;
                    }
                }

                return;
            }

            // CREATE TABLE t (... col type CONSTRAINT |: the first word of the current item, unless it is the CONSTRAINT itself.
            var open = afterName;
            if (open >= anchorIndex || tokens[open].Kind != TokenKind.OpenParen)
            {
                return;
            }

            var depth = 1;
            var itemStart = open + 1;
            for (var i = open + 1; i <= anchorIndex; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    depth--;
                }
                else if (depth == 1 && tokens[i].Kind == TokenKind.Comma)
                {
                    itemStart = i + 1;
                }
            }

            if (itemStart < anchorIndex && tokens[itemStart].IsNamePart && !tokens[itemStart].IsWord("CONSTRAINT"))
            {
                position.ConstraintColumn = tokens[itemStart].IdentifierValue;
            }
        }

        private static TableDdlPosition AtCore(List<Token> tokens, int statementStart, int anchorIndex)
        {
            if (statementStart < 0 || anchorIndex <= statementStart || anchorIndex >= tokens.Count)
            {
                return null;
            }

            var head = tokens[statementStart];
            if (head.IsWord("CREATE") && statementStart + 1 < tokens.Count && tokens[statementStart + 1].IsWord("TABLE"))
            {
                return InDefinition(tokens, statementStart + 2, anchorIndex);
            }

            if (head.IsWord("DECLARE"))
            {
                for (var i = statementStart + 2; i <= anchorIndex; i++)
                {
                    if (tokens[i].IsWord("TABLE") && tokens[i - 1].Kind == TokenKind.Variable)
                    {
                        return InDefinition(tokens, i + 1, anchorIndex, variable: true);
                    }
                }

                return null;
            }

            if (head.IsWord("ALTER") && statementStart + 1 < tokens.Count && tokens[statementStart + 1].IsWord("TABLE"))
            {
                return InAlter(tokens, statementStart + 2, anchorIndex);
            }

            return null;
        }

        private static int SkipName(List<Token> tokens, int from, int limit, List<string> parts)
        {
            var i = from;
            while (i <= limit && tokens[i].IsNamePart)
            {
                parts.Add(tokens[i].IdentifierValue);
                i++;
                if (i <= limit && tokens[i].Kind == TokenKind.Dot)
                {
                    i++;
                }
                else
                {
                    break;
                }
            }

            return i;
        }

        private static TableDdlPosition InDefinition(List<Token> tokens, int nameStart, int anchorIndex, bool variable = false)
        {
            var open = nameStart;
            if (!variable)
            {
                var parts = new List<string>();
                open = SkipName(tokens, nameStart, anchorIndex, parts);
            }

            if (open > anchorIndex || tokens[open].Kind != TokenKind.OpenParen)
            {
                return null;
            }

            var depth = 1;
            var itemStart = open + 1;
            for (var i = open + 1; i <= anchorIndex; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    if (--depth == 0)
                    {
                        return null;
                    }
                }
                else if (depth == 1 && tokens[i].Kind == TokenKind.Comma)
                {
                    itemStart = i + 1;
                }
            }

            if (depth != 1)
            {
                // REFERENCES t (|: the key columns of t. Elsewhere inside parentheses (a type, a CHECK) nothing is decided here.
                if (depth == 2 && tokens[anchorIndex].Kind == TokenKind.OpenParen)
                {
                    var current = tokens.GetRange(itemStart, anchorIndex - itemStart + 1);
                    var references = current.FindIndex(t => t.IsWord("REFERENCES"));
                    if (references >= 0)
                    {
                        return References(current, references);
                    }
                }

                return null;
            }

            var item = tokens.GetRange(itemStart, anchorIndex - itemStart + 1);
            if (item.Count == 0 || itemStart > anchorIndex)
            {
                return new TableDdlPosition(ContextKind.Fallback, Of(TableItems), null);
            }

            var first = item[0];
            if (first.IsWord("CONSTRAINT") || first.IsWord("PRIMARY") || first.IsWord("UNIQUE") || first.IsWord("FOREIGN") || first.IsWord("CHECK") || first.IsWord("INDEX"))
            {
                return TableConstraint(item);
            }

            if (item.Count == 1)
            {
                return first.IsNamePart ? new TableDdlPosition(ContextKind.DataType, null, first.IdentifierValue, null, sizes: true) : null;
            }

            return ColumnOption(item, 1);
        }

        /// <summary>A table-level constraint: CONSTRAINT name PRIMARY KEY | UNIQUE | FOREIGN KEY (...) REFERENCES ... | CHECK (...).</summary>
        private static TableDdlPosition TableConstraint(List<Token> item)
        {
            var i = 0;
            if (item[0].IsWord("CONSTRAINT"))
            {
                if (item.Count == 1)
                {
                    return new TableDdlPosition(ContextKind.Name);
                }

                if (item.Count == 2)
                {
                    return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "PRIMARY KEY", "UNIQUE", "FOREIGN KEY", "CHECK", "DEFAULT" }, null, true));
                }

                i = 2;
            }

            var last = item[item.Count - 1];
            var word = item[i];
            if ((word.IsWord("PRIMARY") || word.IsWord("FOREIGN")) && item.Count == i + 1)
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "KEY" }, null, true));
            }

            if ((word.IsWord("PRIMARY") || word.IsWord("UNIQUE")) && last.IsWord("KEY") || (word.IsWord("UNIQUE") && item.Count == i + 1))
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "CLUSTERED", "NONCLUSTERED" }));
            }

            if (word.IsWord("FOREIGN") && last.Kind == TokenKind.CloseParen)
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "REFERENCES" }, null, true));
            }

            var refs = item.FindIndex(t => t.IsWord("REFERENCES"));
            return refs >= 0 ? References(item, refs) : null;
        }

        /// <summary>The options of a column after its data type: NOT NULL, IDENTITY, DEFAULT, PRIMARY KEY, UNIQUE, REFERENCES, CHECK, COLLATE.</summary>
        private static TableDdlPosition ColumnOption(List<Token> item, int typeIndex)
        {
            // The type: a word (or two for DOUBLE PRECISION) and its parenthesized size.
            var i = typeIndex + 1;
            if (typeIndex < item.Count && item[typeIndex].IsWord("DOUBLE") && i < item.Count && item[i].IsWord("PRECISION"))
            {
                i++;
            }

            if (i < item.Count && item[i].Kind == TokenKind.OpenParen)
            {
                var depth = 0;
                for (; i < item.Count; i++)
                {
                    if (item[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (item[i].Kind == TokenKind.CloseParen && --depth == 0)
                    {
                        i++;
                        break;
                    }
                }

                if (depth > 0)
                {
                    return null;
                }
            }

            var rest = item.GetRange(Math.Min(i, item.Count), item.Count - Math.Min(i, item.Count));
            return Options(rest, forAlter: false);
        }

        private static TableDdlPosition Options(List<Token> rest, bool forAlter)
        {
            var allOptions = forAlter ? new[] { "NULL", "NOT NULL", "COLLATE" } : ColumnOptions;
            if (rest.Count == 0)
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(allOptions.Where(o => o != "CONSTRAINT").ToArray(), new[] { "CONSTRAINT" }));
            }

            var last = rest[rest.Count - 1];
            if (last.IsWord("NOT"))
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "NULL" }, null, true));
            }

            if (last.IsWord("PRIMARY") || last.IsWord("FOREIGN"))
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "KEY" }, null, true));
            }

            if (last.IsWord("CONSTRAINT"))
            {
                return new TableDdlPosition(ContextKind.Name);
            }

            if (last.IsWord("DEFAULT") || last.IsWord("COLLATE") || last.IsWord("CHECK"))
            {
                return new TableDdlPosition(ContextKind.Fallback);
            }

            var refs = rest.FindIndex(t => t.IsWord("REFERENCES"));
            if (refs >= 0)
            {
                return References(rest, refs);
            }

            if (last.IsWord("KEY") || last.IsWord("UNIQUE"))
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "CLUSTERED", "NONCLUSTERED" }, Remaining(allOptions, rest)));
            }

            // After a complete option: the options that are not there yet.
            return new TableDdlPosition(ContextKind.Keyword, Of(Remaining(allOptions, rest), null));
        }

        private static string[] Remaining(string[] options, List<Token> used)
        {
            bool Has(string word) => used.Any(t => t.IsWord(word));
            return options.Where(o =>
            {
                switch (o)
                {
                    case "NOT NULL":
                    case "NULL":
                        return !Has("NULL");
                    case "IDENTITY(1,1)":
                        return !Has("IDENTITY");
                    case "PRIMARY KEY":
                        return !Has("PRIMARY");
                    case "DEFAULT":
                    case "UNIQUE":
                    case "REFERENCES":
                    case "COLLATE":
                    case "CHECK":
                        return !Has(o);
                    default:
                        return true;
                }
            }).ToArray();
        }

        /// <summary>REFERENCES table [(column)] [ON DELETE action] [ON UPDATE action].</summary>
        private static TableDdlPosition References(List<Token> tokens, int refs)
        {
            var after = tokens.GetRange(refs + 1, tokens.Count - refs - 1);
            var parts = new List<string>();
            var i = 0;
            while (i < after.Count && after[i].IsNamePart)
            {
                parts.Add(after[i].IdentifierValue);
                i++;
                if (i < after.Count && after[i].Kind == TokenKind.Dot)
                {
                    i++;
                }
                else
                {
                    break;
                }
            }

            if (after.Count == 0 || (i == after.Count && (after[after.Count - 1].Kind == TokenKind.Dot || parts.Count == 0)))
            {
                return new TableDdlPosition(ContextKind.ObjectName);
            }

            if (i == after.Count)
            {
                // Right after the table name: the parenthesis with the key columns comes, or the actions.
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "ON DELETE", "ON UPDATE" }));
            }

            var tail = after.GetRange(i, after.Count - i);
            if (tail[0].Kind == TokenKind.OpenParen && tail.Count == 1)
            {
                // REFERENCES t (|: the key columns of t, the primary key first.
                return new TableDdlPosition(ContextKind.InsertColumns, null, null, parts) { KeyColumnsOfReference = true };
            }

            var last = tail[tail.Count - 1];
            if (last.Kind == TokenKind.CloseParen && tail.Count == 1)
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "ON DELETE", "ON UPDATE" }));
            }

            if (last.IsWord("ON"))
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "DELETE", "UPDATE" }, null, true));
            }

            if (last.IsWord("DELETE") || last.IsWord("UPDATE"))
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(ReferentialActions, null, true));
            }

            if (last.IsWord("SET"))
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "NULL", "DEFAULT" }, null, true));
            }

            if (last.IsWord("NO"))
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "ACTION" }, null, true));
            }

            // After an action: the other ON clause.
            var done = tail.Where(t => t.IsWord("DELETE") || t.IsWord("UPDATE")).Select(t => t.Upper).ToList();
            var left = new[] { "ON DELETE", "ON UPDATE" }.Where(o => !done.Contains(o.Substring(3))).ToArray();
            return left.Length == 0 ? null : new TableDdlPosition(ContextKind.Keyword, Of(left));
        }

        private static readonly string[] AlterActions =
        {
            "ADD", "ALTER COLUMN", "DROP COLUMN", "DROP CONSTRAINT", "ADD CONSTRAINT", "WITH CHECK", "WITH NOCHECK", "NOCHECK CONSTRAINT", "CHECK CONSTRAINT",
            "ENABLE TRIGGER", "DISABLE TRIGGER",
        };

        private static TableDdlPosition InAlter(List<Token> tokens, int nameStart, int anchorIndex)
        {
            var table = new List<string>();
            var i = SkipName(tokens, nameStart, anchorIndex, table);
            if (table.Count == 0 || i > anchorIndex + 1)
            {
                return null;
            }

            if (tokens[i - 1].Kind == TokenKind.Dot)
            {
                return null;
            }

            var rest = i > anchorIndex ? new List<Token>() : tokens.GetRange(i, anchorIndex - i + 1);
            if (rest.Count == 0)
            {
                return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "ADD", "ALTER COLUMN", "DROP COLUMN", "DROP CONSTRAINT" }, AlterActions.Skip(4).ToArray()));
            }

            var first = rest[0];
            var last = rest[rest.Count - 1];
            if (first.IsWord("WITH"))
            {
                if (rest.Count == 1)
                {
                    return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "CHECK", "NOCHECK" }, null, true));
                }

                return rest.Count == 2 && (last.IsWord("CHECK") || last.IsWord("NOCHECK")) ? new TableDdlPosition(ContextKind.Keyword, Of(new[] { "ADD" }, null, true)) : null;
            }

            if (first.IsWord("ENABLE") || first.IsWord("DISABLE"))
            {
                return rest.Count == 1 ? new TableDdlPosition(ContextKind.Keyword, Of(new[] { "TRIGGER" }, null, true)) : null;
            }

            if (first.IsWord("NOCHECK") || first.IsWord("CHECK"))
            {
                if (rest.Count == 2 && rest[1].IsWord("CONSTRAINT"))
                {
                    return new TableDdlPosition(ContextKind.Name);
                }

                return rest.Count == 1 ? new TableDdlPosition(ContextKind.Keyword, Of(new[] { "CONSTRAINT" }, null, true)) : null;
            }

            if (first.IsWord("ALTER"))
            {
                if (rest.Count == 1)
                {
                    return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "COLUMN" }, null, true));
                }

                if (!rest[1].IsWord("COLUMN"))
                {
                    return null;
                }

                if (rest.Count == 2)
                {
                    return new TableDdlPosition(ContextKind.InsertColumns, null, null, table);
                }

                if (rest.Count == 3)
                {
                    return new TableDdlPosition(ContextKind.DataType, null, rest[2].IdentifierValue, null, sizes: true);
                }

                return Options(StripType(rest, 3), forAlter: true);
            }

            if (first.IsWord("DROP"))
            {
                if (rest.Count == 1)
                {
                    return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "COLUMN", "CONSTRAINT" }, new[] { "IF EXISTS" }, true));
                }

                if (rest[1].IsWord("COLUMN"))
                {
                    return rest.Count == 2 || last.Kind == TokenKind.Comma ? new TableDdlPosition(ContextKind.InsertColumns, null, null, table) : null;
                }

                return rest[1].IsWord("CONSTRAINT") && rest.Count == 2 ? new TableDdlPosition(ContextKind.Name) : null;
            }

            if (first.IsWord("ADD"))
            {
                if (rest.Count == 1)
                {
                    return new TableDdlPosition(ContextKind.Keyword, Of(new[] { "CONSTRAINT", "COLUMN" }), null, null);
                }

                if (rest[1].IsWord("CONSTRAINT") || rest[1].IsWord("PRIMARY") || rest[1].IsWord("FOREIGN") || rest[1].IsWord("UNIQUE") || rest[1].IsWord("CHECK") || rest[1].IsWord("DEFAULT"))
                {
                    return TableConstraint(rest.GetRange(1, rest.Count - 1));
                }

                var column = rest[1].IsWord("COLUMN") ? 2 : 1;
                if (rest.Count == column)
                {
                    return new TableDdlPosition(ContextKind.Name);
                }

                if (rest.Count == column + 1)
                {
                    return rest[column].IsNamePart ? new TableDdlPosition(ContextKind.DataType, null, rest[column].IdentifierValue, null, sizes: true) : null;
                }

                return ColumnOption(rest.GetRange(column, rest.Count - column), 1);
            }

            return null;
        }

        /// <summary>The tokens after the data type that starts at <paramref name="typeIndex"/> (a type word and its parenthesized size).</summary>
        private static List<Token> StripType(List<Token> tokens, int typeIndex)
        {
            var i = typeIndex + 1;
            if (i < tokens.Count && tokens[i].Kind == TokenKind.OpenParen)
            {
                var depth = 0;
                for (; i < tokens.Count; i++)
                {
                    if (tokens[i].Kind == TokenKind.OpenParen)
                    {
                        depth++;
                    }
                    else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
                    {
                        i++;
                        break;
                    }
                }
            }

            i = Math.Min(i, tokens.Count);
            return tokens.GetRange(i, tokens.Count - i);
        }
    }
}
