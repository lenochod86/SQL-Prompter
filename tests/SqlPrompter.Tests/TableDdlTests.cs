using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>CREATE TABLE column definitions and ALTER TABLE (#213).</summary>
    public class TableDdlTests
    {
        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), SampleDatabase());

        /// <summary>The upper-cased keywords of the keyword items that are preferred (the first group).</summary>
        private static string[] First(string sql)
        {
            var keywords = Items(sql).Where(i => i.Kind == SuggestionKind.Keyword && !i.IsBlock).ToList();
            var group = keywords.Min(k => k.Group);
            return keywords.Where(k => k.Group == group).Select(k => k.InsertText.ToUpperInvariant()).ToArray();
        }

        private static void AssertFirst(string sql, params string[] expected)
        {
            var first = First(sql);
            foreach (var keyword in expected)
            {
                Assert.Contains(keyword, first);
            }
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (|")]
        [InlineData("CREATE TABLE dbo.T (Id int NOT NULL, |")]
        [InlineData("DECLARE @t TABLE (|")]
        public void ANewItemOffersTheTableConstraints(string sql)
        {
            AssertFirst(sql, "CONSTRAINT", "PRIMARY KEY", "UNIQUE", "FOREIGN KEY", "CHECK");
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (Id |")]
        [InlineData("CREATE TABLE dbo.T (Id int, Name |")]
        [InlineData("DECLARE @t TABLE (Id |")]
        [InlineData("ALTER TABLE dbo.Users ALTER COLUMN Name |")]
        [InlineData("ALTER TABLE dbo.Users ADD Phone |")]
        [InlineData("ALTER TABLE dbo.Users ADD COLUMN Phone |")]
        public void AfterTheColumnNameComeTheDataTypes(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.DataType, context.Kind);
            var items = SuggestionBuilder.Build(context, SampleDatabase());
            Assert.Contains(items, i => i.Kind == SuggestionKind.DataType && i.InsertText.ToUpperInvariant() == "INT");
        }

        [Fact]
        public void TheDataTypesComeWithTheUsualLengths()
        {
            var items = Items("CREATE TABLE dbo.T (Phone |").Where(i => i.Kind == SuggestionKind.DataType && i.Group < 5).OrderBy(i => i.Group).Select(i => i.InsertText).ToArray();

            Assert.Equal(new[] { "nvarchar(50)", "nvarchar(100)", "nvarchar(255)", "nvarchar(MAX)" }, items.Take(4).ToArray());
            Assert.Contains("decimal(18,2)", items);
        }

        [Fact]
        public void ADeclareWithoutTableHasNoSizes()
        {
            Assert.DoesNotContain(Items("DECLARE @x |"), i => i.InsertText == "nvarchar(100)");
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (Id int |", "NOT NULL", "NULL", "IDENTITY(1,1)", "DEFAULT", "PRIMARY KEY", "UNIQUE", "REFERENCES", "CHECK", "COLLATE")]
        [InlineData("CREATE TABLE dbo.T (Name nvarchar(50) |", "NOT NULL", "NULL", "DEFAULT", "PRIMARY KEY")]
        [InlineData("CREATE TABLE dbo.T (Id int NOT |", "NULL")]
        [InlineData("CREATE TABLE dbo.T (Id int PRIMARY |", "KEY")]
        [InlineData("CREATE TABLE dbo.T (Id int PRIMARY KEY |", "CLUSTERED", "NONCLUSTERED")]
        [InlineData("CREATE TABLE dbo.T (Id int NOT NULL |", "IDENTITY(1,1)", "DEFAULT", "PRIMARY KEY")]
        public void ColumnOptions(string sql, params string[] expected)
        {
            AssertFirst(sql, expected);
        }

        [Fact]
        public void OptionsThatAreThereAlreadyAreNotOfferedAgain()
        {
            var first = First("CREATE TABLE dbo.T (Id int NOT NULL IDENTITY(1,1) |");

            Assert.DoesNotContain("NULL", first);
            Assert.DoesNotContain("NOT NULL", first);
            Assert.DoesNotContain("IDENTITY(1,1)", first);
            Assert.Contains("PRIMARY KEY", first);
        }

        [Fact]
        public void ReferencesOffersTheTables()
        {
            var context = Analyze("CREATE TABLE dbo.T (IdUser int REFERENCES |");

            Assert.Equal(ContextKind.ObjectName, context.Kind);
            Assert.Contains(SuggestionBuilder.Build(context, SampleDatabase()), i => i.Kind == SuggestionKind.Table && i.DisplayText == "Users");
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (IdUser int REFERENCES dbo.Users |", "ON DELETE", "ON UPDATE")]
        [InlineData("CREATE TABLE dbo.T (IdUser int REFERENCES dbo.Users (Id) |", "ON DELETE", "ON UPDATE")]
        [InlineData("CREATE TABLE dbo.T (IdUser int REFERENCES dbo.Users (Id) ON |", "DELETE", "UPDATE")]
        [InlineData("CREATE TABLE dbo.T (IdUser int REFERENCES dbo.Users (Id) ON DELETE |", "CASCADE", "SET NULL", "SET DEFAULT", "NO ACTION")]
        [InlineData("CREATE TABLE dbo.T (IdUser int REFERENCES dbo.Users (Id) ON DELETE SET |", "NULL", "DEFAULT")]
        [InlineData("CREATE TABLE dbo.T (IdUser int REFERENCES dbo.Users (Id) ON DELETE NO |", "ACTION")]
        [InlineData("CREATE TABLE dbo.T (IdUser int REFERENCES dbo.Users (Id) ON DELETE CASCADE |", "ON UPDATE")]
        public void ReferencesAndTheirActions(string sql, params string[] expected)
        {
            AssertFirst(sql, expected);
        }

        [Fact]
        public void OnDeleteIsNotOfferedTwice()
        {
            Assert.DoesNotContain("ON DELETE", First("CREATE TABLE dbo.T (IdUser int REFERENCES dbo.Users (Id) ON DELETE CASCADE |"));
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (CONSTRAINT |")]
        [InlineData("CREATE TABLE dbo.T (Id int CONSTRAINT |")]
        public void ConstraintNamesAreYoursToType(string sql)
        {
            Assert.Equal(ContextKind.Name, Analyze(sql).Kind);
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (CONSTRAINT PK_T |", "PRIMARY KEY", "FOREIGN KEY", "UNIQUE", "CHECK", "DEFAULT")]
        [InlineData("CREATE TABLE dbo.T (CONSTRAINT PK_T PRIMARY |", "KEY")]
        [InlineData("CREATE TABLE dbo.T (CONSTRAINT PK_T PRIMARY KEY |", "CLUSTERED", "NONCLUSTERED")]
        [InlineData("CREATE TABLE dbo.T (CONSTRAINT FK_T FOREIGN |", "KEY")]
        [InlineData("CREATE TABLE dbo.T (CONSTRAINT FK_T FOREIGN KEY (IdUser) |", "REFERENCES")]
        public void TableConstraints(string sql, params string[] expected)
        {
            AssertFirst(sql, expected);
        }

        [Fact]
        public void ADefaultOffersTheFunctionsAndNotTheOptions()
        {
            var context = Analyze("CREATE TABLE dbo.T (Created datetime2 DEFAULT |");

            Assert.Equal(ContextKind.Fallback, context.Kind);
        }

        [Theory]
        [InlineData("ALTER TABLE dbo.Users |", "ADD", "ALTER COLUMN", "DROP COLUMN", "DROP CONSTRAINT")]
        [InlineData("ALTER TABLE dbo.Users WITH |", "CHECK", "NOCHECK")]
        [InlineData("ALTER TABLE dbo.Users WITH CHECK |", "ADD")]
        [InlineData("ALTER TABLE dbo.Users ENABLE |", "TRIGGER")]
        [InlineData("ALTER TABLE dbo.Users DISABLE |", "TRIGGER")]
        [InlineData("ALTER TABLE dbo.Users NOCHECK |", "CONSTRAINT")]
        [InlineData("ALTER TABLE dbo.Users ALTER |", "COLUMN")]
        [InlineData("ALTER TABLE dbo.Users DROP |", "COLUMN", "CONSTRAINT")]
        [InlineData("ALTER TABLE dbo.Users ADD |", "CONSTRAINT")]
        [InlineData("ALTER TABLE dbo.Users ADD CONSTRAINT PK_U |", "PRIMARY KEY", "FOREIGN KEY", "UNIQUE", "CHECK", "DEFAULT")]
        [InlineData("ALTER TABLE dbo.Users ALTER COLUMN Name nvarchar(50) |", "NULL", "NOT NULL", "COLLATE")]
        [InlineData("ALTER TABLE dbo.Users ADD Phone nvarchar(20) |", "NOT NULL", "DEFAULT", "PRIMARY KEY")]
        public void AlterTable(string sql, params string[] expected)
        {
            AssertFirst(sql, expected);
        }

        [Theory]
        [InlineData("ALTER TABLE dbo.Users ALTER COLUMN |")]
        [InlineData("ALTER TABLE dbo.Users DROP COLUMN |")]
        [InlineData("ALTER TABLE dbo.Users DROP COLUMN Name, |")]
        public void ColumnsOfTheTable(string sql)
        {
            var context = Analyze(sql);

            Assert.Equal(ContextKind.InsertColumns, context.Kind);
            var columns = SuggestionBuilder.Build(context, SampleDatabase()).Where(i => i.Kind == SuggestionKind.Column).Select(i => i.DisplayText).ToArray();
            Assert.Equal(new[] { "Id", "Name", "Email" }, columns);
        }

        [Fact]
        public void DropConstraintNamesAreYoursToType()
        {
            Assert.Equal(ContextKind.Name, Analyze("ALTER TABLE dbo.Users DROP CONSTRAINT |").Kind);
        }

        [Fact]
        public void OtherStatementsAreUnchanged()
        {
            Assert.Equal(ContextKind.Keyword, Analyze("SELECT * FROM dbo.Users |").Kind);
            Assert.NotEqual(ContextKind.DataType, Analyze("CREATE PROCEDURE p AS SELECT |").Kind);
        }
    }
}
