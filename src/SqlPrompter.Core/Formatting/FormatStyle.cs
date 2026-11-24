using System;
using System.Runtime.Serialization;

namespace SqlPrompter.Core.Formatting
{
    public enum CaseStyle
    {
        /// <summary>The text is left as it was written.</summary>
        Keep,
        Upper,
        Lower,
    }

    /// <summary>
    /// How the formatter lays out a script. The defaults follow the style the project is tested against (upper-case keywords, tabs, commas
    /// before the items, statements shorter than 75 characters on one line); <see cref="SqlPromptStyleImporter"/> fills it from a SQL Prompt style.
    /// </summary>
    [DataContract]
    public sealed class FormatStyle
    {
        public FormatStyle()
        {
            SetDefaults();
        }

        public CaseStyle KeywordCase { get; set; }

        public CaseStyle FunctionCase { get; set; }

        public CaseStyle DataTypeCase { get; set; }

        [DataMember(Name = "keywordCase")]
        private string KeywordCaseText
        {
            get => KeywordCase.ToString();
            set => KeywordCase = Parse(value, CaseStyle.Upper);
        }

        [DataMember(Name = "functionCase")]
        private string FunctionCaseText
        {
            get => FunctionCase.ToString();
            set => FunctionCase = Parse(value, CaseStyle.Upper);
        }

        [DataMember(Name = "dataTypeCase")]
        private string DataTypeCaseText
        {
            get => DataTypeCase.ToString();
            set => DataTypeCase = Parse(value, CaseStyle.Upper);
        }

        /// <summary>Leading white space is made of tabs where a whole tab fits (the rest is spaces).</summary>
        [DataMember(Name = "useTabs")]
        public bool UseTabs { get; set; }

        /// <summary>The width of one indent level and of a tab stop.</summary>
        [DataMember(Name = "tabWidth")]
        public int TabWidth { get; set; }

        /// <summary><c>, item</c> at the start of a line instead of <c>item,</c> at the end of the previous one.</summary>
        [DataMember(Name = "commaBeforeItems")]
        public bool CommaBeforeItems { get; set; }

        /// <summary>A SELECT, INSERT, UPDATE or DELETE whose text on one line is shorter than this stays on one line; 0 turns it off.</summary>
        [DataMember(Name = "collapseStatementsShorterThan")]
        public int CollapseStatementsShorterThan { get; set; }

        /// <summary>A subquery in parentheses shorter than this stays on the line of its item; 0 turns it off.</summary>
        [DataMember(Name = "collapseSubqueriesShorterThan")]
        public int CollapseSubqueriesShorterThan { get; set; }

        /// <summary>A CREATE TABLE shorter than this stays on one line; 0 turns it off.</summary>
        [DataMember(Name = "collapseDdlShorterThan")]
        public int CollapseDdlShorterThan { get; set; }

        /// <summary><c>SELECT TOP 10</c> / <c>SELECT DISTINCT</c> are followed by a line break before the first item.</summary>
        [DataMember(Name = "newLineAfterDistinctAndTop")]
        public bool NewLineAfterDistinctAndTop { get; set; }

        /// <summary><c>AND</c> and <c>OR</c> start the lines of a condition (otherwise they end them).</summary>
        [DataMember(Name = "andOrBeforeConditions")]
        public bool AndOrBeforeConditions { get; set; }

        /// <summary>The conditions of WHERE and HAVING are put on separate lines when there is more than one.</summary>
        [DataMember(Name = "conditionsOnSeparateLines")]
        public bool ConditionsOnSeparateLines { get; set; }

        /// <summary>Longer lines of a column list in INSERT and CREATE TABLE are broken.</summary>
        [DataMember(Name = "maxLineLength")]
        public int MaxLineLength { get; set; }

        /// <summary>One blank line between statements stays when the script had one (otherwise all blank lines are removed).</summary>
        [DataMember(Name = "keepBlankLines")]
        public bool KeepBlankLines { get; set; }

