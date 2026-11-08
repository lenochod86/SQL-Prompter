using SqlPrompter.Core.Navigation;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The complete script of a table (#351).</summary>
    public class TableScriptTests
    {
        private static TableScriptModel Cities()
        {
            var table = new TableScriptModel { Schema = "dbo", Name = "Cities" };
            table.Columns.Add(new ScriptTableColumn("IdCity", "int", false, "IDENTITY(1,1)"));
            table.Columns.Add(new ScriptTableColumn("IdCountry", "int", false, null));
            table.Columns.Add(new ScriptTableColumn("Name", "nvarchar(80)", false, null) { Collation = "Latin1_General_CI_AS", DefaultName = "DF_Cities_Name", DefaultText = "(N'?')" });
            table.Columns.Add(new ScriptTableColumn("Population", "int", true, null));
            table.Columns.Add(new ScriptTableColumn("Big", "int", true, null) { ComputedText = "([Population]/(1000))", IsPersisted = true });

            var pk = new TableIndexScript { Name = "PK_Cities", IsPrimaryKey = true, IsUnique = true };
            pk.KeyColumns.Add("[IdCity]");
            table.Indexes.Add(pk);
            var unique = new TableIndexScript { Name = "UQ_Cities_Name", IsUniqueConstraint = true, IsUnique = true };
            unique.KeyColumns.Add("[Name]");
            table.Indexes.Add(unique);

            var clustered = new TableIndexScript { Name = "CX_Cities", IsUnique = true, IsClustered = true };
            clustered.KeyColumns.Add("[IdCountry]");
            clustered.KeyColumns.Add("[Name] DESC");
            clustered.Options.Add("FILLFACTOR = 80");
            table.Indexes.Add(clustered);
            var filtered = new TableIndexScript { Name = "IX_Cities_Filtered", Filter = "([Population] IS NOT NULL)" };
            filtered.KeyColumns.Add("[Name]");
            table.Indexes.Add(filtered);
            var included = new TableIndexScript { Name = "IX_Cities_Included" };
            included.KeyColumns.Add("[IdCountry]");
            included.IncludedColumns.Add("[Name]");
            included.IncludedColumns.Add("[Population]");
            table.Indexes.Add(included);

            table.Checks.Add(new TableCheckScript { Name = "CK_Cities_Population", Definition = "([Population]>=(0))" });

            var fk = new TableForeignKeyScript { Name = "FK_Cities_Countries", ReferencedSchema = "dbo", ReferencedTable = "Countries", DeleteAction = "CASCADE", UpdateAction = "NO_ACTION" };
            fk.Columns.Add("IdCountry");
            fk.ReferencedColumns.Add("IdCountry");
            table.ForeignKeys.Add(fk);
            var second = new TableForeignKeyScript { Name = "FK_Cities_Regions", ReferencedSchema = "dbo", ReferencedTable = "Regions", DeleteAction = "NO_ACTION", UpdateAction = "SET_NULL", IsNotTrusted = true, IsDisabled = true, IsNotForReplication = true };
            second.Columns.Add("IdRegion");
            second.ReferencedColumns.Add("IdRegion");
            table.ForeignKeys.Add(second);

            table.ReferencedBy.Add(new TableReferencedBy { Schema = "dbo", Table = "Visits", ForeignKey = "FK_Visits_Cities" });
            table.Triggers.Add(new TableTriggerScript { Name = "TR_Cities", Definition = "\r\nCREATE TRIGGER dbo.TR_Cities ON dbo.Cities AFTER INSERT AS SELECT 1;\r\n" });
            return table;
        }

        [Fact]
        public void TheScriptHasTheTableTheForeignKeysTheIndexesAndTheTriggers()
        {
            var script = DefinitionScript.ForTable(Cities());

            Assert.Contains("CREATE TABLE [dbo].[Cities]\r\n(\r\n    [IdCity] int IDENTITY(1,1) NOT NULL,", script);
            Assert.Contains("[Name] nvarchar(80) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF_Cities_Name] DEFAULT (N'?'),", script);
            Assert.Contains("[Big] AS ([Population]/(1000)) PERSISTED,", script);
            Assert.Contains("CONSTRAINT [PK_Cities] PRIMARY KEY NONCLUSTERED ([IdCity]),", script);
            Assert.Contains("CONSTRAINT [UQ_Cities_Name] UNIQUE NONCLUSTERED ([Name]),", script);
            Assert.Contains("CONSTRAINT [CK_Cities_Population] CHECK ([Population]>=(0))", script);
            Assert.Contains("ALTER TABLE [dbo].[Cities] WITH CHECK ADD CONSTRAINT [FK_Cities_Countries] FOREIGN KEY ([IdCountry]) REFERENCES [dbo].[Countries] ([IdCountry]) ON DELETE CASCADE;", script);
            Assert.Contains("ALTER TABLE [dbo].[Cities] WITH NOCHECK ADD CONSTRAINT [FK_Cities_Regions] FOREIGN KEY ([IdRegion]) REFERENCES [dbo].[Regions] ([IdRegion]) ON UPDATE SET NULL NOT FOR REPLICATION;", script);
            Assert.Contains("ALTER TABLE [dbo].[Cities] NOCHECK CONSTRAINT [FK_Cities_Regions];", script);
            Assert.Contains("CREATE UNIQUE CLUSTERED INDEX [CX_Cities] ON [dbo].[Cities] ([IdCountry], [Name] DESC) WITH (FILLFACTOR = 80);", script);
            Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Cities_Filtered] ON [dbo].[Cities] ([Name]) WHERE ([Population] IS NOT NULL);", script);
            Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Cities_Included] ON [dbo].[Cities] ([IdCountry]) INCLUDE ([Name], [Population]);", script);
            Assert.Contains("GO\r\nCREATE TRIGGER dbo.TR_Cities ON dbo.Cities AFTER INSERT AS SELECT 1;\r\nGO", script);
            Assert.Contains("-- Referenced by: [dbo].[Visits] (FK_Visits_Cities)", script);
        }

        [Fact]
        public void TheOrderIsTableThenForeignKeysThenIndexesThenTriggers()
        {
            var script = DefinitionScript.ForTable(Cities());

            Assert.True(script.IndexOf("CREATE TABLE") < script.IndexOf("ADD CONSTRAINT [FK_Cities_Countries]"));
            Assert.True(script.IndexOf("ADD CONSTRAINT [FK_Cities_Countries]") < script.IndexOf("CREATE UNIQUE CLUSTERED INDEX"));
            Assert.True(script.IndexOf("CREATE NONCLUSTERED INDEX [IX_Cities_Included]") < script.IndexOf("CREATE TRIGGER"));
        }

        [Fact]
        public void ATableWithOnlyColumnsGivesJustTheCreateTable()
        {
            var table = new TableScriptModel { Schema = "dbo", Name = "T" };
            table.Columns.Add(new ScriptTableColumn("A", "int", true, null));

            Assert.Equal("CREATE TABLE [dbo].[T]\r\n(\r\n    [A] int NULL\r\n);\r\n", DefinitionScript.ForTable(table));
        }
    }
}
