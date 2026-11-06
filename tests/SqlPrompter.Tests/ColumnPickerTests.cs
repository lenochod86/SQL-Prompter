using System.Linq;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class ColumnPickerTests
    {
        private static ColumnClause At(string marked)
        {
            var caret = marked.IndexOf('|');
            return ColumnClauses.At(marked.Remove(caret, 1), caret);
        }

        private static string[] NonAggregated(string marked)
        {
            var caret = marked.IndexOf('|');
            return ColumnClauses.NonAggregatedItems(marked.Remove(caret, 1), caret).ToArray();
        }

        [Theory]
        [InlineData("SELECT |", ColumnClause.SelectList)]
        [InlineData("SELECT DISTINCT |", ColumnClause.SelectList)]
        [InlineData("SELECT TOP 10 |", ColumnClause.SelectList)]
        [InlineData("SELECT TOP (10) |", ColumnClause.SelectList)]
        [InlineData("SELECT TOP 5 WITH TIES |", ColumnClause.SelectList)]
        [InlineData("SELECT a, |", ColumnClause.SelectList)]
        [InlineData("SELECT a, b, id|", ColumnClause.SelectList)]
        [InlineData("SELECT * FROM t GROUP BY |", ColumnClause.GroupBy)]
        [InlineData("SELECT * FROM t GROUP BY a, |", ColumnClause.GroupBy)]
        [InlineData("SELECT * FROM t ORDER BY |", ColumnClause.OrderBy)]
        [InlineData("SELECT * FROM t ORDER BY a DESC, |", ColumnClause.OrderBy)]
        [InlineData("SELECT * FROM t WHERE a = 1 ORDER BY |", ColumnClause.OrderBy)]
        [InlineData("SELECT (SELECT |", ColumnClause.SelectList)]
        public void ColumnsArePickedAtTheStartOfAnItem(string sql, ColumnClause expected) => Assert.Equal(expected, At(sql));

        [Theory]
        [InlineData("SELECT * FROM t WHERE |")]
        [InlineData("SELECT * FROM t WHERE a = 1, |")]
        [InlineData("SELECT * FROM |")]
        [InlineData("SELECT a + |")]
        [InlineData("SELECT COUNT(|")]
        [InlineData("SELECT a |")]
        [InlineData("UPDATE t SET a = 1, |")]
        [InlineData("SELECT * FROM t JOIN u ON a = b, |")]
        [InlineData("SELECT * FROM t UNION ALL |")]
        [InlineData("|")]
        public void ElsewhereThereIsNoColumnClause(string sql) => Assert.Equal(ColumnClause.None, At(sql));

        [Fact]
        public void TheExampleOfTheIssueRepeatsTheNonAggregatedItem() =>
            Assert.Equal(new[] { "sk.IdZakaznik" }, NonAggregated("SELECT MAX(sk.PosledniAktivita), sk.IdZakaznik FROM Zfp.SanonKlienta AS sk GROUP BY |"));

        [Theory]
        [InlineData("SELECT a, b FROM t GROUP BY |", "a|b")]
        [InlineData("SELECT t.a AS x, t.b y, z = t.c FROM t GROUP BY |", "t.a|t.b|t.c")]
        [InlineData("SELECT COUNT(*), SUM(a), a FROM t GROUP BY |", "a")]
        [InlineData("SELECT a + b AS total, 1 AS one, 'x' AS txt, NULL AS n, @v AS v FROM t GROUP BY |", "a + b")]
        [InlineData("SELECT YEAR(d), a FROM t GROUP BY |", "YEAR(d)|a")]
        [InlineData("SELECT DISTINCT TOP 5 a, b FROM t GROUP BY |", "a|b")]
        [InlineData("SELECT CASE WHEN a = 1 THEN 'x' ELSE 'y' END AS k, a FROM t GROUP BY |", "CASE WHEN a = 1 THEN 'x' ELSE 'y' END|a")]
        [InlineData("SELECT a, SUM(b) OVER (PARTITION BY c) AS s FROM t GROUP BY |", "a")]
        [InlineData("SELECT a, (SELECT MAX(x) FROM u) AS m FROM t GROUP BY |", "a")]
        [InlineData("SELECT a, a FROM t GROUP BY |", "a")]
        [InlineData("SELECT * FROM t GROUP BY |", "")]
        [InlineData("SELECT a, b FROM t GROUP BY a, |", "a|b")]
        public void OnlyNonAggregatedItemsAreRepeated(string sql, string expected) =>
            Assert.Equal(expected, string.Join("|", NonAggregated(sql)));

        [Fact]
        public void ASubqueryUsesItsOwnSelectList() =>
            Assert.Equal("u.x", string.Join("|", NonAggregated("SELECT a FROM t WHERE a IN (SELECT u.x, COUNT(*) FROM u GROUP BY |")));

        [Fact]
        public void OnlyAGroupByHasNonAggregatedItems() => Assert.Empty(NonAggregated("SELECT a, b FROM t ORDER BY |"));

        private static string Group(string marked, SuggestionOptions options = null)
        {
            var caret = marked.IndexOf('|');
            var sql = marked.Remove(caret, 1);
            var context = SqlPrompter.Core.Analysis.ContextAnalyzer.Analyze(sql, caret);
            var items = SuggestionBuilder.Build(context, SampleDatabase(), null, options ?? SuggestionOptions.Default);
            return string.Join("|", items.Where(i => i.DisplayText == "All non-aggregated columns").Select(i => i.InsertText));
        }

        [Fact]
        public void GroupByOffersAllNonAggregatedColumnsAsAnItem() =>
            Assert.Equal("u.Id\r\n  , u.Name".Replace("\r\n  , ", ",\r\n"), Group("SELECT u.Id, u.Name, COUNT(*) FROM dbo.Users u GROUP BY |").Replace("\r\n", "\r\n"));

        [Fact]
        public void TheItemIsNotOfferedWhereItWouldRepeatNothingOrWhileTyping()
        {
            Assert.Equal(string.Empty, Group("SELECT COUNT(*) FROM dbo.Users u GROUP BY |"));
            Assert.Equal(string.Empty, Group("SELECT u.Id FROM dbo.Users u GROUP BY u.I|"));
            Assert.Equal(string.Empty, Group("SELECT u.Id FROM dbo.Users u ORDER BY |"));
        }

        private static ColumnPicker Picker(string marked, SuggestionOptions options = null)
        {
            var caret = marked.IndexOf('|');
            var sql = marked.Remove(caret, 1);
            var context = SqlPrompter.Core.Analysis.ContextAnalyzer.Analyze(sql, caret);
            return ColumnPicker.TryBuild(sql, caret, context, SampleDatabase(), null, options ?? SuggestionOptions.Default);
        }

        [Fact]
        public void TheSelectListOfSeveralSourcesIsGroupedBySourceAndQualified()
        {
            var picker = Picker("SELECT | FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id");

            Assert.Equal(ColumnClause.SelectList, picker.Clause);
            Assert.Equal(new[] { "u", "u", "u", "o", "o", "o" }, picker.Columns.Select(c => c.Group).ToArray());
            Assert.Equal("u.Id", picker.Columns[0].InsertText);
            Assert.Equal("o.Total", picker.Columns[5].InsertText);
        }

        [Fact]
        public void OneSourceIsNotQualifiedUnlessTheOptionSaysSo()
        {
            Assert.Equal("Name", Picker("SELECT | FROM dbo.Users u").Columns[1].InsertText);

            var qualified = new SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.MatchTyped, new SqlPrompter.Core.Settings.AliasSettings { QualifyColumnsWithAliases = true });
            Assert.Equal("u.Name", Picker("SELECT | FROM dbo.Users u", qualified).Columns[1].InsertText);
        }

        [Fact]
        public void TheOrderIsTheTableOrderOrAlphabeticalWithinASource()
        {
            var picker = Picker("SELECT | FROM dbo.Users u JOIN dbo.Orders o ON 1 = 1");

            Assert.Equal(new[] { "Id", "Name", "Email", "Id", "UserId", "Total" }, picker.Ordered(true).Select(c => c.Name).ToArray());
            Assert.Equal(new[] { "Email", "Id", "Name", "Id", "Total", "UserId" }, picker.Ordered(false).Select(c => c.Name).ToArray());
        }

        [Fact]
        public void TheInsertedTextFollowsTheChosenColumnsInTheDisplayOrder()
        {
            var picker = Picker("SELECT | FROM dbo.Users u JOIN dbo.Orders o ON 1 = 1");
            var chosen = new[] { picker.Columns[5], picker.Columns[0], picker.Columns[2] };

            Assert.Equal("u.Id,\r\nu.Email,\r\no.Total".Replace("\r\n", "\n"), picker.InsertText(chosen, true, SuggestionOptions.Default).Replace("\r\n", "\n").Replace("\n", "\n"));
            Assert.Equal("u.Email,\r\nu.Id,\r\no.Total".Replace("\r\n", "\n"), picker.InsertText(chosen, false, SuggestionOptions.Default).Replace("\r\n", "\n"));
        }

        [Fact]
        public void InsertColumnsOffersTheInsertableColumnsThatAreNotWrittenYet()
        {
            var picker = Picker("INSERT INTO dbo.Users (Id, |) VALUES (1)");

            Assert.Equal(ColumnClause.InsertColumns, picker.Clause);
            Assert.Equal(new[] { "Name", "Email" }, picker.Columns.Select(c => c.Name).ToArray());
        }

        [Fact]
        public void GroupByAndOrderByHaveAPickerToo()
        {
            Assert.Equal(ColumnClause.GroupBy, Picker("SELECT 1 FROM dbo.Users u GROUP BY |").Clause);
            Assert.Equal(ColumnClause.OrderBy, Picker("SELECT 1 FROM dbo.Users u ORDER BY |").Clause);
        }

        [Fact]
        public void NoPickerWhereNoColumnIsPicked()
        {
            Assert.Null(Picker("SELECT 1 FROM dbo.Users u WHERE |"));
            Assert.Null(Picker("SELECT | FROM dbo.Unknown u"));
            Assert.Null(Picker("SELECT u.| FROM dbo.Users u"));
        }
    }
}
