using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The position map for MERGE, PIVOT, OVER, IF/WHILE, EXEC, OFFSET … FETCH and FOR XML/JSON (#178).</summary>
    public class KeywordMapMoreTests
    {
        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), SampleDatabase());

        private static string[] Ranked(string sql) => Items(sql).Where(i => i.Kind == SuggestionKind.Keyword && !i.IsBlock).OrderBy(i => i.Group).Select(i => i.InsertText).ToArray();

        /// <summary>The keywords of the best keyword group.</summary>
        private static string[] First(string sql)
        {
            var keywords = Items(sql).Where(i => i.Kind == SuggestionKind.Keyword && !i.IsBlock).ToList();
            var group = keywords.Min(k => k.Group);
            return keywords.Where(k => k.Group == group).Select(k => k.InsertText).ToArray();
        }

        /// <summary>The keywords that rank above every other kind of item.</summary>
        private static string[] Above(string sql)
        {
            var items = Items(sql);
            var others = items.Where(i => i.Kind != SuggestionKind.Keyword).Select(i => i.Group).DefaultIfEmpty(int.MaxValue).Min();
            return items.Where(i => i.Kind == SuggestionKind.Keyword && !i.IsBlock && i.Group < others).Select(i => i.InsertText).ToArray();
        }

        private static void AssertFirst(string sql, params string[] expected)
        {
            var first = First(sql);
            Assert.True(first.Length < 40, "nothing is preferred at this position");
            foreach (var keyword in expected)
            {
                Assert.Contains(keyword, first);
            }
        }

        private static void AssertAbove(string sql, params string[] expected)
        {
            var above = Above(sql);
            foreach (var keyword in expected)
            {
                Assert.Contains(keyword, above);
            }
        }

        private const string Merge = "MERGE INTO dbo.Users AS t USING dbo.Orders AS s ON s.UserId = t.Id";

        [Fact]
        public void MergeTargetWantsUsingOrAs()
        {
            AssertFirst("MERGE INTO dbo.Users |", "AS", "USING");
            Assert.Equal(new[] { "USING" }, Ranked("MERGE INTO dbo.Users AS t |").Take(1).ToArray());
            Assert.DoesNotContain("AS", First("MERGE INTO dbo.Users AS t |"));
        }

        [Fact]
        public void MergeSourceWantsOn()
        {
            AssertFirst("MERGE INTO dbo.Users AS t USING dbo.Orders |", "AS", "ON");
            Assert.Equal(new[] { "ON" }, First("MERGE INTO dbo.Users AS t USING dbo.Orders s |"));
        }

        [Fact]
        public void AfterTheMergeConditionWhenMatched()
        {
            AssertAbove(Merge + " |", "AND", "OR", "WHEN MATCHED", "WHEN NOT MATCHED");
        }

        [Fact]
        public void WhenIsFollowedByMatchedOrNotMatched()
        {
            Assert.Equal(new[] { "MATCHED", "NOT MATCHED" }, First(Merge + " WHEN |").OrderBy(x => x).ToArray());
        }

        [Fact]
        public void AfterMatchedThenOrAnd()
        {
            AssertFirst(Merge + " WHEN MATCHED |", "THEN", "AND");
            AssertFirst(Merge + " WHEN NOT MATCHED |", "BY TARGET", "BY SOURCE", "THEN");
            AssertFirst(Merge + " WHEN NOT MATCHED BY |", "TARGET", "SOURCE");
            AssertFirst(Merge + " WHEN NOT MATCHED BY SOURCE |", "THEN");
        }

        [Fact]
        public void AfterThenTheAction()
        {
            AssertFirst(Merge + " WHEN MATCHED THEN |", "UPDATE SET", "DELETE", "INSERT");
            Assert.Equal(new[] { "SET" }, First(Merge + " WHEN MATCHED THEN UPDATE |"));
        }

        [Fact]
        public void AfterAnActionTheNextWhenOrOutput()
        {
            AssertAbove(Merge + " WHEN MATCHED THEN UPDATE SET Name = s.Name |", "WHEN", "OUTPUT");
            AssertAbove(Merge + " WHEN MATCHED THEN DELETE |", "WHEN", "OUTPUT");
            AssertAbove(Merge + " WHEN NOT MATCHED THEN INSERT (Name) VALUES (s.Name) |", "WHEN", "OUTPUT");
            Assert.Contains("VALUES", First(Merge + " WHEN NOT MATCHED THEN INSERT (Name) |"));
        }

        [Fact]
        public void PivotNeedsAnAlias()
        {
            Assert.Equal(new[] { "AS" }, First("SELECT * FROM dbo.Orders PIVOT (SUM(Total) FOR UserId IN ([1], [2])) |"));
            Assert.Contains("PIVOT", Ranked("SELECT * FROM dbo.Orders o |"));
            Assert.Contains("UNPIVOT", Ranked("SELECT * FROM dbo.Orders o |"));
        }

        [Fact]
        public void InsidePivotForAndIn()
        {
            Assert.Equal(new[] { "FOR" }, First("SELECT * FROM dbo.Orders PIVOT (SUM(Total) |"));
            Assert.Equal(new[] { "IN" }, First("SELECT * FROM dbo.Orders PIVOT (SUM(Total) FOR UserId |"));
            Assert.Equal(new[] { "FOR" }, First("SELECT * FROM dbo.Orders UNPIVOT (Total |"));
        }

        [Fact]
        public void OverOffersPartitionAndOrder()
        {
            AssertFirst("SELECT ROW_NUMBER() OVER (|", "PARTITION BY", "ORDER BY");
        }

        [Fact]
        public void InsideOverTheNextPart()
        {
            AssertAbove("SELECT ROW_NUMBER() OVER (PARTITION BY u.Id |", "ORDER BY");
            AssertAbove("SELECT ROW_NUMBER() OVER (PARTITION BY u.Id ORDER BY u.Name |", "ASC", "DESC", "ROWS", "RANGE");
            AssertAbove("SELECT ROW_NUMBER() OVER (ORDER BY u.Name DESC |", "ROWS", "RANGE");
        }

        [Fact]
        public void WindowFrame()
        {
            AssertFirst("SELECT SUM(x) OVER (ORDER BY u.Name ROWS |", "BETWEEN", "UNBOUNDED PRECEDING", "CURRENT ROW");
            AssertFirst("SELECT SUM(x) OVER (ORDER BY u.Name ROWS BETWEEN UNBOUNDED PRECEDING |", "AND");
            AssertFirst("SELECT SUM(x) OVER (ORDER BY u.Name ROWS BETWEEN UNBOUNDED PRECEDING AND |", "CURRENT ROW", "UNBOUNDED FOLLOWING");
        }

        [Fact]
        public void AFunctionCallInTheSelectListMayBeFollowedByOver()
        {
            Assert.Contains("OVER", Above("SELECT COUNT(*) |"));
            Assert.DoesNotContain("OVER", Above("SELECT (1 + 2) |"));
        }

        [Fact]
        public void IfOffersExists()
        {
            AssertFirst("IF |", "EXISTS", "NOT EXISTS");
        }

        [Fact]
        public void AfterAnIfConditionBeginAndTheBody()
        {
            AssertFirst("IF @x = 1 |", "BEGIN", "AND", "OR");
            Assert.Contains("SELECT", Ranked("IF @x = 1 |"));
            AssertFirst("IF EXISTS (SELECT 1 FROM dbo.Users) |", "BEGIN");
            AssertFirst("WHILE @i < 10 |", "BEGIN");
            Assert.Empty(Analyze("IF @x |").PreferredKeywords);
        }

        [Fact]
        public void AfterAnExecParameterOutput()
        {
            AssertAbove("EXEC dbo.Proc @a = @b |", "OUTPUT");
            Assert.Contains("WITH RECOMPILE", Ranked("EXEC dbo.Proc @a = @b |"));
            AssertFirst("EXEC dbo.Proc @a = @b OUTPUT |", "WITH RECOMPILE");
            AssertFirst("EXEC @r = dbo.Proc @a = 1 |", "OUTPUT");
        }

        [Fact]
        public void ExecWithOffersRecompile()
        {
            Assert.Equal(new[] { "RECOMPILE", "RESULT SETS" }, First("EXEC dbo.Proc @a = 1 WITH |").OrderBy(x => x).ToArray());
        }

        [Fact]
        public void OffsetIsFollowedByRowsThenFetch()
        {
            Assert.Equal(new[] { "ROW", "ROWS" }, First("SELECT * FROM dbo.Users u ORDER BY u.Id OFFSET 10 |").OrderBy(x => x).ToArray());
            AssertFirst("SELECT * FROM dbo.Users u ORDER BY u.Id OFFSET 10 ROWS |", "FETCH NEXT", "FETCH FIRST");
        }

        [Fact]
        public void FetchWantsNextRowsOnly()
        {
            Assert.Equal(new[] { "FIRST", "NEXT" }, First("SELECT * FROM dbo.Users u ORDER BY u.Id OFFSET 10 ROWS FETCH |").OrderBy(x => x).ToArray());
            Assert.Equal(new[] { "ROW", "ROWS" }, First("SELECT * FROM dbo.Users u ORDER BY u.Id OFFSET 10 ROWS FETCH NEXT 5 |").OrderBy(x => x).ToArray());
            Assert.Equal(new[] { "ONLY" }, First("SELECT * FROM dbo.Users u ORDER BY u.Id OFFSET 10 ROWS FETCH NEXT 5 ROWS |"));
            AssertFirst("SELECT * FROM dbo.Users u ORDER BY u.Id OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY |", "OPTION", "UNION");
        }

        [Fact]
        public void ForOffersXmlAndJson()
        {
            Assert.Equal(new[] { "BROWSE", "JSON", "XML" }, First("SELECT * FROM dbo.Users u FOR |").OrderBy(x => x).ToArray());
        }

        [Fact]
        public void ForXmlModes()
        {
            AssertFirst("SELECT * FROM dbo.Users u FOR XML |", "PATH", "AUTO", "RAW", "EXPLICIT");
            AssertFirst("SELECT * FROM dbo.Users u FOR JSON |", "PATH", "AUTO");
        }

        [Fact]
        public void ForXmlOptions()
        {
            AssertFirst("SELECT * FROM dbo.Users u FOR XML PATH('r') |", "ROOT", "TYPE", "ELEMENTS");
            AssertFirst("SELECT * FROM dbo.Users u FOR XML AUTO, |", "ROOT", "TYPE", "ELEMENTS");
            AssertFirst("SELECT * FROM dbo.Users u FOR JSON AUTO |", "ROOT", "INCLUDE_NULL_VALUES", "WITHOUT_ARRAY_WRAPPER");
        }
    }
}
