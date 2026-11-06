using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>SET options, query hints and the ERROR_ functions in CATCH (#218).</summary>
    public class SetAndHintsTests
    {
        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), SampleDatabase());

        private static string[] First(string sql)
        {
            var keywords = Items(sql).Where(i => i.Kind == SuggestionKind.Keyword && !i.IsBlock).ToList();
            var group = keywords.Min(k => k.Group);
            return keywords.Where(k => k.Group == group).Select(k => k.InsertText.ToUpperInvariant()).ToArray();
        }

        [Fact]
        public void SetOffersTheUsualOptions()
        {
            var first = First("SET |");

            foreach (var option in new[] { "NOCOUNT ON", "XACT_ABORT ON", "TRANSACTION ISOLATION LEVEL", "ANSI_NULLS ON", "QUOTED_IDENTIFIER ON", "STATISTICS IO ON", "STATISTICS TIME ON", "IDENTITY_INSERT", "DATEFORMAT", "LANGUAGE" })
            {
                Assert.Contains(option, first);
            }
        }

        [Theory]
        [InlineData("SET NOCOUNT |", "ON", "OFF")]
        [InlineData("SET XACT_ABORT |", "ON", "OFF")]
        [InlineData("SET ANSI_NULLS |", "ON", "OFF")]
        [InlineData("SET TRANSACTION |", "ISOLATION LEVEL")]
        [InlineData("SET TRANSACTION ISOLATION |", "LEVEL")]
        [InlineData("SET TRANSACTION ISOLATION LEVEL |", "READ UNCOMMITTED", "READ COMMITTED", "REPEATABLE READ", "SNAPSHOT", "SERIALIZABLE")]
        [InlineData("SET STATISTICS |", "IO", "TIME", "XML", "PROFILE")]
        [InlineData("SET STATISTICS IO |", "ON", "OFF")]
        [InlineData("SET IDENTITY_INSERT dbo.Users |", "ON", "OFF")]
        [InlineData("SET DATEFORMAT |", "DMY", "MDY", "YMD")]
        public void SetOptionsHaveTheirValues(string sql, params string[] expected)
        {
            var first = First(sql);

            Assert.Equal(expected.OrderBy(x => x), first.OrderBy(x => x).Where(x => expected.Contains(x)));
        }

        [Fact]
        public void IdentityInsertOffersTheTables()
        {
            var context = Analyze("SET IDENTITY_INSERT |");

            Assert.Equal(ContextKind.ObjectName, context.Kind);
            Assert.Contains(SuggestionBuilder.Build(context, SampleDatabase()), i => i.Kind == SuggestionKind.Table);
        }

        [Fact]
        public void AVariableAssignmentIsNoSetOption()
        {
            Assert.DoesNotContain("NOCOUNT ON", First("SET @x = |"));
        }

        [Fact]
        public void UpdateSetIsNotTheSetStatement()
        {
            Assert.DoesNotContain("NOCOUNT ON", First("UPDATE dbo.Users SET |"));
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Users OPTION (|")]
        [InlineData("SELECT * FROM dbo.Users OPTION (RECOMPILE, |")]
        [InlineData("UPDATE dbo.Users SET Name = 'a' OPTION (|")]
        public void OptionOffersTheQueryHintsOnly(string sql)
        {
            var context = Analyze(sql);
            var keywords = SuggestionBuilder.Build(context, SampleDatabase()).Where(i => i.Kind == SuggestionKind.Keyword).Select(i => i.InsertText.ToUpperInvariant()).ToArray();

            foreach (var hint in new[] { "RECOMPILE", "MAXDOP", "OPTIMIZE FOR", "HASH JOIN", "LOOP JOIN", "MERGE JOIN", "FORCE ORDER", "USE HINT", "MAXRECURSION" })
            {
                Assert.Contains(hint, keywords);
            }

            Assert.DoesNotContain("SELECT", keywords);
        }

        [Fact]
        public void TheErrorFunctionsComeFirstInsideCatch()
        {
            var items = Items("BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT |");

            var first = items.Where(i => i.Kind == SuggestionKind.Function && i.Group == 0).Select(i => i.DisplayText).OrderBy(x => x).ToArray();

            Assert.Equal(new[] { "ERROR_LINE", "ERROR_MESSAGE", "ERROR_NUMBER", "ERROR_PROCEDURE", "ERROR_SEVERITY", "ERROR_STATE", "XACT_STATE" }, first);
        }

        [Fact]
        public void OutsideCatchTheyAreOrdinaryFunctions()
        {
            var items = Items("SELECT |");

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Function && i.DisplayText == "ERROR_MESSAGE" && i.Group == 0);
            Assert.Contains(items, i => i.Kind == SuggestionKind.Function && i.DisplayText == "ERROR_MESSAGE");
        }

        [Theory]
        [InlineData("BEGIN CATCH SELECT |", true)]
        [InlineData("BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 1; |", true)]
        [InlineData("BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 1 END CATCH SELECT |", false)]
        [InlineData("BEGIN TRY SELECT |", false)]
        [InlineData("SELECT |", false)]
        public void TheCatchBlockIsRecognized(string sql, bool inside)
        {
            var caret = sql.IndexOf('|');

            Assert.Equal(inside, CatchBlock.IsInside(sql.Remove(caret, 1), caret));
        }
    }
}