        /// <summary>A reserved word typed in the editor gets the keyword case as soon as a space, parenthesis, comma or semicolon follows it.</summary>
        [DataMember(Name = "caseKeywordsWhileTyping")]
        public bool CaseKeywordsWhileTyping { get; set; }

        /// <summary>The items of GROUP BY and ORDER BY each get a line (when there are several); off keeps them on the line of the keyword (#305).</summary>
        [DataMember(Name = "groupOrderBySeparateLines")]
        public bool GroupOrderBySeparateLines { get; set; }

        /// <summary>The <c>AS alias</c> of the items of a SELECT list start in one column (#305); only items on one line with an explicit AS are aligned.</summary>
        [DataMember(Name = "alignAliases")]
        public bool AlignAliases { get; set; }

        /// <summary><c>ON</c> of a JOIN starts a line of its own, indented under the JOIN, instead of following the joined table (#305).</summary>
        [DataMember(Name = "onOnNewLine")]
        public bool OnOnNewLine { get; set; }

        /// <summary>The comparison operators of conditions on separate lines (WHERE, HAVING, ON) start in one column (#305).</summary>
        [DataMember(Name = "alignComparisons")]
        public bool AlignComparisons { get; set; }

        /// <summary>The items of every clause of a statement start in one column (that of the longest keyword), not each after its own keyword (#305).</summary>
        [DataMember(Name = "alignAcrossClauses")]
        public bool AlignAcrossClauses { get; set; }

        /// <summary>JOIN lines are indented to the column of the table after FROM (#305).</summary>
        [DataMember(Name = "indentJoinTable")]
        public bool IndentJoinTable { get; set; }

        /// <summary><c>SET @x = value</c> that is not collapsed puts the equals sign and the value on the next line (#305).</summary>
        [DataMember(Name = "equalsOnNewLine")]
        public bool EqualsOnNewLine { get; set; }

        /// <summary>BEGIN and END are indented one level under the IF, WHILE or ELSE they belong to, and the contents one more level (#305).</summary>
        [DataMember(Name = "indentBeginEnd")]
        public bool IndentBeginEnd { get; set; }

        /// <summary>The opening parenthesis of a subquery that is not collapsed is on a line of its own, at the column of the item (otherwise it ends the line before) (#305).</summary>
        [DataMember(Name = "parenOnNewLine")]
        public bool ParenOnNewLine { get; set; }

        /// <summary>The contents of such a subquery are indented one level under the parentheses (#305).</summary>
        [DataMember(Name = "indentParenContents")]
        public bool IndentParenContents { get; set; }

        /// <summary>The opening parenthesis of a CTE is on a line of its own (otherwise it ends the line of AS) (#305).</summary>
        [DataMember(Name = "cteParenOnNewLine")]
        public bool CteParenOnNewLine { get; set; }

        /// <summary>The contents of a CTE are indented under its parentheses (#305).</summary>
        [DataMember(Name = "cteIndentContents")]
        public bool CteIndentContents { get; set; }

        /// <summary>AS of a CTE is on a line of its own, under the name (#305).</summary>
        [DataMember(Name = "cteAsOnNewLine")]
        public bool CteAsOnNewLine { get; set; }

        /// <summary>The opening parenthesis of CREATE TABLE is on a line of its own (otherwise it ends the first line) (#305).</summary>
        [DataMember(Name = "ddlParenOnNewLine")]
        public bool DdlParenOnNewLine { get; set; }

        /// <summary>The column definitions of CREATE TABLE are indented under the parentheses (#305).</summary>
        [DataMember(Name = "ddlIndentContents")]
        public bool DdlIndentContents { get; set; }

        /// <summary>The data types of the columns of CREATE TABLE start in one column (#305).</summary>
        [DataMember(Name = "alignDdlColumns")]
        public bool AlignDdlColumns { get; set; }

