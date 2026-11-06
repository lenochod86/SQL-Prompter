using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>Operators after a column in a condition and values after the operator, by the type of the column (#204).</summary>
    public class OperatorSuggestionTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "Orders", DbObjectKind.Table, false),
            };
            return new DatabaseSnapshot("test", "dbo", new[] { "dbo" }, objects, "v1").WithColumns(new Dictionary<int, IReadOnlyList<DbColumn>>
            {
                [1] = new[]
                {
                    new DbColumn("Id", "int", false),
                    new DbColumn("Name", "nvarchar(50)", true),
                    new DbColumn("Email", "varchar(100)", false),
                    new DbColumn("Created", "datetime2", false),
                    new DbColumn("Active", "bit", false),
                    new DbColumn("Rate", "decimal(18,2)", true),
                },
                [2] = new[] { new DbColumn("Id", "int", false), new DbColumn("UserId", "int", false), new DbColumn("Placed", "datetime", false) },
            });
        }

        private static IReadOnlyList<Suggestion> Items(string sql)
        {
            var caret = sql.IndexOf('|');
            return SuggestionBuilder.Build(Analyze(sql), Database(), ScriptModel.Build(sql.Remove(caret, 1), caret));
        }

        private static string[] Operators(string sql) => Items(sql).Where(i => i.Kind == SuggestionKind.Keyword && i.Group < -20).OrderBy(i => i.Group).Select(i => i.DisplayText).ToArray();

        [Theory]
        [InlineData("SELECT * FROM dbo.Users WHERE Name |")]
        [InlineData("SELECT * FROM dbo.Users u WHERE u.Name |")]
        [InlineData("SELECT * FROM dbo.Users WHERE Id = 1 AND Name |")]
        [InlineData("SELECT * FROM dbo.Users WHERE Id = 1 OR Name |")]
        [InlineData("SELECT * FROM dbo.Users WHERE NOT Name |")]
        [InlineData("SELECT * FROM dbo.Users WHERE (Name |")]
        [InlineData("SELECT * FROM dbo.Users GROUP BY Id HAVING Name |")]
        [InlineData("SELECT CASE WHEN Name | THEN 1 END FROM dbo.Users")]
        [InlineData("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON u.Name |")]
        public void OperatorsFollowAColumnInACondition(string sql)
        {
            Assert.Contains("LIKE", Operators(sql));
        }

        [Theory]
        [InlineData("SELECT Name |  FROM dbo.Users")]
        [InlineData("SELECT * FROM dbo.Users WHERE |")]
        [InlineData("SELECT * FROM dbo.Users ORDER BY Name |")]
        [InlineData("SELECT * FROM dbo.Users WHERE Name = 'a' |")]
        [InlineData("SELECT * FROM dbo.Users WHERE UPPER(Name) |")]
        public void NoOperatorsElsewhere(string sql)
        {
            Assert.Empty(Operators(sql));
        }

        [Fact]
        public void TextHasLike()
        {
            var operators = Operators("SELECT * FROM dbo.Users WHERE Email |");

            Assert.Equal(new[] { "=", "<>", "LIKE", "NOT LIKE", "IN", "NOT IN", "IS NULL", "IS NOT NULL" }, operators);
        }

        [Fact]
        public void NumbersAndDatesHaveComparisonsAndBetweenButNoLike()
        {
            foreach (var sql in new[] { "SELECT * FROM dbo.Users WHERE Id |", "SELECT * FROM dbo.Users WHERE Created |" })
            {
                var operators = Operators(sql);
                Assert.Equal(new[] { "=", "<>", "<", ">", "<=", ">=", "BETWEEN", "IN", "NOT IN", "IS NULL", "IS NOT NULL" }, operators);
            }
        }

        [Fact]
        public void BitHasNeitherLikeNorBetween()
        {
            var operators = Operators("SELECT * FROM dbo.Users WHERE Active |");

            Assert.DoesNotContain("LIKE", operators);
            Assert.DoesNotContain("BETWEEN", operators);
            Assert.Contains("=", operators);
        }

        [Fact]
        public void NullableColumnsOfferIsNullFirst()
        {
            Assert.Equal(new[] { "IS NULL", "IS NOT NULL" }, Operators("SELECT * FROM dbo.Users WHERE Name |").Take(2).ToArray());
            Assert.Equal("=", Operators("SELECT * FROM dbo.Users WHERE Email |")[0]);
        }

        [Fact]
        public void InComesWithParenthesesAndTheCaretBetweenThem()
        {
            var item = Items("SELECT * FROM dbo.Users WHERE Id |").Single(i => i.DisplayText == "IN" && i.Group < -20);

            Assert.Equal("IN ()", item.InsertText);
            Assert.Equal(1, item.CaretBack);
        }

        [Fact]
        public void TheColumnOfAnotherSourceIsFoundByItsAlias()
        {
            var operators = Operators("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON o.Placed |");

            Assert.Contains("BETWEEN", operators);
            Assert.DoesNotContain("LIKE", operators);
        }

        [Fact]
        public void AfterAComparisonColumnsOfTheSameTypeComeFirst()
        {
            var items = Items("SELECT * FROM dbo.Users u JOIN dbo.Orders o ON 1 = 1 WHERE u.Created = |");

            var created = items.First(i => i.Kind == SuggestionKind.Column && i.DisplayText == "Created");
            var name = items.First(i => i.Kind == SuggestionKind.Column && i.DisplayText == "Name");
            var placed = items.First(i => i.Kind == SuggestionKind.Column && i.DisplayText == "Placed");
            Assert.Equal(0, created.Group);
            Assert.Equal(0, placed.Group);
            Assert.True(name.Group > created.Group);
        }

        [Fact]
        public void AfterAComparisonVariablesOfTheTypeComeFirst()
        {
            var items = Items("DECLARE @from datetime, @n int;\nSELECT * FROM dbo.Users WHERE Created >= |");

            Assert.Equal(0, items.Single(i => i.DisplayText == "@from").Group);
            Assert.True(items.Single(i => i.DisplayText == "@n").Group > 0);
        }

        [Fact]
        public void AfterAComparisonWithADateTheCurrentTimeFunctionsComeFirst()
        {
            var items = Items("SELECT * FROM dbo.Users WHERE Created < |");

            Assert.Equal(0, items.Single(i => i.DisplayText == "GETDATE" || i.DisplayText == "GETDATE()").Group);
            Assert.True(items.First(i => i.Kind == SuggestionKind.Function && i.DisplayText.StartsWith("ABS")).Group > 0);
        }

        [Fact]
        public void NoOperatorsAfterTheComparison()
        {
            Assert.Empty(Operators("SELECT * FROM dbo.Users WHERE Name = |"));
        }

        [Fact]
        public void TypeClasses()
        {
            Assert.Equal(TypeClass.Text, OperatorSuggestions.Classify("nvarchar(50)"));
            Assert.Equal(TypeClass.Text, OperatorSuggestions.Classify("NVARCHAR(MAX)"));
            Assert.Equal(TypeClass.Number, OperatorSuggestions.Classify("decimal(18,2)"));
            Assert.Equal(TypeClass.Date, OperatorSuggestions.Classify("datetime2"));
            Assert.Equal(TypeClass.Bit, OperatorSuggestions.Classify("bit"));
            Assert.Equal(TypeClass.Guid, OperatorSuggestions.Classify("uniqueidentifier"));
            Assert.Equal(TypeClass.Unknown, OperatorSuggestions.Classify("varbinary(8)"));
        }
    }
}
