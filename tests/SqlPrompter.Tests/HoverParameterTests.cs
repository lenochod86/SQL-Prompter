using System.Collections.Generic;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Navigation;
using SqlPrompter.Metadata;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The tooltip of a parameter in a call of a procedure (#453).</summary>
    public class HoverParameterTests
    {
        private static DatabaseSnapshot Db() =>
            new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales" }, new DbObject[0], "v1", procedures: new[]
            {
                new DbProcedure(10, "dbo", "Report", new[]
                {
                    new DbParameter("@id_firma", "int", false, false),
                    new DbParameter("@obdobi", "varchar(6)", false, false),
                    new DbParameter("@perioda", "int", false, true, "1"),
                    new DbParameter("@total", "money", true, true, "NULL"),
                }),
                new DbProcedure(11, "sales", "Report", new[] { new DbParameter("@Other", "int", false, false) }),
                new DbProcedure(12, "dbo", "NoArgs", new DbParameter[0]),
            });

        private static string Hover(string marked, IDatabaseMetadata other = null)
        {
            var caret = marked.IndexOf('|');
            return HoverInfo.At(marked.Remove(caret, 1), caret, Db(), db => string.Equals(db, "Archive", System.StringComparison.OrdinalIgnoreCase) ? other : null)?.Text;
        }

        [Fact]
        public void ANamedParameterShowsItsTypeAndPosition() =>
            Assert.Equal("parameter @obdobi of dbo.Report\nvarchar(6), parameter 2 of 4, required", Hover("EXEC dbo.Report @id_firma = 1, @obd|obi = '202610'"));

        [Fact]
        public void TheMultiLineCallOfTheIssueWorks() =>
            Assert.Equal("parameter @perioda of dbo.Report\nint, parameter 3 of 4, default = 1",
                Hover("EXEC dbo.Report @id_firma = 1\n                       , @obdobi = '202610'\n                       , @per|ioda = 1\n                       , @total = 5"));

        [Fact]
        public void OutputAndDefaultAreShown() =>
            Assert.Equal("parameter @total of dbo.Report\nmoney, parameter 4 of 4, OUTPUT, default = NULL", Hover("EXECUTE dbo.Report @tot|al = @t OUTPUT"));

        [Fact]
        public void TheSchemaIsOptionalAndTheDefaultOneIsPreferred() =>
            Assert.StartsWith("parameter @obdobi of dbo.Report", Hover("EXEC Report @obdo|bi = 1"));

        [Fact]
        public void AnotherSchemaIsFound() =>
            Assert.StartsWith("parameter @Other of sales.Report", Hover("EXEC sales.Report @Oth|er = 1"));

        [Fact]
        public void TheReturnVariableOfExecIsSkipped() =>
            Assert.StartsWith("parameter @id_firma of dbo.Report", Hover("DECLARE @r int; EXEC @r = dbo.Report @id_fi|rma = 1"));

        [Fact]
        public void AValueGivenByPositionShowsTheParameterItGoesTo()
        {
            Assert.Equal("parameter @obdobi of dbo.Report\nvarchar(6), parameter 2 of 4, required", Hover("EXEC dbo.Report 1, '2026|10'"));
            Assert.StartsWith("parameter @id_firma of dbo.Report", Hover("EXEC dbo.Report |1, 2"));
        }

        [Fact]
        public void APositionalValueAfterANamedOneIsNotDescribed() =>
            Assert.Null(Hover("EXEC dbo.Report @id_firma = 1, 5|"));

        [Fact]
        public void TheVariableOnTheRightSideKeepsItsOwnTooltip()
        {
            Assert.Equal("Variable @x: int", Hover("DECLARE @x int;\nEXEC dbo.Report @id_firma = @|x"));
            Assert.Null(Hover("EXEC dbo.Report @id_firma = @|undeclared"));
        }

        [Fact]
        public void AnUnknownParameterSaysSo() =>
            Assert.Equal("No parameter @nope in dbo.Report\n(4 parameters)", Hover("EXEC dbo.Report @no|pe = 1"));

        [Fact]
        public void TooManyPositionalValuesSaySo() =>
            Assert.StartsWith("No parameter 2 in dbo.NoArgs", Hover("EXEC dbo.NoArgs 1, |2"));

        [Fact]
        public void AProcedureThatIsNotFoundGivesNothing() =>
            Assert.Null(Hover("EXEC dbo.Missing @a| = 1"));

        [Fact]
        public void AnAssignmentOutsideExecIsNotAParameter() =>
            Assert.Null(Hover("SET @a| = 1"));

        [Fact]
        public void TheProcedureOfAnotherDatabaseIsUsedWhenItIsLoaded()
        {
            var other = new DatabaseSnapshot("Archive", "dbo", new[] { "dbo" }, new DbObject[0], "v1",
                procedures: new[] { new DbProcedure(1, "dbo", "Purge", new[] { new DbParameter("@days", "int", false, false) }) });

            Assert.Equal("parameter @days of dbo.Purge\nint, parameter 1 of 1, required", Hover("EXEC Archive.dbo.Purge @da|ys = 3", other));
            Assert.Null(Hover("EXEC Archive.dbo.Purge @da|ys = 3"));
        }
    }
}
