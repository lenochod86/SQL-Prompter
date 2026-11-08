using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Editing;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Settings;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The repairs of TOP without ORDER BY, NOT IN (subquery) and the old-style join (#208).</summary>
    public class CodeFixTests
    {
        private static DatabaseSnapshot Db() =>
            new DatabaseSnapshot("t", "dbo", new[] { "dbo" },
                new[] { new DbObject(1, "dbo", "Orders", DbObjectKind.Table, false), new DbObject(2, "dbo", "Users", DbObjectKind.Table, false), new DbObject(3, "dbo", "Pairs", DbObjectKind.Table, false), new DbObject(4, "dbo", "Heap", DbObjectKind.Table, false) },
                "v1", null, null, null, null,
                new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "IdOrder" }, [2] = new[] { "IdUser" }, [3] = new[] { "IdA", "IdB" } });

        private static string Fixed(string sql, IDatabaseMetadata metadata = null)
        {
            var settings = new AnalysisSettings();
            var fixes = CodeAnalyzer.Analyze(sql, settings, metadata).Where(i => i.Fix != null).Select(i => i.Fix).OrderByDescending(f => f.Start).ToList();
            var result = sql;
            foreach (var fix in fixes)
            {
                result = result.Substring(0, fix.Start) + fix.Replacement + result.Substring(fix.Start + fix.Length);
            }

            return result;
        }

        [Fact]
        public void TopGetsTheOrderByOfThePrimaryKey()
        {
            Assert.Equal("SELECT TOP 5 * FROM dbo.Orders ORDER BY [IdOrder]", Fixed("SELECT TOP 5 * FROM dbo.Orders", Db()));
            Assert.Equal("SELECT TOP (5) o.IdOrder FROM dbo.Orders AS o WHERE o.IdOrder > 3 ORDER BY o.[IdOrder];", Fixed("SELECT TOP (5) o.IdOrder FROM dbo.Orders AS o WHERE o.IdOrder > 3;", Db()).Replace("ORDER BY [o].", "ORDER BY o."));
            Assert.Equal("SELECT TOP 1 * FROM Pairs ORDER BY [IdA], [IdB]", Fixed("SELECT TOP 1 * FROM Pairs", Db()));
        }

        [Fact]
        public void TopHasNoRepairWithoutAKnownKey()
        {
            Assert.Equal("SELECT TOP 5 * FROM dbo.Orders", Fixed("SELECT TOP 5 * FROM dbo.Orders"));
            Assert.Equal("SELECT TOP 5 * FROM dbo.Heap", Fixed("SELECT TOP 5 * FROM dbo.Heap", Db()));
            Assert.Equal("SELECT TOP 5 * FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdOrder", Fixed("SELECT TOP 5 * FROM dbo.Orders o JOIN dbo.Users u ON u.IdUser = o.IdOrder", Db()));
            Assert.Equal("SELECT DISTINCT TOP 5 Name FROM dbo.Orders", Fixed("SELECT DISTINCT TOP 5 Name FROM dbo.Orders", Db()));
        }

        [Fact]
        public void NotInBecomesNotExists()
        {
            Assert.Equal(
                "SELECT * FROM dbo.Users u WHERE NOT EXISTS (SELECT 1 FROM dbo.Orders o WHERE [o].[IdUser] = u.IdUser)",
                Fixed("SELECT * FROM dbo.Users u WHERE u.IdUser NOT IN (SELECT IdUser FROM dbo.Orders o)"));
            Assert.Equal(
                "SELECT * FROM dbo.Users u WHERE NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE (Total > 5 OR Total < 0) AND [Orders].[IdUser] = u.IdUser) AND 1 = 1",
                Fixed("SELECT * FROM dbo.Users u WHERE u.IdUser NOT IN (SELECT IdUser FROM dbo.Orders WHERE Total > 5 OR Total < 0) AND 1 = 1"));
            Assert.Equal(
                "SELECT * FROM dbo.Users u WHERE NOT EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.Open = 1 AND o.IdUser = u.IdUser)",
                Fixed("SELECT * FROM dbo.Users u WHERE u.IdUser NOT IN (SELECT o.IdUser FROM dbo.Orders o WHERE o.Open = 1)"));
        }

        [Fact]
        public void NotInIsLeftAloneWhenItIsNotAPlainShape()
        {
            const string grouped = "SELECT * FROM dbo.Users u WHERE u.IdUser NOT IN (SELECT IdUser FROM dbo.Orders GROUP BY IdUser)";
            const string joined = "SELECT * FROM dbo.Users u WHERE u.IdUser NOT IN (SELECT o.IdUser FROM dbo.Orders o JOIN dbo.Users x ON x.IdUser = o.IdUser)";
            const string expression = "SELECT * FROM dbo.Users u WHERE u.IdUser + 1 NOT IN (SELECT IdUser FROM dbo.Orders)";
            const string list = "SELECT * FROM dbo.Users u WHERE u.IdUser NOT IN (SELECT IdUser, Total FROM dbo.Orders)";
            // The outer column has no qualifier: inside the subquery it would mean the column of the subquery.
            const string unqualified = "SELECT * FROM dbo.Users WHERE IdUser NOT IN (SELECT IdUser FROM dbo.Orders)";
            Assert.Equal(unqualified, Fixed(unqualified));
            Assert.Equal(grouped, Fixed(grouped));
            Assert.Equal(joined, Fixed(joined));
            Assert.Equal(expression, Fixed(expression));
            Assert.Equal(list, Fixed(list));
        }

        [Fact]
        public void AnOldStyleJoinBecomesAJoin()
        {
            Assert.Equal(
                "SELECT * FROM dbo.Orders o JOIN dbo.Users u ON o.IdUser = u.IdUser WHERE o.Total > 5",
                Fixed("SELECT * FROM dbo.Orders o, dbo.Users u WHERE o.IdUser = u.IdUser AND o.Total > 5"));
            Assert.Equal(
                "SELECT * FROM A a JOIN B b ON a.x = b.x JOIN C c ON b.y = c.y AND a.z = c.z",
                Fixed("SELECT * FROM A a, B b, C c WHERE a.x = b.x AND b.y = c.y AND a.z = c.z"));
            Assert.Equal(
                "SELECT * FROM A a CROSS JOIN B b WHERE a.x = 1",
                Fixed("SELECT * FROM A a, B b WHERE a.x = 1"));
            Assert.Equal(
                "SELECT * FROM A a JOIN B AS b ON a.x = b.x ORDER BY a.x",
                Fixed("SELECT * FROM A a, B AS b WHERE a.x = b.x ORDER BY a.x"));
        }

        [Fact]
        public void AnOldStyleJoinIsLeftAloneWhenItCannotBeRewrittenSafely()
        {
            const string or = "SELECT * FROM A a, B b WHERE a.x = b.x OR a.y = b.y";
            const string subquery = "SELECT * FROM A a, (SELECT 1 AS x) b WHERE a.x = b.x";
            const string mixed = "SELECT * FROM A a, B b JOIN C c ON c.z = b.z WHERE a.x = b.x";
            Assert.Equal(or, Fixed(or));
            Assert.Equal(subquery, Fixed(subquery));
            Assert.Equal(mixed, Fixed(mixed));
        }

        [Fact]
        public void TheFixCommandAppliesTheNewRepairs()
        {
            var text = "SELECT * FROM A a, B b WHERE a.x = b.x\r\nSELECT * FROM U WHERE U.id NOT IN (SELECT id FROM O)";

            var result = CodeFixes.Apply(text, new AnalysisSettings());

            Assert.Contains("FROM A a JOIN B b ON a.x = b.x", result);
            Assert.Contains("NOT EXISTS (SELECT 1 FROM O WHERE [O].[id] = U.id)", result);
        }
    }
}
