using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The position map: what the list offers first at the positions of T-SQL (#178).</summary>
    public class KeywordMapTests
    {
        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), SampleDatabase());

        /// <summary>The keywords of the first keyword group of the list.</summary>
        private static string[] First(string sql)
        {
            var keywords = Items(sql).Where(i => i.Kind == SuggestionKind.Keyword && !i.IsBlock).ToList();
            var group = keywords.Min(k => k.Group);
            return keywords.Where(k => k.Group == group).Select(k => k.InsertText).ToArray();
        }

        /// <summary>The keywords in the order of their groups.</summary>
        private static string[] Order(string sql) => Items(sql)
            .Where(i => i.Kind == SuggestionKind.Keyword && !i.IsBlock).OrderBy(i => i.Group).Select(i => i.InsertText).ToArray();

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

        private static void AssertNotFirst(string sql, params string[] unexpected)
        {
            var first = First(sql);
            foreach (var keyword in unexpected)
            {
                Assert.DoesNotContain(keyword, first);
            }
        }

        [Fact]
        public void SelectOffersTopAndDistinctWithTheColumns()
        {
            var items = Items("SELECT | FROM dbo.Users u");

            var top = items.Single(i => i.InsertText == "TOP");
            Assert.True(top.Group <= items.First(i => i.Kind == SuggestionKind.Alias).Group);
            Assert.True(top.Group < items.Single(i => i.Kind == SuggestionKind.Function && i.InsertText == "COUNT()").Group);
            Assert.Contains(items, i => i.InsertText == "DISTINCT" && i.Group == top.Group);
        }

        [Fact]
        public void AfterASelectItemFromAsAndIntoComeBeforeEverything()
        {
            var above = Above("SELECT u.Name |");

            Assert.Contains("FROM", above);
            Assert.Contains("AS", above);
            Assert.Contains("INTO", above);
            Assert.DoesNotContain("WHERE", above);
        }

        [Fact]
        public void AfterASelectItemWithAnAliasNoAs()
        {
            var above = Above("SELECT u.Name n |");

            Assert.Contains("FROM", above);
            Assert.DoesNotContain("AS", above);
        }

        [Fact]
        public void FromIsNotOfferedWhenOneFollows()
        {
            var above = Above("SELECT u.Name | FROM dbo.Users u");

            Assert.DoesNotContain("FROM", above);
            Assert.Contains("AS", above);
        }

        [Fact]
        public void AfterTopNumberNothingIsPreferred()
        {
            Assert.Empty(Above("SELECT TOP 10 |"));
        }

        [Fact]
        public void AfterATableNameAsWithAndWhereAndJoins()
        {
            AssertFirst("SELECT * FROM dbo.Users |", "AS", "WITH", "WHERE", "JOIN", "LEFT JOIN", "CROSS APPLY");
            AssertNotFirst("SELECT * FROM dbo.Users |", "HAVING", "AND", "ON", "SET");
        }

        [Fact]
        public void AfterATableNameGroupByAndOrderByComeLater()
        {
            var order = Order("SELECT * FROM dbo.Users |");

            Assert.True(Array.IndexOf(order, "WHERE") < Array.IndexOf(order, "GROUP BY"));
            Assert.True(Array.IndexOf(order, "GROUP BY") < Array.IndexOf(order, "HAVING"));
            Assert.Contains("OPTION", order);
        }

        [Fact]
        public void AfterAnAliasWhereAndJoinsWithoutAs()
        {
            AssertFirst("SELECT * FROM dbo.Users u |", "WHERE", "JOIN", "INNER JOIN", "WITH");
            AssertNotFirst("SELECT * FROM dbo.Users u |", "AS", "ON", "HAVING");
        }

        [Fact]
        public void TableHintsAfterWith()
        {
            var items = Items("SELECT * FROM dbo.Users AS u WITH (|");

            Assert.All(items, i => Assert.Equal(SuggestionKind.Keyword, i.Kind));
            foreach (var hint in new[] { "NOLOCK", "READPAST", "UPDLOCK", "ROWLOCK", "INDEX" })
            {
                Assert.Contains(items, i => i.InsertText == hint);
            }

            Assert.DoesNotContain(items, i => i.InsertText == "SELECT");
        }

        [Fact]
        public void AfterTableHintsTheNextClause()
        {
            AssertFirst("SELECT * FROM dbo.Users u WITH (NOLOCK) |", "WHERE", "JOIN");
            AssertNotFirst("SELECT * FROM dbo.Users u WITH (NOLOCK) |", "WITH", "AS");
        }

        [Fact]
        public void JoinedTableWithAnAliasOffersOnAlone()
        {
            var context = Analyze("SELECT * FROM dbo.Users u JOIN dbo.Orders o |");
            var items = SuggestionBuilder.Build(context, SampleDatabase());

            Assert.Equal(new[] { "ON" }, items.Where(i => i.Group == items.Min(x => x.Group)).Select(i => i.InsertText).ToArray());
            Assert.False(context.SoftSelection);
        }

        [Fact]
        public void JoinedTableWithoutAnAliasOffersOnAndAs()
        {
            AssertFirst("SELECT * FROM dbo.Users u JOIN dbo.Orders |", "ON", "AS");
        }

        [Fact]
        public void CrossJoinHasNoOn()
        {
            AssertFirst("SELECT * FROM dbo.Users u CROSS JOIN dbo.Orders o |", "WHERE", "JOIN");
            AssertNotFirst("SELECT * FROM dbo.Users u CROSS JOIN dbo.Orders o |", "ON");
        }

        [Fact]
        public void AfterTheOnConditionAndOrAndTheNextClause()
        {
            const string sql = "SELECT * FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id |";

            Assert.Contains("AND", Above(sql));
            Assert.Contains("OR", Above(sql));
            Assert.Contains("WHERE", Order(sql));
            Assert.Contains("LEFT JOIN", Order(sql));
        }

        [Fact]
        public void AfterTheWhereConditionAndOrGroupByOrderBy()
        {
            const string sql = "SELECT * FROM dbo.Users u WHERE u.Id = 1 |";

            var above = Above(sql);
            foreach (var keyword in new[] { "AND", "OR", "GROUP BY", "ORDER BY" })
            {
                Assert.Contains(keyword, above);
            }

            Assert.DoesNotContain("HAVING", above);
            Assert.DoesNotContain("WHERE", above);
            Assert.DoesNotContain("JOIN", above);
        }

        [Fact]
        public void AfterABetweenStartOnlyAnd()
        {
            Assert.Equal(new[] { "AND" }, Above("SELECT * FROM dbo.Users u WHERE u.Id BETWEEN 1 |"));
            Assert.Contains("OR", Above("SELECT * FROM dbo.Users u WHERE u.Id BETWEEN 1 AND 5 |"));
        }

        [Fact]
        public void AfterTheGroupByListHavingAndOrderBy()
        {
            const string sql = "SELECT u.Name FROM dbo.Users u GROUP BY u.Name |";

            Assert.Contains("HAVING", Above(sql));
            Assert.Contains("ORDER BY", Above(sql));
            Assert.DoesNotContain("WHERE", Above(sql));
            Assert.DoesNotContain("AND", Above(sql));
        }

        [Fact]
        public void HavingOnlyAfterGroupBy()
        {
            Assert.DoesNotContain("HAVING", Above("SELECT * FROM dbo.Users u WHERE u.Id = 1 |"));
            Assert.DoesNotContain("HAVING", First("SELECT * FROM dbo.Users u |"));
        }

        [Fact]
        public void AfterTheHavingConditionAndOrderBy()
        {
            var above = Above("SELECT u.Name FROM dbo.Users u GROUP BY u.Name HAVING COUNT(*) > 1 |");

            Assert.Contains("AND", above);
            Assert.Contains("OR", above);
            Assert.Contains("ORDER BY", above);
        }

        [Fact]
        public void AfterAnOrderByItemDirectionAndOffset()
        {
            var above = Above("SELECT * FROM dbo.Users u ORDER BY u.Name |");
            Assert.Contains("ASC", above);
            Assert.Contains("DESC", above);
            Assert.Contains("OFFSET", above);

            var afterDirection = Above("SELECT * FROM dbo.Users u ORDER BY u.Name DESC |");
            Assert.Contains("OFFSET", afterDirection);
            Assert.DoesNotContain("ASC", afterDirection);
            Assert.DoesNotContain("WHERE", afterDirection);
        }

        [Fact]
        public void AfterUnionAllOrSelect()
        {
            AssertFirst("SELECT 1 UNION |", "ALL", "SELECT");
            AssertFirst("SELECT 1 UNION ALL |", "SELECT");
        }

        [Fact]
        public void CteNameWantsAs()
        {
            var context = Analyze("WITH c |");
            var items = SuggestionBuilder.Build(context, null);

            Assert.Equal(new[] { "AS" }, items.Select(i => i.InsertText).ToArray());
            Assert.False(context.SoftSelection);
        }

        [Fact]
        public void CteWithColumnListWantsAs()
        {
            Assert.Equal(new[] { "AS" }, SuggestionBuilder.Build(Analyze("WITH c (a, b) |"), null).Select(i => i.InsertText).ToArray());
        }

        [Fact]
        public void CteBodyStartsWithSelect()
        {
            Assert.Equal(new[] { "SELECT" }, SuggestionBuilder.Build(Analyze("WITH c AS (|"), null).Select(i => i.InsertText).ToArray());
        }

        [Fact]
        public void AfterTheCteTheMainStatement()
        {
            var items = SuggestionBuilder.Build(Analyze("WITH c AS (SELECT 1 AS x) |"), null).Select(i => i.InsertText).ToArray();

            Assert.Equal(new[] { "DELETE", "INSERT", "MERGE", "SELECT", "UPDATE" }, items.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void TheMainSelectAfterCtesContinuesNormally()
        {
            AssertFirst("WITH c AS (SELECT 1 AS x), d AS (SELECT 2 AS y) SELECT * FROM c |", "WHERE", "JOIN");
        }

        [Fact]
        public void InsertIntoTableWantsValuesOrSelect()
        {
            AssertFirst("INSERT INTO dbo.Users |", "VALUES", "SELECT", "EXEC");
            AssertFirst("INSERT INTO dbo.Users (Name) |", "VALUES", "SELECT", "EXEC");
            AssertNotFirst("INSERT INTO dbo.Users (Name) |", "WHERE", "FROM");
        }

        [Fact]
        public void InsertWantsInto()
        {
            Assert.Contains("INTO", Analyze("INSERT |").PreferredKeywords);
        }

        [Fact]
        public void UpdateTableWantsSet()
        {
            var context = Analyze("UPDATE dbo.Users |");
            var items = SuggestionBuilder.Build(context, SampleDatabase());

            Assert.Equal(new[] { "SET" }, items.Where(i => i.Group == items.Min(x => x.Group)).Select(i => i.InsertText).ToArray());
            Assert.False(context.SoftSelection);
        }

        [Fact]
        public void AfterAnUpdateAssignmentFromWhereOutput()
        {
            const string sql = "UPDATE dbo.Users SET Name = 'x' |";

            AssertFirst(sql, "FROM", "WHERE");
            Assert.Contains("OUTPUT", Order(sql));
            AssertNotFirst(sql, "GROUP BY", "ORDER BY", "JOIN");
        }

        [Fact]
        public void AfterAnUpdateWhereNoGroupBy()
        {
            const string sql = "UPDATE dbo.Users SET Name = 'x' WHERE Id = 1 |";

            Assert.Contains("AND", Above(sql));
            Assert.Contains("OR", Above(sql));
            Assert.DoesNotContain("GROUP BY", Above(sql));
        }

        [Fact]
        public void DeleteOffersFromAndTop()
        {
            var keywords = Analyze("DELETE |").PreferredKeywords;

            Assert.Contains("FROM", keywords);
            Assert.Contains("TOP", keywords);
        }

        [Fact]
        public void AfterDeleteFromTableWhere()
        {
            AssertFirst("DELETE FROM dbo.Users |", "WHERE");
            AssertNotFirst("DELETE FROM dbo.Users |", "JOIN", "GROUP BY");
        }

        [Theory]
        [InlineData("CREATE |", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "INDEX", "TRIGGER", "OR ALTER")]
        [InlineData("ALTER |", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "INDEX", "TRIGGER")]
        [InlineData("DROP |", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "INDEX", "TRIGGER")]
        [InlineData("CREATE OR |", "ALTER")]
        public void DdlStartsWithTheObjectType(string sql, params string[] expected)
        {
            AssertFirst(sql, expected);
        }

        [Fact]
        public void AlterDoesNotOfferOrAlter()
        {
            AssertNotFirst("ALTER |", "OR ALTER");
        }

        [Fact]
        public void DropTableOffersIfExists()
        {
            Assert.Contains("IF EXISTS", Analyze("DROP TABLE |").PreferredKeywords);
            AssertFirst("DROP VIEW |", "IF EXISTS");
        }

        [Fact]
        public void BeginOffersTransactionAndTry()
        {
            AssertFirst("BEGIN |", "TRANSACTION", "TRAN", "TRY");
            AssertNotFirst("BEGIN |", "SELECT");
            AssertFirst("BEGIN TRY SELECT 1 END TRY BEGIN |", "CATCH");
        }

        [Fact]
        public void CaseOffersWhen()
        {
            AssertFirst("SELECT CASE | FROM dbo.Users u", "WHEN");
            Assert.Contains("WHEN", Above("SELECT CASE u.Id |"));
        }

        [Fact]
        public void AfterWhenConditionThen()
        {
            const string sql = "SELECT CASE WHEN u.Id = 1 | FROM dbo.Users u";

            Assert.Contains("THEN", Above(sql));
            Assert.DoesNotContain("ELSE", Above(sql));
            Assert.DoesNotContain("END", Above(sql));
        }

        [Fact]
        public void AfterThenWhenElseEnd()
        {
            var afterThen = Above("SELECT CASE WHEN u.Id = 1 THEN 'a' | FROM dbo.Users u");
            Assert.Contains("WHEN", afterThen);
            Assert.Contains("ELSE", afterThen);
            Assert.Contains("END", afterThen);

            var afterElse = Above("SELECT CASE WHEN u.Id = 1 THEN 'a' ELSE 'b' | FROM dbo.Users u");
            Assert.Contains("END", afterElse);
            Assert.DoesNotContain("WHEN", afterElse);
            Assert.DoesNotContain("ELSE", afterElse);
        }

        [Fact]
        public void AfterEndOfCaseTheSelectItemContinues()
        {
            var above = Above("SELECT CASE WHEN u.Id = 1 THEN 'a' END |");

            Assert.Contains("AS", above);
            Assert.Contains("FROM", above);
        }

        [Fact]
        public void StatementStartOffersTheMostUsedFirst()
        {
            var items = SuggestionBuilder.Build(Analyze("|"), null);
            var first = items.Where(i => i.Group == items.Min(x => x.Group)).Select(i => i.InsertText).ToArray();

            foreach (var keyword in new[] { "SELECT", "INSERT INTO", "UPDATE", "DELETE FROM", "WITH", "DECLARE", "SET", "EXEC", "IF", "BEGIN" })
            {
                Assert.Contains(keyword, first);
            }

            Assert.DoesNotContain("COMMIT", first);
            Assert.Contains(items, i => i.InsertText == "COMMIT");
        }

        [Fact]
        public void NestedSelectUsesItsOwnPosition()
        {
            AssertFirst("SELECT * FROM dbo.Users u WHERE u.Id IN (SELECT o.UserId FROM dbo.Orders o |", "WHERE", "JOIN");
        }

        [Fact]
        public void FunctionArgumentsHaveNoKeywordPosition()
        {
            Assert.Empty(Above("SELECT COUNT(u.Id |"));
        }
    }
}
