using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Settings;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>INSERT INTO a temporary table or a table variable: the column list and the columns inside the parentheses (#449).</summary>
    public class InsertTempTableTests
    {
        private static readonly EditorLayout Editor = new EditorLayout { NewLine = "\n", LineWidth = 80 };

        private static IReadOnlyList<Suggestion> Build(string marked, InsertSettings inserts = null)
        {
            var caret = marked.IndexOf('|');
            var text = marked.Remove(caret, 1);
            var options = new SuggestionOptions(KeywordCase.MatchTyped, new AliasSettings(), null, null, null, inserts ?? new InsertSettings(), Editor);
            return SuggestionBuilder.Build(Analyze(marked), SampleDatabase(), ScriptModel.Build(text, caret), options);
        }

        private static string[] ColumnNames(string marked) =>
            Build(marked).Where(i => i.Kind == SuggestionKind.Column && !i.IsBlock).Select(i => i.DisplayText).ToArray();

        [Theory]
        [InlineData("CREATE TABLE #t (a int, b varchar(10));\nINSERT INTO #t (|", "a,b")]
        [InlineData("CREATE TABLE #t (a int, b varchar(10));\nINSERT INTO #t (a, |", "a,b")]
        [InlineData("CREATE TABLE #t (a int, b varchar(10));\nINSERT #t (|", "a,b")]
        [InlineData("CREATE TABLE ##g (a int, b varchar(10));\nINSERT INTO ##g (|", "a,b")]
        [InlineData("CREATE TABLE #t ([a b] int, c int);\nINSERT INTO #t (|", "a b,c")]
        [InlineData("DECLARE @t TABLE (a int identity, b int);\nINSERT INTO @t (|", "b")]
        [InlineData("CREATE TABLE #t (a int identity(1,1), b int, c AS a + b);\nINSERT INTO #t (|", "b")]
        [InlineData("SELECT 1 AS x, 2 AS y INTO #t;\nINSERT INTO #t (|", "x,y")]
        public void TheColumnsOfATemporaryTableAreOfferedInTheParentheses(string marked, string expected) =>
            Assert.Equal(expected.Split(new[] { ',' }), ColumnNames(marked).OrderBy(x => x, System.StringComparer.Ordinal).ToArray());

        [Fact]
        public void ATemporaryTableThatIsNotKnownGivesAnEmptyList()
        {
            Assert.Empty(Build("INSERT INTO #unknown (|"));
            Assert.Empty(Build("INSERT INTO @unknown (|"));
        }

        [Fact]
        public void ATableCreatedAfterTheInsertIsNotKnown()
        {
            Assert.Empty(ColumnNames("INSERT INTO #t (|\nCREATE TABLE #t (a int);"));
        }

        [Fact]
        public void ThePickedTemporaryTableBringsItsColumnList()
        {
            var items = Build("CREATE TABLE #t (a int identity, b int, c varchar(5));\nINSERT INTO |");

            var item = items.Single(i => i.Kind == SuggestionKind.ScriptTable && i.DisplayText == "#t");
            Assert.True(item.IsBlock);
            Assert.StartsWith("#t", item.InsertText);
            Assert.Contains("b", item.InsertText);
            Assert.Contains("c", item.InsertText);
            Assert.DoesNotContain("a,", item.InsertText.Replace(" ", string.Empty));
        }

        [Fact]
        public void ATableVariableBringsItsColumnListToo()
        {
            var item = Build("DECLARE @t TABLE (a int, b int);\nINSERT INTO |").Single(i => i.Kind == SuggestionKind.ScriptTable && i.DisplayText == "@t");

            Assert.True(item.IsBlock);
            Assert.Contains("a", item.InsertText);
            Assert.Contains("b", item.InsertText);
        }

        [Fact]
        public void ATemporaryTableWithUnknownColumnsIsInsertedAsItIs()
        {
            var item = Build("SELECT * INTO #t FROM dbo.Unknown;\nINSERT INTO |").Single(i => i.Kind == SuggestionKind.ScriptTable && i.DisplayText == "#t");

            Assert.False(item.IsBlock);
            Assert.Equal("#t", item.InsertText);
        }

        [Fact]
        public void OutsideAnInsertTheTemporaryTableIsPlain()
        {
            var item = Build("CREATE TABLE #t (a int);\nSELECT * FROM |").Single(i => i.Kind == SuggestionKind.ScriptTable && i.DisplayText == "#t");

            Assert.False(item.IsBlock);
        }

        [Fact]
        public void TheFragmentsAfterTheColumnListWorkForATemporaryTable()
        {
            var items = Build("CREATE TABLE #t (a int, b varchar(10));\nINSERT INTO #t (a, b) |");

            Assert.Contains(items, i => i.DisplayText.StartsWith("VALUES"));
        }
    }
}
