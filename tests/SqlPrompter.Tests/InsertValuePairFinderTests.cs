using System.Linq;
using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class InsertValuePairFinderTests
    {
        private static string Partners(string sql)
        {
            var caret = sql.IndexOf('|');
            var text = sql.Remove(caret, 1);
            return string.Join(" ~ ", InsertValuePairFinder.Find(text, caret).Select(s => text.Substring(s.Start, s.Length)));
        }

        [Theory]
        [InlineData("INSERT INTO dbo.T (a, |b, c) VALUES (1, 2, 3)", "2")]
        [InlineData("INSERT INTO dbo.T (a, b, c|) VALUES (1, 2, 3)", "3")]
        [InlineData("INSERT INTO dbo.T (a, b, c) VALUES (1, |2, 3)", "b")]
        [InlineData("insert t ([a], [b b]) values (1, |N'x')", "[b b]")]
        [InlineData("INSERT INTO @t (a, b) VALUES (|@x, @y);", "a")]
        [InlineData("INSERT INTO #t (a, b) VALUES (1, 2|)", "b")]
        [InlineData("INSERT TOP (5) INTO db..T WITH (TABLOCK) (a, |b) VALUES (1, 2)", "2")]
        [InlineData("INSERT INTO t (a, |b) OUTPUT inserted.a, inserted.b INTO @o (x, y) VALUES (1, 2)", "2")]
        public void PairsByPosition(string sql, string expected) => Assert.Equal(expected, Partners(sql));

        [Theory]
        [InlineData("INSERT INTO t (a, |b) VALUES (1, 2), (3, 4), (5, 6)", "2 ~ 4 ~ 6")]
        [InlineData("INSERT INTO t (a, b) VALUES (1, 2), (3, |4)", "b")]
        public void MultipleRows(string sql, string expected) => Assert.Equal(expected, Partners(sql));

        [Theory]
        [InlineData("INSERT INTO t (a, |b, c) VALUES (COALESCE(@x, 1, 2), CONVERT(varchar(10), GETDATE(), 120), 'x,y')", "CONVERT(varchar(10), GETDATE(), 120)")]
        [InlineData("INSERT INTO t (a, b, |c) VALUES (COALESCE(@x, 1, 2), '), (', (SELECT MAX(x), 1 FROM u))", "(SELECT MAX(x), 1 FROM u)")]
        [InlineData("INSERT INTO t (a, b) VALUES (CASE WHEN x = 1 THEN 2 ELSE 3 END, |-1)", "b")]
        [InlineData("INSERT INTO t (a, b) VALUES (1 /* , */, |2) -- , 3", "b")]
        public void CommasInsideAValueDoNotCount(string sql, string expected) => Assert.Equal(expected, Partners(sql));

        [Theory]
        [InlineData("INSERT INTO t (a, |b) SELECT x.a, x.b + 1 AS n FROM x", "x.b + 1 AS n")]
        [InlineData("INSERT INTO t (a, b) SELECT DISTINCT TOP (10) x.a, |ISNULL(x.b, 0) FROM x WHERE x.a IN (1, 2)", "b")]
        [InlineData("INSERT INTO t (a, |b) SELECT TOP 10 PERCENT 1, CASE WHEN 1 = 1 THEN 'a' ELSE 'b' END", "CASE WHEN 1 = 1 THEN 'a' ELSE 'b' END")]
        [InlineData("INSERT INTO t (a, |b) SELECT 1, 2 UNION ALL SELECT 3, 4", "2")]
        [InlineData("INSERT INTO t (a, |b) SELECT x.a, x.* FROM x", "")]
        [InlineData("INSERT INTO t (|a, b) SELECT x.a, x.* FROM x", "x.a")]
        [InlineData("INSERT INTO t (a, |b) SELECT 1, 2 * 3", "2 * 3")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2 FROM x ORDER BY |a, b", "")]
        public void InsertSelect(string sql, string expected) => Assert.Equal(expected, Partners(sql));

        [Fact]
        public void MultiLineLayout()
        {
            var sql = "INSERT INTO dbo.TableA\r\n(\r\n    Id,\r\n    OwnerId,\r\n    Cre|ated, -- when\r\n    Note\r\n)\r\nVALUES\r\n(176518, 148819, N'2026-11-27T10:00:00', NULL);";

            Assert.Equal("N'2026-11-27T10:00:00'", Partners(sql));
        }

        [Theory]
        [InlineData("INSERT INTO t (a, b, |c) VALUES (1, 2)", "")]
        [InlineData("INSERT INTO t (a) VALUES (1, |2)", "")]
        [InlineData("INSERT INTO t (a, |b) VALUES (1, )", "")]
        [InlineData("INSERT INTO t (a, b, |c) VALUES (1, , 3)", "3")]
        public void UnequalCounts(string sql, string expected) => Assert.Equal(expected, Partners(sql));

        [Theory]
        [InlineData("SELECT |a, b FROM t")]
        [InlineData("INSERT INTO t VALUES (|1, 2)")]
        [InlineData("INSERT INTO t (a, |b) EXEC dbo.p 1, 2")]
        [InlineData("INSERT INTO t (a, |b) DEFAULT VALUES")]
        [InlineData("INSERT INTO |t (a, b) VALUES (1, 2)")]
        [InlineData("INSERT INTO t (a, b) VALUES (1, 2);\r\nSELECT |1, 2")]
        [InlineData("MERGE t USING s ON 1 = 1 WHEN NOT MATCHED THEN INSERT (a, |b) VALUES (s.a, s.b);")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2\r\nUPDATE t SET a = 1, |b = 2")]
        public void NothingToPair(string sql) => Assert.Equal(string.Empty, Partners(sql));

        [Theory]
        [InlineData("INSERT INTO t (a, |b) VALUES (1, 2\r\nSELECT 3, 4", "2")]
        [InlineData("IF 1 = 1 INSERT INTO t (a, |b) SELECT 1, 2 ELSE PRINT 'x'", "2")]
        [InlineData("INSERT INTO t (a, |b) SELECT 1, 2\r\nGO\r\nSELECT 1", "2")]
        [InlineData("INSERT INTO t (a) VALUES (1)\r\nINSERT INTO u (c, |d) VALUES (3, 4)", "4")]
        public void TheListEndsWhereTheStatementDoes(string sql, string expected) => Assert.Equal(expected, Partners(sql));
    }
}
