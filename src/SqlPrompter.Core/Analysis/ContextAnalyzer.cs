using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;
using SqlPrompter.Core.Sql;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>
    /// Determines what is being typed at the caret. Works with incomplete SQL; positions it does not
    /// understand are reported as Name or Fallback so that only our list is shown.
    /// </summary>
    public static class ContextAnalyzer
    {
        private enum Clause
        {
            None,
            Select,
            From,
            On,
            Where,
            GroupBy,
            Having,
            OrderBy,
            Set,
            Other,
        }

        private static readonly HashSet<string> JoinModifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "LEFT", "RIGHT", "FULL", "INNER", "CROSS", "OUTER",
        };

        /// <summary>The table sources visible at a position of the text (the statement's own and, for subqueries, the outer ones), whatever the context there is.</summary>
        public static IReadOnlyList<TableSource> SourcesAt(string text, int position)
        {
            if (text == null || position < 0 || position > text.Length)
            {
                return Array.Empty<TableSource>();
            }

            var tokens = CodeTokens(text, Lexer.Tokenize(text), position);
            var index = Math.Min(CountBefore(tokens, position), Math.Max(0, tokens.Count - 1));
            if (tokens.Count == 0)
            {
                return Array.Empty<TableSource>();
            }

            var bounds = StatementSplitter.Split(tokens);
            var statementStart = 0;
            var statementEnd = tokens.Count;
            for (var b = 0; b < bounds.Count; b++)
            {
                if (bounds[b] <= index)
                {
                    statementStart = bounds[b];
                    statementEnd = b + 1 < bounds.Count ? bounds[b + 1] : tokens.Count;
                }
            }

            if (statementEnd <= index)
            {
                statementEnd = tokens.Count;
            }

            var structure = new Structure(tokens, statementStart, statementEnd);
            return CollectSources(structure, structure.ScopesAt(index));
        }

        public static CaretContext Analyze(string text, int caret)
        {
            var context = AnalyzeCore(text, caret);
            if (!ReferenceEquals(context, CaretContext.None))
            {
                context.Text = text;
                context.Caret = caret;
            }

            return context;
        }

        private static CaretContext AnalyzeCore(string text, int caret)
        {
            if (text == null || caret < 0 || caret > text.Length)
            {
                return CaretContext.None;
            }

            var all = Lexer.Tokenize(text);
            if (all.Any(t => (t.Kind == TokenKind.Comment || t.Kind == TokenKind.String) && Contains(t, caret)))
            {
                return CaretContext.None;
            }

            var tokens = WithMissingOn(CodeTokens(text, all, caret));
            var word = tokens.FirstOrDefault(t => t.Start < caret && caret <= t.End);
            if (word != null && word.Kind == TokenKind.Number)
            {
                return CaretContext.None;
            }

            // Variable or temporary table: not known to the extension yet, but the built-in list must not show either.
            var namesUnknownObject = word != null &&
                (word.Kind == TokenKind.Variable || (word.Kind == TokenKind.Word && word.Text.StartsWith("#", StringComparison.Ordinal)));

            if (word != null && !namesUnknownObject && word.Kind != TokenKind.Word && word.Kind != TokenKind.QuotedIdentifier)
            {
                // Caret right after an operator, dot or parenthesis: no word being typed.
                word = null;
            }

            var caretIndex = word != null ? tokens.IndexOf(word) : CountBefore(tokens, caret);

            // Qualifier: a.b.| -> [a, b]
            var qualifier = new List<string>();
            var nameStart = caretIndex;
            while (nameStart >= 2 && tokens[nameStart - 1].Kind == TokenKind.Dot)
            {
                if (tokens[nameStart - 2].IsNamePart || tokens[nameStart - 2].Kind == TokenKind.Variable)
                {
                    qualifier.Insert(0, tokens[nameStart - 2].IdentifierValue);
                    nameStart -= 2;
                }
                else if (nameStart >= 3 && tokens[nameStart - 2].Kind == TokenKind.Dot && tokens[nameStart - 3].IsNamePart)
                {
                    // db..| : the schema between the dots is left out, i.e. the default one.
                    qualifier.Insert(0, string.Empty);
                    nameStart -= 1;
                }
                else
                {
                    break;
                }
            }

            var unknownQualifier = nameStart >= 1 && tokens[nameStart - 1].Kind == TokenKind.Dot; // db..t, @t.column
            var replaceStart = word?.Start ?? caret;
            var replaceLength = word != null ? word.End - word.Start : 0;
            var typed = word != null ? text.Substring(word.Start, caret - word.Start) : string.Empty;
            if (typed.StartsWith("[", StringComparison.Ordinal))
            {
                typed = typed.Substring(1);
            }

            var bounds = StatementSplitter.Split(tokens);
            var statementStart = 0;
            var statementEnd = tokens.Count;
            for (var b = 0; b < bounds.Count; b++)
            {
                if (bounds[b] <= nameStart)
                {
                    statementStart = bounds[b];
                    statementEnd = b + 1 < bounds.Count ? bounds[b + 1] : tokens.Count;
                }
            }

            if (statementEnd <= nameStart)
            {
                statementEnd = tokens.Count;
            }

            CaretContext Make(ContextKind kind) => new CaretContext(kind, replaceStart, replaceLength, typed)
            {
                QuoteIdentifiers = word != null && word.Text.StartsWith("[", StringComparison.Ordinal),
                Qualifier = qualifier,
            };

            if (unknownQualifier)
            {
                return Make(ContextKind.Name);
            }

            var anchorIndex = nameStart - 1;
            if (anchorIndex < statementStart || tokens[anchorIndex].Kind == TokenKind.Semicolon)
            {
                if (qualifier.Count > 0 || namesUnknownObject)
                {
                    return Make(ContextKind.Name);
                }

                var start = Make(ContextKind.Keyword);
                start.PreferredKeywords = KeywordMap.StatementFirst;
                start.NextKeywords = Keywords.StatementStart.Except(KeywordMap.StatementFirst).ToArray();
                return start;
            }

            // DROP TABLE IF EXISTS |: the same names as after DROP TABLE, the anchor is the kind of object.
            var afterIfExists = false;
            if (anchorIndex >= statementStart + 3 && tokens[anchorIndex].IsWord("EXISTS") && tokens[anchorIndex - 1].IsWord("IF") && tokens[statementStart].IsWord("DROP"))
            {
                anchorIndex -= 2;
                afterIfExists = true;
            }

            var structure = new Structure(tokens, statementStart, statementEnd);
            var anchor = tokens[anchorIndex];
            var scopes = structure.ScopesAt(nameStart);
            var block = scopes[0];
            var clauseState = ScanClause(structure, block.Start, nameStart);
            var anchorAtBlockLevel = structure.Depth[anchorIndex] == block.Depth;

            // CREATE TABLE and ALTER TABLE: column definitions and the actions on a table.
            var tableDdl = TableDdl.At(tokens, statementStart, anchorIndex);
            if (tableDdl != null)
            {
                return TableDdlContext(tableDdl, qualifier, Make);
            }

            // 0. The column list of INSERT INTO table (…).
            if (qualifier.Count == 0 && (anchor.Kind == TokenKind.OpenParen || anchor.Kind == TokenKind.Comma))
            {
                var insertColumns = TryInsertColumns(tokens, statementStart, anchorIndex, Make) ?? TryIndexColumns(tokens, statementStart, anchorIndex, Make);
                if (insertColumns != null)
                {
                    return insertColumns;
                }
            }

            // ALTER PROCEDURE |, CREATE OR ALTER VIEW |, DROP TRIGGER |: the modules of that kind only.
            if (qualifier.Count <= 1 && TryModuleTarget(tokens, statementStart, anchorIndex, out var moduleKind, out var moduleAction))
            {
                var module = Make(ContextKind.ObjectName);
                module.Module = moduleKind;
                module.ModuleAction = moduleAction;
                module.ModuleHeaderStart = tokens[statementStart].Start;
                module.PreferredKeywords = moduleAction == ModuleAction.Drop && !afterIfExists ? new[] { "IF EXISTS" } : Array.Empty<string>();
                module.OnlyExpectedKeywords = moduleAction == ModuleAction.Drop && !afterIfExists;
                return module;
            }

            // 1. Object name after FROM, JOIN, UPDATE, INTO …
            var objectKeywords = ObjectNameAnchor(tokens, anchorIndex, statementStart);
            if (afterIfExists && objectKeywords != null)
            {
                objectKeywords = Array.Empty<string>();
            }

            var commaInFrom = anchor.Kind == TokenKind.Comma && anchorAtBlockLevel && clauseState.InFrom && clauseState.Clause == Clause.From;
            if (objectKeywords != null || commaInFrom)
            {
                if (qualifier.Count > 3)
                {
                    // More parts than server.database.schema.: not resolved.
                    return Make(ContextKind.Name);
                }

                var objectName = Make(ContextKind.ObjectName);
                objectName.InsertTarget = (anchor.IsWord("INTO") && anchorIndex > statementStart && tokens[anchorIndex - 1].IsWord("INSERT")) || anchor.IsWord("INSERT");
                objectName.ProcedureName = anchor.IsWord("EXEC") || anchor.IsWord("EXECUTE") || anchor.Kind == TokenKind.Operator;
                objectName.PreferredKeywords = qualifier.Count == 0 ? objectKeywords ?? Array.Empty<string>() : Array.Empty<string>();

                // Not for INSERT INTO, UPDATE, DELETE FROM … and not when an alias is already typed after the name.
                var tableSource = commaInFrom || anchor.IsWord("JOIN") || anchor.IsWord("APPLY") ||
                    (anchor.IsWord("FROM") && !tokens[statementStart].IsWord("DELETE"));
                var next = caretIndex + (word != null ? 1 : 0);
                var aliasFollows = next < statementEnd && (tokens[next].Kind == TokenKind.QuotedIdentifier ||
                    (tokens[next].Kind == TokenKind.Word && !Keywords.CannotBeAlias(tokens[next].Text)) || tokens[next].IsWord("AS"));
                if (tableSource && !aliasFollows)
                {
                    objectName.AliasAllowed = true;
                    objectName.AfterJoin = anchor.IsWord("JOIN") && qualifier.Count == 0;
                    objectName.Sources = CollectSources(structure, scopes);
                    objectName.CorrelatedFrom = anchor.IsWord("FROM") && qualifier.Count == 0 && scopes.Count > 1 && !objectName.Sources.Any(src => src.ScopeLevel == 0 && !string.Equals(src.ObjectName, typed, StringComparison.OrdinalIgnoreCase));
                }

                return objectName;
            }

            if (namesUnknownObject)
            {
                // A variable being declared and a #table outside a table position are names of the user's own.
                var declaring = tokens[statementStart].IsWord("DECLARE") && (anchor.IsWord("DECLARE") || anchor.Kind == TokenKind.Comma);
                return word.Kind == TokenKind.Variable && !declaring ? Make(ContextKind.Variable) : Make(ContextKind.Name);
            }

            if (qualifier.Count == 0 &&
                ((anchor.IsWord("AS") && IsCastArgument(structure, anchorIndex)) ||
                 (anchor.Kind == TokenKind.Variable && tokens[statementStart].IsWord("DECLARE"))))
            {
                var dataType = Make(ContextKind.DataType);
                // The name whose column type is likely wanted: DECLARE @IdCustomer |, CAST(IdCustomer AS |.
                if (anchor.Kind == TokenKind.Variable)
                {
                    dataType.TypeOfName = anchor.Text.TrimStart('@');
                }
                else if (anchorIndex - 1 >= statementStart && tokens[anchorIndex - 1].IsNamePart)
                {
                    dataType.TypeOfName = tokens[anchorIndex - 1].IdentifierValue;
                }

                return dataType;
            }

            if (anchor.IsWord("AS"))
            {
                // Naming an alias.
                return Make(ContextKind.Name);
            }

            var sources = CollectSources(structure, scopes);
            if (sources.Count == 0 && IndexDdl.IndexKeyword(tokens, statementStart) >= 0 && tokens[statementStart].IsWord("CREATE"))
            {
                // The filter predicate of CREATE INDEX: the columns of the table the index is on.
                var indexTable = IndexTable(tokens, statementStart, tokens.Count, out _);
                if (indexTable != null)
                {
                    sources.Add(new TableSource(TableSourceKind.Object, indexTable, null, 0));
                }
            }

            var operandBefore = IsOperand(anchor);
            var position = qualifier.Count == 0
                ? KeywordMap.At(structure, block, nameStart, caretIndex + (word != null ? 1 : 0))
                : KeywordPosition.None;

            // CREATE / ALTER / DROP INDEX has no expressions (but the filter predicate, where the map knows nothing): the keywords of the position.
            if (!position.IsEmpty && IndexDdl.IndexKeyword(tokens, statementStart) >= 0)
            {
                var index = Make(position.Exclusive ? ContextKind.Keyword : ContextKind.Fallback);
                index.SoftSelection = true;
                index.ApplyPosition(position, false);
                return index;
            }

            // 2. After a table source in FROM: an alias or the next clause.
            if (clauseState.Clause == Clause.From && anchorAtBlockLevel && qualifier.Count == 0)
            {
                if (operandBefore || anchor.Kind == TokenKind.CloseParen)
                {
                    var after = Make(ContextKind.Keyword);
                    after.PreferredKeywords = Keywords.AfterTableSource;
                    after.SoftSelection = true;
                    after.ApplyPosition(position, false);
                    after.Sources = sources;
                    var own = sources.Where(src => src.ScopeLevel == 0 && src.Kind == TableSourceKind.Object).ToList();
                    if (scopes.Count > 1 && own.Count == 1 && sources.Any(src => src.ScopeLevel >= 1))
                    {
                        // The first table of a subquery: its correlation with the outer query is offered as a WHERE condition.
                        after.Correlation = own[0];
                    }
                    after.Merge = MergeStageOf(tokens, statementStart, position);
                    return after;
                }

                if (JoinModifiers.Contains(anchor.Upper ?? string.Empty))
                {
                    var join = Make(ContextKind.Keyword);
                    join.PreferredKeywords = new[] { "JOIN", "OUTER JOIN", "APPLY" };
                    return join;
                }
            }

            // 3. Expression.
            if (!IsExpressionClause(clauseState.Clause))
            {
                if (qualifier.Count > 0)
                {
                    return Make(ContextKind.Name);
                }

                // EXEC, VALUES, SET @x = … and other positions the extension does not understand yet.
                var fallback = Make(position.Exclusive ? ContextKind.Keyword : ContextKind.Fallback);
                fallback.SoftSelection = true;
                fallback.ApplyPosition(position, false);
                fallback.Sources = sources;
                fallback.Merge = MergeStageOf(tokens, statementStart, position);
                TryInsertTail(structure, anchorIndex, anchorAtBlockLevel, fallback);
                return fallback;
            }

            var expression = Make(ContextKind.Expression);
            expression.Sources = sources;
            expression.SoftSelection = operandBefore;
            expression.ApplyPosition(position, operandBefore || anchor.IsWord("END") || anchor.IsWord("ASC") || anchor.IsWord("DESC"));
            // OVER after a ranking function is the only thing that fits: it stays preselected.
            expression.SoftSelection = operandBefore && !(position.First.Count == 1 && position.Then.Count == 0 && position.First[0] == "OVER");
            if (qualifier.Count == 0 && clauseState.Clause == Clause.Having && (anchor.Kind == TokenKind.OpenParen || (anchor.Upper != null && ConditionStarters.Contains(anchor.Upper))))
            {
                expression.InHaving = true;
            }

            if (qualifier.Count == 0)
            {
                var operand = ConditionOperandBefore(tokens, anchorIndex, block.Start, clauseState.Clause, out var afterComparison);
                if (operand != null)
                {
                    expression.ConditionOperand = operand;
                    expression.AfterComparison = afterComparison;
                }
            }

            if (qualifier.Count == 0 && clauseState.Clause == Clause.Select && !sources.Any(src => src.ScopeLevel == 0))
            {
                expression.SelectWithoutFrom = true;
                expression.FromInsertPosition = SelectListEnd(structure, tokens, nameStart, block, caret);
            }

            if (qualifier.Count == 0 && clauseState.Clause == Clause.On && anchorAtBlockLevel &&
                (anchor.IsWord("ON") || anchor.IsWord("AND") || anchor.IsWord("OR")))
            {
                expression.JoinTarget = FindJoinTarget(structure, anchorIndex, block, sources);
            }

            if (qualifier.Count > 0)
            {
                var target = FindQualifiedSource(sources, qualifier);
                if (target == null)
                {
                    // Qualifier unknown even as an alias: schema of a function, CTE of an outer statement …
                    var unknown = Make(ContextKind.Name);
                    unknown.UnknownExpressionQualifier = qualifier.Count == 1;
                    return unknown;
                }

                expression.QualifiedSource = target;
                expression.SoftSelection = false;
            }

            expression.Merge = MergeStageOf(tokens, statementStart, position);
            return expression;
        }

        /// <summary>The table introduced by the JOIN whose ON condition contains the anchor token; null if it cannot be found.</summary>
        private static TableSource FindJoinTarget(Structure s, int anchorIndex, Scope block, IReadOnlyList<TableSource> sources)
        {
            var sawOn = false;
            for (var i = anchorIndex; i >= block.Start; i--)
            {
                if (s.Depth[i] != block.Depth)
                {
                    continue;
                }

                var upper = s.Tokens[i].Upper;
                if (upper == "ON")
                {
                    sawOn = true;
                }
                else if (sawOn && (upper == "JOIN" || upper == "APPLY"))
                {
                    return sources.FirstOrDefault(src => src.ScopeLevel == 0 && src.TokenIndex == i + 1);
                }
                else if (upper == "JOIN" || upper == "APPLY" || upper == "FROM" || upper == "WHERE")
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>MERGE: after USING source (the ON condition is offered) or after the condition or an action (the WHEN clauses).</summary>
        private static MergeStage MergeStageOf(List<Token> tokens, int statementStart, KeywordPosition position)
        {
            if (!tokens[statementStart].IsWord("MERGE") || position.IsEmpty)
            {
                return MergeStage.None;
            }

            var all = position.First.Concat(position.Then).ToList();
            if (all.Contains("WHEN MATCHED") || all.Contains("WHEN"))
            {
                return MergeStage.When;
            }

            return position.First.Contains("ON") ? MergeStage.On : MergeStage.None;
        }

        /// <summary>INSERT INTO table (columns) |: remembers the table and the listed columns, so that VALUES and SELECT can be offered filled in.</summary>
        private static void TryInsertTail(Structure s, int anchorIndex, bool anchorAtBlockLevel, CaretContext target)
        {
            var tokens = s.Tokens;
            if (!anchorAtBlockLevel || tokens[anchorIndex].Kind != TokenKind.CloseParen)
            {
                return;
            }

            var open = -1;
            var depth = 0;
            for (var i = anchorIndex - 1; i > s.StatementStart; i--)
            {
                if (tokens[i].Kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    if (depth == 0)
                    {
                        open = i;
                        break;
                    }

                    depth--;
                }
            }

            if (open < 0)
            {
                return;
            }

            var parts = new List<string>();
            var k = open - 1;
            while (k >= s.StatementStart && tokens[k].IsNamePart)
            {
                parts.Insert(0, tokens[k].IdentifierValue);
                if (k - 1 >= s.StatementStart && tokens[k - 1].Kind == TokenKind.Dot)
                {
                    k -= 2;
                }
                else
                {
                    k--;
                    break;
                }
            }

            if (parts.Count == 0 || parts.Count > 3 || k < s.StatementStart ||
                !(tokens[k].IsWord("INSERT") || (tokens[k].IsWord("INTO") && k - 1 >= s.StatementStart && tokens[k - 1].IsWord("INSERT"))))
            {
                return;
            }

            var columns = new List<string>();
            for (var j = open + 1; j < anchorIndex; j++)
            {
                if (tokens[j].IsNamePart && (tokens[j - 1].Kind == TokenKind.OpenParen || tokens[j - 1].Kind == TokenKind.Comma))
                {
                    columns.Add(tokens[j].IdentifierValue);
                }
            }

            target.InsertTail = new TableSource(TableSourceKind.Object, parts, null, 0);
            target.InsertTailColumns = columns;
        }

        private static CaretContext TableDdlContext(TableDdlPosition ddl, IReadOnlyList<string> qualifier, Func<ContextKind, CaretContext> make)
        {
            if (ddl.Kind == ContextKind.ObjectName)
            {
                if (qualifier.Count > 3)
                {
                    return make(ContextKind.Name);
                }

                var objectName = make(ContextKind.ObjectName);
                objectName.PreferredKeywords = Array.Empty<string>();
                return objectName;
            }

            if (qualifier.Count > 0 || ddl.Kind == ContextKind.Name)
            {
                var name = make(ContextKind.Name);
                if (qualifier.Count == 0)
                {
                    name.ConstraintTable = ddl.ConstraintTable;
                    name.ConstraintColumn = ddl.ConstraintColumn;
                    name.ExistingConstraintsOf = ddl.ExistingConstraintsOf;
                }

                return name;
            }

            switch (ddl.Kind)
            {
                case ContextKind.DataType:
                    var dataType = make(ContextKind.DataType);
                    dataType.TypeOfName = ddl.TypeOfName;
                    dataType.DataTypeSizes = ddl.Sizes;
                    return dataType;
                case ContextKind.InsertColumns:
                    var columns = make(ContextKind.InsertColumns);
                    columns.Sources = new[] { new TableSource(TableSourceKind.Object, ddl.Table, null, 0) };
                    columns.IndexColumns = true;
                    columns.KeyColumnsOfReference = ddl.KeyColumnsOfReference;
                    return columns;
                default:
                    var keywords = make(ddl.Keywords.Exclusive ? ContextKind.Keyword : ContextKind.Fallback);
                    keywords.SoftSelection = true;
                    keywords.ApplyPosition(ddl.Keywords, false);
                    return keywords;
            }
        }

        private static readonly string[] ComparisonOperators ={ "=", "<>", "!=", "<", ">", "<=", ">=", "!<", "!>" };

        private static readonly HashSet<string> ConditionStarters = new HashSet<string>(StringComparer.Ordinal) { "WHERE", "AND", "OR", "ON", "HAVING", "WHEN", "NOT" };

        /// <summary>
        /// In a condition: the column the caret follows (<c>WHERE col |</c>, <c>AND a.col |</c>, <c>CASE WHEN col |</c>) or, with
        /// <paramref name="afterComparison"/>, the column left of the comparison operator the caret follows (<c>WHERE col = |</c>). Null elsewhere.
        /// </summary>
        private static List<string> ConditionOperandBefore(List<Token> tokens, int anchorIndex, int blockStart, Clause clause, out bool afterComparison)
        {
            afterComparison = false;
            var i = anchorIndex;
            if (i >= blockStart && tokens[i].IsWord("LIKE"))
            {
                afterComparison = true;
                i--;
            }
            else if (i >= blockStart && tokens[i].Kind == TokenKind.Operator && ComparisonOperators.Contains(tokens[i].Text))
            {
                // The lexer may split >= into two operator tokens.
                afterComparison = true;
                i--;
                while (i >= blockStart && tokens[i].Kind == TokenKind.Operator && (tokens[i].Text == "<" || tokens[i].Text == ">" || tokens[i].Text == "!" || tokens[i].Text == "="))
                {
                    i--;
                }
            }

            if (i < blockStart || !tokens[i].IsNamePart || (tokens[i].Kind == TokenKind.Word && Keywords.CannotBeAlias(tokens[i].Text)))
            {
                return null;
            }

            var parts = new List<string> { tokens[i].IdentifierValue };
            while (i - 2 >= blockStart && tokens[i - 1].Kind == TokenKind.Dot && tokens[i - 2].IsNamePart)
            {
                parts.Insert(0, tokens[i - 2].IdentifierValue);
                i -= 2;
            }

            var before = i - 1;
            while (before >= blockStart && tokens[before].Kind == TokenKind.OpenParen)
            {
                before--;
            }

            if (before < blockStart || tokens[before].Upper == null || !ConditionStarters.Contains(tokens[before].Upper))
            {
                return null;
            }

            var condition = clause == Clause.Where || clause == Clause.On || clause == Clause.Having;
            return condition || tokens[before].IsWord("WHEN") ? parts : null;
        }

        /// <summary>AS inside CAST(…) or TRY_CAST(…), followed by a data type.</summary>
        private static bool IsCastArgument(Structure s, int asIndex)
        {
            var depth = 0;
            for (var i = asIndex - 1; i >= s.StatementStart; i--)
            {
                var kind = s.Tokens[i].Kind;
                if (kind == TokenKind.CloseParen)
                {
                    depth++;
                }
                else if (kind == TokenKind.OpenParen)
                {
                    if (depth == 0)
                    {
                        return i > s.StatementStart && (s.Tokens[i - 1].IsWord("CAST") || s.Tokens[i - 1].IsWord("TRY_CAST"));
                    }

                    depth--;
                }
            }

            return false;
        }

        /// <summary>
        /// The code tokens of a script without the comments. When the caret stands right after a comment (only white space between them) that begins on a line of
        /// its own, the comment ends the statement before it as far as the caret is concerned: a token of no width, a semicolon, is put in front of the comment, so
        /// that the caret is at the start of a statement and not at the continuation of the one above the comment (#412). A comment on the line of the previous
        /// token (<c>SELECT a /* c */ |FROM</c>) is part of the statement.
        /// </summary>
        private static List<Token> CodeTokens(string text, IReadOnlyList<Token> all, int caret)
        {
            var tokens = all.Where(t => t.Kind != TokenKind.Comment).ToList();
            var last = -1;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Kind == TokenKind.Comment && all[i].End <= caret && IsBlank(text, all[i].End, caret))
                {
                    last = i;
                }
            }

            if (last < 0)
            {
                return tokens;
            }

            // The run of comments that ends at the caret: the first one decides.
            var first = last;
            while (first > 0 && all[first - 1].Kind == TokenKind.Comment && IsBlank(text, all[first - 1].End, all[first].Start))
            {
                first--;
            }

            var runStart = all[first].Start;
            var previous = tokens.FindLastIndex(t => t.End <= runStart);
            if (previous < 0 || tokens[previous].Kind == TokenKind.Semicolon)
            {
                return tokens;
            }

            var onItsOwnLine = false;
            for (var i = tokens[previous].End; i < runStart; i++)
            {
                if (text[i] == '\n' || text[i] == '\r')
                {
                    onItsOwnLine = true;
                    break;
                }
            }

            if (onItsOwnLine)
            {
                tokens.Insert(previous + 1, new Token(TokenKind.Semicolon, runStart, runStart, string.Empty, false));
            }

            return tokens;
        }

        private static bool IsBlank(string text, int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(Token token, int caret)
        {
            if (caret <= token.Start)
            {
                return false;
            }

            return caret < token.End || (caret == token.End && (token.IsUnterminated || token.IsLineComment));
        }

        /// <summary>
        /// A join whose <c>ON</c> is missing (<c>JOIN t AS x a.Id = |</c>, the keyword was deleted or not typed yet, #358): a name that cannot be an alias,
        /// because it has a dot, stands right after the source of the join and is followed by a comparison. Such a condition is analyzed as if
        /// <c>ON</c> stood before it (a token of no width, so every position in the text stays the same).
        /// </summary>
        private static List<Token> WithMissingOn(List<Token> tokens)
        {
            List<Token> result = null;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].IsWord("JOIN"))
                {
                    continue;
                }

                // The source: name [. [.] name]...
                var p = i + 1;
                while (true)
                {
                    if (p >= tokens.Count || !tokens[p].IsNamePart)
                    {
                        p = -1;
                        break;
                    }

                    p++;
                    if (p < tokens.Count && tokens[p].Kind == TokenKind.Dot)
                    {
                        p++;
                        while (p < tokens.Count && tokens[p].Kind == TokenKind.Dot)
                        {
                            p++;
                        }

                        continue;
                    }

                    break;
                }

                if (p < 0)
                {
                    continue;
                }

                // The alias: AS name, or a name that is not the start of a qualified name.
                var q = p;
                if (p < tokens.Count && tokens[p].IsWord("AS"))
                {
                    q = p + 2;
                }
                else if (p < tokens.Count && tokens[p].IsIdentifier && !(p + 1 < tokens.Count && tokens[p + 1].Kind == TokenKind.Dot))
                {
                    q = p + 1;
                }

                // The condition: a qualified name, then a comparison or nothing yet (the caret is in the name).
                if (q + 1 >= tokens.Count || !tokens[q].IsIdentifier || tokens[q + 1].Kind != TokenKind.Dot)
                {
                    continue;
                }

                var e = q;
                while (e + 1 < tokens.Count && tokens[e + 1].Kind == TokenKind.Dot && e + 2 < tokens.Count && tokens[e + 2].IsNamePart)
                {
                    e += 2;
                }

                var comparison = e + 1 < tokens.Count && tokens[e + 1].Kind == TokenKind.Operator && ComparisonOperators.Contains(tokens[e + 1].Text);
                var typing = e + 1 >= tokens.Count || tokens[e + 1].Kind == TokenKind.Dot;
                if (!comparison && !typing)
                {
                    continue;
                }

                result = result ?? new List<Token>(tokens);
                var at = result.IndexOf(tokens[q]);
                result.Insert(at, new Token(TokenKind.Word, tokens[q].Start, tokens[q].Start, "ON", false));
            }

            return result ?? tokens;
        }

        private static int CountBefore(List<Token> tokens, int caret)
        {
            var count = 0;
            while (count < tokens.Count && tokens[count].End <= caret)
            {
                count++;
            }

            return count;
        }

        /// <summary>A value or identifier that may be followed by an alias or an operator.</summary>
        internal static bool IsOperand(Token token)
        {
            switch (token.Kind)
            {
                case TokenKind.QuotedIdentifier:
                case TokenKind.Variable:
                case TokenKind.String:
                case TokenKind.Number:
                case TokenKind.CloseParen:
                    return true;
                case TokenKind.Word:
                    return !Keywords.IsReserved(token.Text) || token.IsWord("NULL");
                default:
                    return false;
            }
        }

        private static bool IsExpressionClause(Clause clause)
        {
            switch (clause)
            {
                case Clause.Select:
                case Clause.On:
                case Clause.Where:
                case Clause.GroupBy:
                case Clause.Having:
                case Clause.OrderBy:
                case Clause.Set:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// If a table name follows the token, returns the keywords that make sense there
        /// (e.g. FROM after DELETE). Otherwise null.
        /// </summary>
        private static IReadOnlyList<string> ObjectNameAnchor(List<Token> tokens, int anchorIndex, int statementStart)
        {
            var anchor = tokens[anchorIndex];
            var prev = anchorIndex > statementStart ? tokens[anchorIndex - 1] : null;
            var prevWord = prev?.Upper;
            if (anchor.Kind == TokenKind.Operator && anchor.Text == "=" && anchorIndex - 2 >= statementStart &&
                tokens[anchorIndex - 1].Kind == TokenKind.Variable && (tokens[anchorIndex - 2].IsWord("EXEC") || tokens[anchorIndex - 2].IsWord("EXECUTE")))
            {
                // EXEC @result = procedure
                return Array.Empty<string>();
            }

            switch (anchor.Upper)
            {
                case "EXEC":
                case "EXECUTE":
                    return Array.Empty<string>();
                case "FROM":
                case "JOIN":
                case "APPLY":
                case "USING":
                    return Array.Empty<string>();
                case "UPDATE":
                    // Not ON UPDATE CASCADE, FOR UPDATE, AFTER INSERT, UPDATE …
                    var isDml = prev == null || (prev.Kind != TokenKind.Comma && prevWord != "ON" && prevWord != "FOR" &&
                                                 prevWord != "AFTER" && prevWord != "OF" && prevWord != "THEN");
                    return isDml ? Array.Empty<string>() : null;
                case "INTO":
                    return prevWord == "INSERT" || prevWord == "MERGE" ? Array.Empty<string>() : null;
                case "INSERT":
                    return new[] { "INTO" };
                case "MERGE":
                    return new[] { "INTO" };
                case "DELETE":
                    return anchorIndex == statementStart ? new[] { "FROM", "TOP" } : null;
                case "ON":
                    // CREATE / ALTER / DROP INDEX name ON table: the first ON, before any parenthesis (a later ON names a filegroup).
                    return IndexDdl.IndexKeyword(tokens, statementStart) >= 0 && !tokens.Skip(statementStart).Take(anchorIndex - statementStart).Any(t => t.Kind == TokenKind.OpenParen)
                        ? Array.Empty<string>()
                        : null;
                case "IDENTITY_INSERT":
                    return prevWord == "SET" ? Array.Empty<string>() : null;
                case "TABLE":
                    return prevWord == "DROP" ? new[] { "IF EXISTS" } : prevWord == "TRUNCATE" || prevWord == "ALTER" ? Array.Empty<string>() : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// "INSERT INTO table (|" or "INSERT INTO table (a, |": the columns of the table. Null when the caret is somewhere else.
        /// </summary>
        private static CaretContext TryInsertColumns(List<Token> tokens, int statementStart, int anchorIndex, Func<ContextKind, CaretContext> make)
        {
            var open = anchorIndex;
            if (tokens[anchorIndex].Kind == TokenKind.Comma)
            {
                var j = anchorIndex - 1;
                while (j > statementStart && (tokens[j].IsNamePart || tokens[j].Kind == TokenKind.Comma))
                {
                    j--;
                }

                if (tokens[j].Kind != TokenKind.OpenParen)
                {
                    return null;
                }

                open = j;
            }

            var parts = new List<string>();
            var k = open - 1;
            while (k >= statementStart && tokens[k].IsNamePart)
            {
                parts.Insert(0, tokens[k].IdentifierValue);
                if (k - 1 >= statementStart && tokens[k - 1].Kind == TokenKind.Dot)
                {
                    k -= 2;
                }
                else
                {
                    k--;
                    break;
                }
            }

            if (parts.Count == 0 || parts.Count > 3 || k < statementStart)
            {
                return null;
            }

            var isInsert = tokens[k].IsWord("INSERT") || (tokens[k].IsWord("INTO") && k - 1 >= statementStart && tokens[k - 1].IsWord("INSERT"));
            if (!isInsert)
            {
                return null;
            }

            var context = make(ContextKind.InsertColumns);
            context.Sources = new[] { new TableSource(TableSourceKind.Object, parts, null, 0) };
            context.FirstInsertColumn = tokens[anchorIndex].Kind == TokenKind.OpenParen;
            return context;
        }

        /// <summary>The caret is where the name of a procedure, function, view or trigger goes after ALTER, CREATE OR ALTER or DROP.</summary>
        private static bool TryModuleTarget(List<Token> tokens, int statementStart, int anchorIndex, out ModuleKind kind, out ModuleAction action)
        {
            kind = ModuleKind.None;
            action = ModuleAction.None;
            switch (tokens[anchorIndex].Upper)
            {
                case "PROCEDURE":
                case "PROC":
                    kind = ModuleKind.Procedure;
                    break;
                case "FUNCTION":
                    kind = ModuleKind.Function;
                    break;
                case "VIEW":
                    kind = ModuleKind.View;
                    break;
                case "TRIGGER":
                    kind = ModuleKind.Trigger;
                    break;
                case "INDEX":
                    kind = ModuleKind.Index;
                    break;
                default:
                    return false;
            }

            var head = tokens[statementStart].Upper;
            if (head == "ALTER" && anchorIndex == statementStart + 1)
            {
                action = ModuleAction.Alter;
            }
            else if (head == "CREATE" && anchorIndex == statementStart + 3 && tokens[statementStart + 1].IsWord("OR") && tokens[statementStart + 2].IsWord("ALTER"))
            {
                action = ModuleAction.CreateOrAlter;
            }
            else if (head == "DROP" && anchorIndex == statementStart + 1)
            {
                action = ModuleAction.Drop;
            }

            // CREATE OR ALTER does not exist for indexes.
            if (kind == ModuleKind.Index && action == ModuleAction.CreateOrAlter)
            {
                action = ModuleAction.None;
            }

            return action != ModuleAction.None;
        }

        /// <summary>
        /// The column lists of CREATE INDEX name ON table (key columns) INCLUDE (columns): the columns of the table, without those listed
        /// already (the INCLUDE list also without the key columns). Null when the caret is somewhere else.
        /// </summary>
        private static CaretContext TryIndexColumns(List<Token> tokens, int statementStart, int anchorIndex, Func<ContextKind, CaretContext> make)
        {
            if (IndexDdl.IndexKeyword(tokens, statementStart) < 0 || !tokens[statementStart].IsWord("CREATE"))
            {
                return null;
            }

            var open = anchorIndex;
            if (tokens[anchorIndex].Kind == TokenKind.Comma)
            {
                var j = anchorIndex - 1;
                while (j > statementStart && (tokens[j].IsNamePart || tokens[j].Kind == TokenKind.Comma))
                {
                    j--;
                }

                if (tokens[j].Kind != TokenKind.OpenParen)
                {
                    return null;
                }

                open = j;
            }

            var parts = IndexTable(tokens, statementStart, open, out var k);
            if (parts == null)
            {
                return null;
            }
            var excluded = new List<string>();
            if (k != open)
            {
                // Only INCLUDE ( may follow the key columns; those are not offered again.
                if (!tokens[open - 1].IsWord("INCLUDE") || tokens[k].Kind != TokenKind.OpenParen)
                {
                    return null;
                }

                excluded.AddRange(ListedColumns(tokens, k, int.MaxValue));
            }

            excluded.AddRange(ListedColumns(tokens, open, anchorIndex));
            var context = make(ContextKind.InsertColumns);
            context.Sources = new[] { new TableSource(TableSourceKind.Object, parts, null, 0) };
            context.FirstInsertColumn = tokens[anchorIndex].Kind == TokenKind.OpenParen;
            context.IndexColumns = true;
            context.ExcludedColumns = excluded;
            return context;
        }

        /// <summary>The table of CREATE INDEX name ON table: the name after the first ON, before any parenthesis and before <paramref name="limit"/>; null when there is none.</summary>
        /// <param name="end">The index of the token after the name.</param>
        private static List<string> IndexTable(List<Token> tokens, int statementStart, int limit, out int end)
        {
            end = -1;
            var on = -1;
            for (var i = statementStart; i < limit && i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    break;
                }

                if (tokens[i].IsWord("ON"))
                {
                    on = i;
                    break;
                }
            }

            if (on < 0)
            {
                return null;
            }

            var parts = new List<string>();
            var k = on + 1;
            while (k < limit && k < tokens.Count && (tokens[k].IsNamePart || tokens[k].Kind == TokenKind.Dot))
            {
                if (tokens[k].IsNamePart)
                {
                    parts.Add(tokens[k].IdentifierValue);
                }

                k++;
            }

            end = k;
            return parts.Count == 0 || parts.Count > 3 ? null : parts;
        }
        /// <summary>The column names listed in the parentheses that open at <paramref name="open"/> (up to the token <paramref name="last"/> or the closing parenthesis).</summary>
        private static IEnumerable<string> ListedColumns(List<Token> tokens, int open, int last)
        {
            for (var i = open + 1; i < tokens.Count && i <= last && tokens[i].Kind != TokenKind.CloseParen; i++)
            {
                if (tokens[i].IsNamePart && (tokens[i - 1].Kind == TokenKind.OpenParen || tokens[i - 1].Kind == TokenKind.Comma))
                {
                    yield return tokens[i].IdentifierValue;
                }
            }
        }

        private static TableSource FindQualifiedSource(IReadOnlyList<TableSource> sources, IReadOnlyList<string> qualifier)
        {
            if (qualifier.Count == 1)
            {
                var name = qualifier[0];
                return sources.FirstOrDefault(s => string.Equals(s.Alias, name, StringComparison.OrdinalIgnoreCase))
                    ?? sources.FirstOrDefault(s => s.Alias == null && string.Equals(s.ObjectName, name, StringComparison.OrdinalIgnoreCase));
            }

            if (qualifier.Count == 2)
            {
                return sources.FirstOrDefault(s => s.Alias == null &&
                    string.Equals(s.Schema, qualifier[0], StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.ObjectName, qualifier[1], StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private static readonly HashSet<string> ClauseAfterSelectList = new HashSet<string>(StringComparer.Ordinal)
        {
            "WHERE", "GROUP", "HAVING", "ORDER", "OPTION", "FOR",
        };

        /// <summary>Offset after the last token of the select list that contains the caret (where a missing FROM belongs).</summary>
        private static int SelectListEnd(Structure s, List<Token> tokens, int fromIndex, Scope block, int caret)
        {
            var end = caret;
            for (var i = fromIndex; i < block.End && i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (s.Depth[i] == block.Depth && (token.Kind == TokenKind.Semicolon || (token.Upper != null && ClauseAfterSelectList.Contains(token.Upper))))
                {
                    break;
                }

                end = Math.Max(end, token.End);
            }

            return end;
        }

        private struct ClauseState
        {
            public Clause Clause;
            public bool InFrom;
        }

        /// <summary>Scans the block from its start to the caret; skips nested parentheses except those containing the caret.</summary>
        private static ClauseState ScanClause(Structure s, int from, int to)
        {
            var state = new ClauseState();
            var sawUpdate = false;
            var i = from;
            while (i < to)
            {
                var token = s.Tokens[i];
                if (token.Kind == TokenKind.OpenParen)
                {
                    var close = s.Match[i];
                    i = close >= 0 && close < to ? close + 1 : i + 1;
                    continue;
                }

                switch (token.Upper)
                {
                    case "SELECT":
                        state.Clause = Clause.Select;
                        state.InFrom = false;
                        break;
                    case "FROM":
                        state.Clause = Clause.From;
                        state.InFrom = true;
                        break;
                    case "JOIN":
                    case "APPLY":
                    case "USING":
                        state.Clause = Clause.From;
                        break;
                    case "ON":
                        state.Clause = Clause.On;
                        break;
                    case "WHERE":
                        state.Clause = Clause.Where;
                        state.InFrom = false;
                        break;
                    case "GROUP":
                        state.Clause = Clause.GroupBy;
                        state.InFrom = false;
                        break;
                    case "HAVING":
                        state.Clause = Clause.Having;
                        state.InFrom = false;
                        break;
                    case "ORDER":
                    case "PARTITION":
                        state.Clause = Clause.OrderBy;
                        state.InFrom = false;
                        break;
                    case "UPDATE":
                        sawUpdate = true;
                        state.Clause = Clause.Other;
                        break;
                    case "SET":
                        state.Clause = sawUpdate ? Clause.Set : Clause.Other;
                        break;
                    case "VALUES":
                    case "INTO":
                    case "OUTPUT":
                    case "OPTION":
                    case "FOR":
                    case "INSERT":
                    case "DELETE":
                    case "MERGE":
                    case "DECLARE":
                    case "EXEC":
                    case "EXECUTE":
                        state.Clause = Clause.Other;
                        state.InFrom = false;
                        break;
                }

                i++;
            }

            return state;
        }

        private static List<TableSource> CollectSources(Structure s, IReadOnlyList<Scope> scopes)
        {
            var result = new List<TableSource>();
            for (var level = 0; level < scopes.Count; level++)
            {
                var scope = scopes[level];
                result.AddRange(ParseSources(s, scope.Start, scope.End, scope.Depth, level));
                if (!scope.SeesOuter)
                {
                    break;
                }
            }

            return result;
        }

        internal static List<TableSource> ParseSources(Structure s, int from, int to, int depth, int level)
        {
            var sources = new List<TableSource>();
            var tokens = s.Tokens;
            var inFrom = false;
            for (var i = from; i < to; i++)
            {
                var token = tokens[i];
                if (s.Depth[i] != depth)
                {
                    continue;
                }

                if (token.Kind == TokenKind.OpenParen)
                {
                    var close = s.Match[i];
                    if (close < 0 || close >= to)
                    {
                        break;
                    }

                    i = close;
                    continue;
                }

                if (token.Kind == TokenKind.Comma)
                {
                    if (inFrom)
                    {
                        i = ParseReference(s, i + 1, to, depth, level, sources) - 1;
                    }

                    continue;
                }

                var prevWord = i > from ? tokens[i - 1].Upper : null;
                switch (token.Upper)
                {
                    case "FROM":
                        inFrom = true;
                        i = ParseReference(s, i + 1, to, depth, level, sources) - 1;
                        break;
                    case "JOIN":
                    case "APPLY":
                    case "USING":
                        i = ParseReference(s, i + 1, to, depth, level, sources) - 1;
                        break;
                    case "UPDATE":
                        if (prevWord != "ON" && prevWord != "FOR" && prevWord != "AFTER" && prevWord != "OF" &&
                            (i == from || tokens[i - 1].Kind != TokenKind.Comma))
                        {
                            var next = i + 1;
                            if (next < to && tokens[next].IsWord("TOP"))
                            {
                                next++;
                                if (next < to && tokens[next].Kind == TokenKind.OpenParen && s.Match[next] > 0)
                                {
                                    next = s.Match[next] + 1;
                                }
                            }

                            i = ParseReference(s, next, to, depth, level, sources) - 1;
                        }

                        break;
                    case "MERGE":
                        var target = i + 1;
                        if (target < to && tokens[target].IsWord("INTO"))
                        {
                            target++;
                        }

                        i = ParseReference(s, target, to, depth, level, sources) - 1;
                        break;
                    case "WHERE":
                    case "GROUP":
                    case "HAVING":
                    case "ORDER":
                    case "OPTION":
                    case "FOR":
                    case "UNION":
                    case "EXCEPT":
                    case "INTERSECT":
                    case "WINDOW":
                    case "SET":
                        inFrom = false;
                        break;
                }
            }

            // UPDATE u SET … FROM dbo.Users u: the "u" after UPDATE refers to the alias, it is not another source.
            sources.RemoveAll(src => src.Kind == TableSourceKind.Object && src.Alias == null && src.NameParts.Count == 1 &&
                sources.Any(other => other != src && string.Equals(other.Alias, src.ObjectName, StringComparison.OrdinalIgnoreCase)));
            return sources;
        }

        /// <summary>Reads one table source starting at index start and returns the index after it.</summary>
        private static int ParseReference(Structure s, int start, int to, int depth, int level, List<TableSource> sources)
        {
            var tokens = s.Tokens;
            var i = start;
            if (i >= to)
            {
                return i;
            }

            TableSourceKind kind;
            var parts = new List<string>();
            var bodyStart = -1;
            var bodyEnd = -1;
            var first = tokens[i];
            if (first.Kind == TokenKind.OpenParen)
            {
                var close = s.Match[i];
                if (close < 0 || close >= to)
                {
                    return to;
                }

                kind = TableSourceKind.Derived;
                bodyStart = first.End;
                bodyEnd = tokens[close].Start;
                i = close + 1;
            }
            else if (first.Kind == TokenKind.Variable)
            {
                kind = TableSourceKind.Variable;
                parts.Add(first.Text);
                i++;
            }
            else if (first.IsIdentifier || (first.Kind == TokenKind.Word && first.Text.StartsWith("#", StringComparison.Ordinal)))
            {
                parts.Add(first.IdentifierValue);
                i++;
                while (i + 1 < to && tokens[i].Kind == TokenKind.Dot)
                {
                    if (tokens[i + 1].Kind == TokenKind.Dot)
                    {
                        parts.Add(string.Empty);
                        i++;
                        continue;
                    }

                    if (!tokens[i + 1].IsNamePart)
                    {
                        break;
                    }

                    parts.Add(tokens[i + 1].IdentifierValue);
                    i += 2;
                }

                if (i < to && tokens[i].Kind == TokenKind.OpenParen)
                {
                    // Table-valued function: dbo.fn(…)
                    var close = s.Match[i];
                    i = close >= 0 && close < to ? close + 1 : to;
                }

                kind = parts[parts.Count - 1].StartsWith("#", StringComparison.Ordinal) ? TableSourceKind.TempTable : TableSourceKind.Object;
            }
            else
            {
                return i;
            }

            string alias = null;
            if (i < to && tokens[i].IsWord("AS"))
            {
                i++;
            }

            if (i < to && ((tokens[i].Kind == TokenKind.Word && !Keywords.CannotBeAlias(tokens[i].Text)) ||
                           tokens[i].Kind == TokenKind.QuotedIdentifier))
            {
                alias = tokens[i].IdentifierValue;
                i++;
            }

            List<string> columnNames = null;
            if (i < to && kind == TableSourceKind.Derived && tokens[i].Kind == TokenKind.OpenParen && s.Match[i] > 0)
            {
                // (SELECT …) AS d (a, b)
                var close = Math.Min(to, s.Match[i]);
                columnNames = new List<string>();
                for (var k = i + 1; k < close; k++)
                {
                    if (tokens[k].IsNamePart)
                    {
                        columnNames.Add(tokens[k].IdentifierValue);
                    }
                }

                i = Math.Min(to, s.Match[i] + 1);
            }

            sources.Add(new TableSource(kind, parts, alias, level, bodyStart, bodyEnd, columnNames) { TokenIndex = start });
            return i;
        }

        /// <summary>Token range searched for sources: a query block at one parenthesis level.</summary>
        internal sealed class Scope
        {
            public int Start;
            public int End;
            public int Depth;

            /// <summary>The block sees the sources of the enclosing query (subquery in an expression or in APPLY).</summary>
            public bool SeesOuter;
        }

        /// <summary>Parentheses and token depths of one statement.</summary>
        internal sealed class Structure
        {
            public Structure(List<Token> tokens, int start, int end)
                : this(tokens, start, end, new int[tokens.Count], Enumerable.Repeat(-1, tokens.Count).ToArray())
            {
            }

            /// <summary>Fills arrays shared by several structures over disjoint ranges of the same tokens.</summary>
            public Structure(List<Token> tokens, int start, int end, int[] depth, int[] match)
            {
                Tokens = tokens;
                StatementStart = start;
                StatementEnd = end;
                Depth = depth;
                Match = match;
                var stack = new Stack<int>();
                for (var i = start; i < end; i++)
                {
                    Depth[i] = stack.Count;
                    if (tokens[i].Kind == TokenKind.OpenParen)
                    {
                        stack.Push(i);
                    }
                    else if (tokens[i].Kind == TokenKind.CloseParen && stack.Count > 0)
                    {
                        var open = stack.Pop();
                        Match[open] = i;
                        Depth[i] = stack.Count;
                    }
                }
            }

            public List<Token> Tokens { get; }

            public int StatementStart { get; }

            public int StatementEnd { get; }

            public int[] Depth { get; }

            /// <summary>For "(" the index of the matching ")", otherwise -1.</summary>
            public int[] Match { get; }

            /// <summary>Blocks containing the position, innermost first.</summary>
            public IReadOnlyList<Scope> ScopesAt(int position)
            {
                var open = new List<int>();
                for (var i = StatementStart; i < position && i < StatementEnd; i++)
                {
                    if (Tokens[i].Kind == TokenKind.OpenParen && (Match[i] < 0 || Match[i] >= position))
                    {
                        open.Add(i);
                    }
                }

                var scopes = new List<Scope>();
                var current = position;
                for (var k = open.Count - 1; k >= 0; k--)
                {
                    var paren = open[k];
                    if (paren + 1 >= current || !IsQueryStart(Tokens[paren + 1]))
                    {
                        continue;
                    }

                    var regionEnd = Match[paren] >= 0 ? Match[paren] : StatementEnd;
                    var scope = BlockAt(paren + 1, regionEnd, Depth[paren] + 1, current);
                    scope.SeesOuter = SeesOuter(paren);
                    scopes.Add(scope);
                    current = paren;
                }

                scopes.Add(BlockAt(StatementStart, StatementEnd, 0, current));
                return scopes;
            }

            private static bool IsQueryStart(Token token) => token.IsWord("SELECT") || token.IsWord("WITH");

            /// <summary>A derived table in FROM and a CTE body do not see the outer query's sources; a subquery in an expression does.</summary>
            private bool SeesOuter(int paren)
            {
                if (paren <= StatementStart)
                {
                    return true;
                }

                var before = Tokens[paren - 1];
                if (before.IsWord("FROM") || before.IsWord("JOIN") || before.IsWord("AS"))
                {
                    return false;
                }

                return !(before.Kind == TokenKind.Comma && Depth[paren - 1] == Depth[paren] && InFromList(paren));
            }

            private bool InFromList(int index)
            {
                for (var i = index - 1; i >= StatementStart; i--)
                {
                    if (Depth[i] != Depth[index])
                    {
                        continue;
                    }

                    var word = Tokens[i].Upper;
                    if (word == "FROM")
                    {
                        return true;
                    }

                    if (word == "SELECT" || word == "WHERE" || word == "SET")
                    {
                        return false;
                    }
                }

                return false;
            }

            /// <summary>Finds the block between UNION/EXCEPT/INTERSECT in [start, end) that contains the position.</summary>
            private Scope BlockAt(int start, int end, int depth, int position)
            {
                var blockStart = start;
                var blockEnd = end;
                for (var i = start; i < end; i++)
                {
                    if (Depth[i] != depth)
                    {
                        continue;
                    }

                    var word = Tokens[i].Upper;
                    if (word != "UNION" && word != "EXCEPT" && word != "INTERSECT")
                    {
                        continue;
                    }

                    if (i < position)
                    {
                        blockStart = i + 1;
                        if (blockStart < end && Tokens[blockStart].IsWord("ALL"))
                        {
                            blockStart++;
                        }
                    }
                    else
                    {
                        blockEnd = i;
                        break;
                    }
                }

                return new Scope { Start = blockStart, End = blockEnd, Depth = depth };
            }
        }
    }
}
