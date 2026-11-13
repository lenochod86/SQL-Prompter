using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>Where in a MERGE statement the caret is, for the clauses offered as a whole.</summary>
    public enum MergeStage
    {
        None,

        /// <summary>After <c>USING source</c>: the ON condition.</summary>
        On,

        /// <summary>After the ON condition or a WHEN clause: the WHEN clauses.</summary>
        When,
    }

    /// <summary>The kind of programmable object after ALTER, CREATE OR ALTER or DROP.</summary>
    public enum ModuleKind
    {
        None,
        Procedure,
        Function,
        View,
        Trigger,

        /// <summary>The name of an index after ALTER INDEX or DROP INDEX (#200).</summary>
        Index,
    }

    public enum ModuleAction
    {
        None,
        Alter,
        CreateOrAlter,
        Drop,
    }

    public enum ContextKind
    {
        /// <summary>No completion: string, comment, number.</summary>
        None,

        /// <summary>
        /// The user is typing a new or unknown name (alias after AS, variable, #table, unknown qualifier).
        /// The list is empty, but the extension participates so that the built-in list is not shown.
        /// </summary>
        Name,

        /// <summary>Data type: CAST(x AS …), DECLARE @x ….</summary>
        DataType,

        /// <summary>A position the extension does not understand yet: keywords, functions and data types.</summary>
        Fallback,

        /// <summary>Keywords only: start of a statement or after a table source.</summary>
        Keyword,

        /// <summary>Name of a table or view (after FROM, JOIN, UPDATE, INTO …).</summary>
        ObjectName,

        /// <summary>Expression: columns, aliases, functions and keywords.</summary>
        Expression,

        /// <summary>A variable or parameter (@name) is being typed.</summary>
        Variable,

        /// <summary>The column list of INSERT INTO table (…): the columns of <see cref="CaretContext.Sources"/> (one table) are offered.</summary>
        InsertColumns,
    }

    public enum TableSourceKind
    {
        /// <summary>Named table, view or table-valued function.</summary>
        Object,

        /// <summary>Derived table (SELECT …) alias.</summary>
        Derived,

        /// <summary>Table variable @t.</summary>
        Variable,

        /// <summary>Temporary table #t.</summary>
        TempTable,
    }

    /// <summary>Row source in FROM, JOIN, APPLY, UPDATE or MERGE.</summary>
    public sealed class TableSource
    {
        public TableSource(TableSourceKind kind, IReadOnlyList<string> nameParts, string alias, int scopeLevel)
            : this(kind, nameParts, alias, scopeLevel, -1, -1, null)
        {
        }

        public TableSource(
            TableSourceKind kind,
            IReadOnlyList<string> nameParts,
            string alias,
            int scopeLevel,
            int bodyStart,
            int bodyEnd,
            IReadOnlyList<string> columnNames)
        {
            Kind = kind;
            NameParts = nameParts ?? Array.Empty<string>();
            Alias = alias;
            ScopeLevel = scopeLevel;
            BodyStart = bodyStart;
            BodyEnd = bodyEnd;
            ColumnNames = columnNames;
        }

        /// <summary>Derived table: start of the text between the parentheses, otherwise -1.</summary>
        public int BodyStart { get; }

        /// <summary>Derived table: end of the text between the parentheses, otherwise -1.</summary>
        public int BodyEnd { get; }

        /// <summary>Explicit column names of a derived table, (SELECT …) AS d (a, b); otherwise null.</summary>
        public IReadOnlyList<string> ColumnNames { get; }

        public TableSourceKind Kind { get; }

        /// <summary>Name parts, e.g. [dbo, Users]. Empty for a derived table.</summary>
        public IReadOnlyList<string> NameParts { get; }

        public string Alias { get; }

        /// <summary>0 = block containing the caret, 1 = enclosing query (correlated subquery) and so on.</summary>
        public int ScopeLevel { get; }

        /// <summary>Index of the first token of the reference in the statement; orders the sources of a block by position.</summary>
        internal int TokenIndex { get; set; } = -1;

        public string ObjectName => NameParts.Count > 0 ? NameParts[NameParts.Count - 1] : null;

        public string Schema => NameParts.Count > 1 ? NameParts[NameParts.Count - 2] : null;

        public string Database => NameParts.Count > 2 ? NameParts[NameParts.Count - 3] : null;

        /// <summary>How an expression refers to the source: the alias, otherwise the object name.</summary>
        public string ReferenceName => Alias ?? ObjectName;

        public override string ToString() => string.Join(".", NameParts) + (Alias != null ? " " + Alias : string.Empty) + " @" + ScopeLevel;
    }

    /// <summary>Result of analyzing the text at the caret position.</summary>
    public sealed class CaretContext
    {
        public static readonly CaretContext None = new CaretContext(ContextKind.None, 0, 0, string.Empty);

        public CaretContext(ContextKind kind, int replaceStart, int replaceLength, string typedPrefix)
        {
            Kind = kind;
            ReplaceStart = replaceStart;
            ReplaceLength = replaceLength;
            TypedPrefix = typedPrefix ?? string.Empty;
        }

        public ContextKind Kind { get; }

        /// <summary>Start of the span a suggestion replaces (the word at the caret).</summary>
        public int ReplaceStart { get; }

        public int ReplaceLength { get; }

        /// <summary>Text of the word from its start to the caret.</summary>
        public string TypedPrefix { get; }

        /// <summary>The user started the identifier with "[", so it is always inserted in brackets.</summary>
        public bool QuoteIdentifiers { get; internal set; }

        /// <summary>Name parts before the word at the caret, e.g. [dbo] for "dbo.Us|".</summary>
        public IReadOnlyList<string> Qualifier { get; internal set; } = Array.Empty<string>();

        /// <summary>Table sources visible from the caret, nearest block first.</summary>
        public IReadOnlyList<TableSource> Sources { get; internal set; } = Array.Empty<TableSource>();

        /// <summary>
        /// A table is being chosen for FROM, JOIN or APPLY and nothing follows it, so an alias can be inserted with it.
        /// </summary>
        public bool AliasAllowed { get; internal set; }

        /// <summary>The name is the target of INSERT [INTO]: choosing a table can insert its column list with it.</summary>
        public bool InsertTarget { get; internal set; }

        /// <summary>With <see cref="ContextKind.ObjectName"/>: the name of a database of the server (ALTER DATABASE, DROP DATABASE, USE, ...), nothing else (#447).</summary>
        public bool DatabaseName { get; internal set; }

        /// <summary>With <see cref="DatabaseName"/>: <c>CURRENT</c> is accepted in place of the name (ALTER DATABASE CURRENT SET ...).</summary>
        public bool CurrentDatabaseAllowed { get; internal set; }

        /// <summary>The name follows EXEC: stored procedures are offered and choosing one inserts its parameters.</summary>
        public bool ProcedureName { get; internal set; }

        /// <summary>With <see cref="ContextKind.InsertColumns"/>: the caret is right after the opening parenthesis (not after a comma).</summary>
        public bool FirstInsertColumn { get; internal set; }

        /// <summary>With <see cref="ContextKind.InsertColumns"/>: the column list of an index (key columns or INCLUDE) of CREATE INDEX, where every column of the table fits.</summary>
        public bool IndexColumns { get; internal set; }

        /// <summary>With <see cref="ContextKind.InsertColumns"/> in <c>REFERENCES t (</c>: the key columns of the referenced table, the primary key first (#213).</summary>
        public bool KeyColumnsOfReference { get; internal set; }

        /// <summary>With <see cref="ContextKind.Name"/> after CONSTRAINT: the table (and column) the constraint name is made from, e.g. PK_t, DF_t_col (#213).</summary>
        public string ConstraintTable { get; internal set; }

        public string ConstraintColumn { get; internal set; }

        /// <summary>With <see cref="ContextKind.Name"/> after DROP / CHECK / NOCHECK CONSTRAINT: the name parts of the table whose constraints are offered (#213).</summary>
        public IReadOnlyList<string> ExistingConstraintsOf { get; internal set; }

        /// <summary>With <see cref="ContextKind.ObjectName"/> after ALTER / CREATE OR ALTER / DROP PROCEDURE, FUNCTION, VIEW or TRIGGER: only modules of this kind are offered.</summary>
        public ModuleKind Module { get; internal set; }

        /// <summary>With <see cref="Module"/>: what the statement is.</summary>
        public ModuleAction ModuleAction { get; internal set; }

        /// <summary>With <see cref="Module"/>: the offset where the statement (ALTER, CREATE OR ALTER, DROP) starts.</summary>
        public int ModuleHeaderStart { get; internal set; }

        /// <summary>With <see cref="ContextKind.InsertColumns"/> for an index: the columns that are listed already and are not offered again.</summary>
        public IReadOnlyList<string> ExcludedColumns { get; internal set; } = Array.Empty<string>();

        /// <summary>In a condition right after a column (or after a comparison operator that follows one): the name of the column as parts (qualifier first).</summary>
        public IReadOnlyList<string> ConditionOperand { get; internal set; }

        /// <summary>With <see cref="ConditionOperand"/>: the caret is after a comparison operator, where a value of the type of the column goes.</summary>
        public bool AfterComparison { get; internal set; }

        /// <summary>At the start of a condition of HAVING (after HAVING, AND, OR, NOT or an opening parenthesis): the aggregates of the select list are offered first.</summary>
        public bool InHaving { get; internal set; }

        /// <summary>With <see cref="ContextKind.DataType"/>: the name (variable without @, or the operand of CAST) whose same-named columns give the types to offer first.</summary>
        public string TypeOfName { get; internal set; }

        /// <summary>With <see cref="ContextKind.DataType"/> in a column definition: the usual sized types (nvarchar(100), decimal(18,2) ...) are offered too.</summary>
        public bool DataTypeSizes { get; internal set; }

        /// <summary>After <c>INSERT INTO table (columns) |</c>: the table (VALUES and SELECT are offered pre-filled for the listed columns).</summary>
        public TableSource InsertTail { get; internal set; }

        /// <summary>With <see cref="InsertTail"/>: the columns listed in the parentheses.</summary>
        public IReadOnlyList<string> InsertTailColumns { get; internal set; }

        /// <summary>In a MERGE: where the caret is; the target and the source are the first two table sources.</summary>
        public MergeStage Merge { get; internal set; }

        /// <summary>The name follows JOIN, so tables related by a foreign key to the tables of the query can be offered first.</summary>
        public bool AfterJoin { get; internal set; }

        /// <summary>The name is the first table of a subquery (after FROM) that has outer queries: the tables related to the outer ones are offered first.</summary>
        public bool CorrelatedFrom { get; internal set; }

        /// <summary>After the table (and alias) that starts a subquery: the table, so that the correlation with the outer query can be offered as a WHERE condition.</summary>
        public TableSource Correlation { get; internal set; }

        /// <summary>The conditions are made between the table of a subquery and the tables of the outer queries too.</summary>
        public bool Correlated { get; internal set; }

        /// <summary>
        /// At the start of a condition after ON (or AND, OR following it): the table the JOIN introduces. Conditions that link it to the
        /// tables before it in the query are offered.
        /// </summary>
        public TableSource JoinTarget { get; internal set; }

        /// <summary>The qualifier started with a database name that was removed from it (three-part name); the metadata is that database's.</summary>
        public bool InOtherDatabase { get; internal set; }

        /// <summary>A dotted prefix (a database, a linked server and its database) was taken off the qualifier: a name part is expected, so no snippets and keywords (#357).</summary>
        public bool AfterDot { get; internal set; }

        /// <summary>
        /// A SELECT list without any FROM: columns of all tables are offered and choosing one adds the FROM.
        /// </summary>
        public bool SelectWithoutFrom { get; internal set; }

        /// <summary>With <see cref="SelectWithoutFrom"/>, the offset in the analyzed text where " FROM …" is inserted: the end of the select list.</summary>
        public int FromInsertPosition { get; internal set; } = -1;

        /// <summary>
        /// Names the query already refers to (aliases, or table names without alias), not counting the word being typed:
        /// in "JOIN o" the "o" is parsed as a table source of its own and must not make the alias "o" look taken.
        /// </summary>
        public IEnumerable<string> ReferenceNamesInUse() => Sources
            .Where(s => s.ReferenceName != null && !IsTypedFragment(s))
            .Select(s => s.ReferenceName);

        private bool IsTypedFragment(TableSource source) =>
            TypedPrefix.Length > 0 && source.Alias == null && source.Kind == TableSourceKind.Object && source.NameParts.Count == Qualifier.Count + 1 &&
            string.Equals(source.ObjectName, TypedPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>For "alias.|", the source the qualifier refers to.</summary>
        public TableSource QualifiedSource { get; internal set; }

        /// <summary>
        /// In an expression after "name." where the name is no table source or alias: a schema whose scalar functions are offered
        /// (the kind is <see cref="ContextKind.Name"/>, so everywhere else nothing is).
        /// </summary>
        public bool UnknownExpressionQualifier { get; internal set; }

        /// <summary>Keywords that take precedence at this position.</summary>
        public IReadOnlyList<string> PreferredKeywords { get; internal set; } = Array.Empty<string>();

        /// <summary>Keywords that can follow here as well, offered after <see cref="PreferredKeywords"/> and before the other keywords.</summary>
        public IReadOnlyList<string> NextKeywords { get; internal set; } = Array.Empty<string>();

        /// <summary>Only the preferred and next keywords can follow here (table hints, a CTE): nothing else is offered.</summary>
        public bool OnlyExpectedKeywords { get; internal set; }

        /// <summary>The keyword position was read after a complete operand (e.g. a select item), so its keywords rank above the columns.</summary>
        public bool KeywordsAfterOperand { get; internal set; }

        internal void ApplyPosition(KeywordPosition position, bool afterOperand)
        {
            if (position == null || position.IsEmpty)
            {
                return;
            }

            PreferredKeywords = position.First;
            NextKeywords = position.Then;
            OnlyExpectedKeywords = position.Exclusive;
            KeywordsAfterOperand = afterOperand;
            if (position.First.Count == 1 && position.Then.Count == 0 &&
                (TypedPrefix.Length == 0 || position.First[0].StartsWith(TypedPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                // Exactly one keyword can follow: it is preselected.
                SoftSelection = false;
            }
        }

        /// <summary>
        /// The user is probably typing a name of their own (an alias), so the selection must not be firm
        /// and Enter must not insert the first suggestion.
        /// </summary>
        public bool SoftSelection { get; internal set; }

        /// <summary>The text (the batch) and the caret this context was made from; null / 0 for contexts made by hand.</summary>
        public string Text { get; internal set; }

        public int Caret { get; internal set; }

        /// <summary>Copy of the context with another qualifier (e.g. without the database part) and without preferred keywords.</summary>
        internal CaretContext WithQualifier(IReadOnlyList<string> qualifier, bool inOtherDatabase = true) => new CaretContext(Kind, ReplaceStart, ReplaceLength, TypedPrefix)
        {
            QuoteIdentifiers = QuoteIdentifiers,
            Qualifier = qualifier,
            Sources = Sources,
            QualifiedSource = QualifiedSource,
            SoftSelection = SoftSelection,
            AliasAllowed = AliasAllowed,
            AfterJoin = AfterJoin,
            InsertTarget = InsertTarget,
            DatabaseName = DatabaseName,
            CurrentDatabaseAllowed = CurrentDatabaseAllowed,
            ProcedureName = ProcedureName,
            FirstInsertColumn = FirstInsertColumn,
            IndexColumns = IndexColumns,
            KeyColumnsOfReference = KeyColumnsOfReference,
            ConstraintTable = ConstraintTable,
            ConstraintColumn = ConstraintColumn,
            ExistingConstraintsOf = ExistingConstraintsOf,
            Module = Module,
            ModuleAction = ModuleAction,
            ModuleHeaderStart = ModuleHeaderStart,
            ExcludedColumns = ExcludedColumns,
            InOtherDatabase = inOtherDatabase,
            AfterDot = true,
        };

        public override string ToString() => Kind + " [" + string.Join(".", Qualifier) + "] '" + TypedPrefix + "'";
    }
}
