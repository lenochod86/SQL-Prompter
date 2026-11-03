using SqlPrompter.Core.Analysis;
using Xunit;

namespace SqlPrompter.Tests
{
    public class SchemaChangeDetectorTests
    {
        [Theory]
        [InlineData("CREATE TABLE dbo.NewTable (Id int)")]
        [InlineData("create view v as select 1 as x")]
        [InlineData("CREATE OR ALTER PROCEDURE dbo.p AS SELECT 1")]
        [InlineData("CREATE PROC dbo.p AS SELECT 1")]
        [InlineData("ALTER FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END")]
        [InlineData("ALTER TABLE dbo.T ADD Name nvarchar(50)")]
        [InlineData("DROP TABLE IF EXISTS dbo.T")]
        [InlineData("CREATE SCHEMA sales")]
        [InlineData("CREATE SYNONYM dbo.s FOR dbo.T")]
        [InlineData("CREATE TYPE dbo.Ids AS TABLE (Id int)")]
        [InlineData("CREATE TRIGGER t ON dbo.T AFTER INSERT AS SELECT 1")]
        [InlineData("CREATE SEQUENCE dbo.s")]
        [InlineData("EXEC sp_rename 'dbo.T', 'U'")]
        [InlineData("EXEC sys.sp_rename 'dbo.T.Name', 'Title', 'COLUMN'")]
        [InlineData("SELECT * INTO dbo.Copy FROM dbo.T")]
        [InlineData("SELECT a, b INTO #tmp FROM dbo.T")]
        [InlineData("SELECT 1\nGO\nCREATE TABLE dbo.T (Id int)")]
        [InlineData("-- comment\nCREATE TABLE dbo.T (Id int)")]
        public void DetectsSchemaChange(string script)
        {
            Assert.True(SchemaChangeDetector.ChangesSchema(script));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("SELECT * FROM dbo.T")]
        [InlineData("INSERT INTO dbo.T (Id) VALUES (1)")]
        [InlineData("INSERT INTO dbo.T (Id) SELECT Id FROM dbo.U")]
        [InlineData("MERGE INTO dbo.T t USING dbo.U u ON t.Id = u.Id WHEN MATCHED THEN DELETE;")]
        [InlineData("UPDATE dbo.T SET Name = 'CREATE TABLE x'")]
        [InlineData("-- DROP TABLE dbo.T\nSELECT 1")]
        [InlineData("/* ALTER TABLE dbo.T ADD x int */ SELECT 1")]
        [InlineData("CREATE INDEX IX_T ON dbo.T (Id)")]
        [InlineData("DECLARE c CURSOR FOR SELECT Id FROM dbo.T; FETCH NEXT FROM c INTO @id")]
        [InlineData("SELECT Id FROM dbo.T WHERE Id IN (SELECT Id FROM dbo.U)")]
        [InlineData("DELETE FROM dbo.T")]
        public void IgnoresScriptWithoutSchemaChange(string script)
        {
            Assert.False(SchemaChangeDetector.ChangesSchema(script));
        }
    }
}