        /// <summary>The parameters of CREATE / ALTER PROCEDURE are written one per line under the name (#305).</summary>
        [DataMember(Name = "procedureParametersOnNewLines")]
        public bool ProcedureParametersOnNewLines { get; set; }

        /// <summary>The column list of INSERT INTO is written one column per line when the line is longer than this; 1 is always, 0 never (#305).</summary>
        [DataMember(Name = "insertColumnsBreakLength")]
        public int InsertColumnsBreakLength { get; set; }

        /// <summary>The opening parenthesis of a broken INSERT column list is on a line of its own (otherwise it ends the first line) (#305).</summary>
        [DataMember(Name = "insertParenOnNewLine")]
        public bool InsertParenOnNewLine { get; set; }

        /// <summary>The rows of INSERT ... VALUES each get a line; off keeps them on the line of VALUES (#305).</summary>
        [DataMember(Name = "insertValuesOnNewLines")]
        public bool InsertValuesOnNewLines { get; set; }

        /// <summary>A CASE expression shorter than this stays on one line, a longer one is broken into lines for WHEN, ELSE and END; 0 breaks every CASE (#305).</summary>
        [DataMember(Name = "collapseCaseShorterThan")]
        public int CollapseCaseShorterThan { get; set; }

        /// <summary>The expression of a simple CASE (<c>CASE expr WHEN ...</c>) is on the line after CASE (#305).</summary>
        [DataMember(Name = "caseExpressionOnNewLine")]
        public bool CaseExpressionOnNewLine { get; set; }

        /// <summary>A space after the opening and before the closing parenthesis of the contents of IN: <c>IN ( 1, 2 )</c> (#305).</summary>
        [DataMember(Name = "spaceAroundInContents")]
        public bool SpaceAroundInContents { get; set; }

        /// <summary>The contents of BEGIN … END are indented.</summary>
        [DataMember(Name = "indentBlocks")]
        public bool IndentBlocks { get; set; }

        public FormatStyle Clone() => (FormatStyle)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            TabWidth = Math.Max(1, Math.Min(16, TabWidth));
        }

        private static CaseStyle Parse(string value, CaseStyle fallback) => Enum.TryParse(value, true, out CaseStyle parsed) ? parsed : fallback;

        private void SetDefaults()
        {
            KeywordCase = CaseStyle.Upper;
            FunctionCase = CaseStyle.Upper;
            DataTypeCase = CaseStyle.Upper;
            UseTabs = true;
            TabWidth = 4;
            CommaBeforeItems = true;
            CollapseStatementsShorterThan = 75;
            CollapseSubqueriesShorterThan = 75;
            CollapseDdlShorterThan = 75;
            NewLineAfterDistinctAndTop = false;
            AndOrBeforeConditions = true;
            ConditionsOnSeparateLines = true;
            GroupOrderBySeparateLines = true;
            OnOnNewLine = false;
            AlignAliases = false;
            AlignComparisons = false;
            AlignAcrossClauses = false;
            IndentJoinTable = false;
            EqualsOnNewLine = false;
            IndentBeginEnd = false;
            ParenOnNewLine = false;
            IndentParenContents = true;
            CteParenOnNewLine = true;
            CteIndentContents = true;
            CteAsOnNewLine = false;
            DdlParenOnNewLine = true;
            DdlIndentContents = true;
            AlignDdlColumns = true;
            ProcedureParametersOnNewLines = false;
            InsertColumnsBreakLength = 0;
            InsertParenOnNewLine = false;
            InsertValuesOnNewLines = true;
            CollapseCaseShorterThan = 35;
            CaseExpressionOnNewLine = false;
            SpaceAroundInContents = false;
            MaxLineLength = 120;
            KeepBlankLines = true;
            IndentBlocks = true;
            CaseKeywordsWhileTyping = false;
        }
    }
}
