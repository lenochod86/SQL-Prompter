using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Navigation;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>ALTER / CREATE OR ALTER / DROP of procedures, functions, views and triggers: the lists and the script (#203).</summary>
    public class AlterModuleTests
    {
        private static DatabaseSnapshot Database()
        {
            var objects = new[]
            {
                new DbObject(1, "dbo", "Users", DbObjectKind.Table, false),
                new DbObject(2, "dbo", "ActiveUsers", DbObjectKind.View, false),
                new DbObject(3, "sales", "OrderView", DbObjectKind.View, false),
                new DbObject(4, "dbo", "fnTotal", DbObjectKind.ScalarFunction, false),
                new DbObject(5, "dbo", "fnUsers", DbObjectKind.TableFunction, false),
                new DbObject(100, "sys", "objects", DbObjectKind.View, true),
            };
            var procedures = new[]
            {
                new DbProcedure(10, "dbo", "GetUsers", new DbParameter[0]),
                new DbProcedure(11, "sales", "GetOrders", new DbParameter[0]),
            };
            var triggers = new[]
            {
                new DbTrigger("dbo", "trUsersInsert", "dbo.Users"),
                new DbTrigger(null, "trDatabaseDdl", null),
            };
            return new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales", "sys" }, objects, "v1", procedures: procedures, triggers: triggers);
        }

        private static IReadOnlyList<Suggestion> Items(string sql) => SuggestionBuilder.Build(Analyze(sql), Database());

        [Theory]
        [InlineData("ALTER PROCEDURE |", "GetUsers", "GetOrders")]
        [InlineData("ALTER PROC |", "GetUsers", "GetOrders")]
        [InlineData("alter procedure get|", "GetUsers", "GetOrders")]
        [InlineData("CREATE OR ALTER PROCEDURE |", "GetUsers", "GetOrders")]
        [InlineData("DROP PROCEDURE |", "GetUsers", "GetOrders")]
        [InlineData("ALTER VIEW |", "ActiveUsers", "OrderView")]
        [InlineData("CREATE OR ALTER VIEW |", "ActiveUsers", "OrderView")]
        [InlineData("ALTER FUNCTION |", "fnTotal", "fnUsers")]
        [InlineData("ALTER TRIGGER |", "trUsersInsert", "trDatabaseDdl")]
        [InlineData("DROP TRIGGER |", "trUsersInsert", "trDatabaseDdl")]
        public void OnlyObjectsOfTheKindAreOffered(string sql, params string[] expected)
        {
            var context = Analyze(sql);
            Assert.Equal(ContextKind.ObjectName, context.Kind);

            var items = SuggestionBuilder.Build(context, Database());

            Assert.Equal(expected.OrderBy(x => x), items.Where(i => i.Kind != SuggestionKind.Keyword).Select(i => i.DisplayText).OrderBy(x => x));
        }

        [Fact]
        public void ASchemaNarrowsTheList()
        {
            Assert.Equal(new[] { "GetOrders" }, Items("ALTER PROCEDURE sales.|").Select(i => i.DisplayText).ToArray());
        }

        [Fact]
        public void TheNameIsQualifiedUnlessTheSchemaWasTyped()
        {
            Assert.Contains(Items("ALTER PROCEDURE |"), i => i.InsertText == "dbo.GetUsers");
            Assert.Contains(Items("ALTER PROCEDURE dbo.|"), i => i.InsertText == "GetUsers");
            Assert.Contains(Items("ALTER TRIGGER |"), i => i.InsertText == "trDatabaseDdl");
        }

        [Fact]
        public void TheTriggerShowsItsParent()
        {
            var item = Items("ALTER TRIGGER |").Single(i => i.DisplayText == "trUsersInsert");

            Assert.Equal("dbo.Users", item.Suffix);
        }

        [Fact]
        public void AlterAndCreateOrAlterItemsCarryTheScriptTarget()
        {
            var alter = (ModuleScript)Items("ALTER PROCEDURE |").Single(i => i.DisplayText == "GetUsers").Tag;
            Assert.Equal(ModuleKind.Procedure, alter.Kind);
            Assert.Equal("dbo", alter.Schema);
            Assert.False(alter.CreateOrAlter);
            Assert.Equal(0, alter.HeaderStart);

            var createOrAlter = (ModuleScript)Items("SELECT 1;\nCREATE OR ALTER VIEW |").Single(i => i.DisplayText == "ActiveUsers").Tag;
            Assert.True(createOrAlter.CreateOrAlter);
            Assert.Equal(0, createOrAlter.HeaderStart - "SELECT 1;\n".Length);
        }

        [Fact]
        public void DropOffersTheNamesOnly()
        {
            Assert.All(Items("DROP VIEW |"), i => Assert.Null(i.Tag));
        }

        [Fact]
        public void NoSnippetsOrKeywordsAreMixedIn()
        {
            Assert.All(Items("ALTER PROCEDURE |"), i => Assert.Equal(SuggestionKind.Procedure, i.Kind));
        }

        [Fact]
        public void CreateProcedureOfANewNameKeepsItsOldBehavior()
        {
            Assert.DoesNotContain(Items("CREATE PROCEDURE |"), i => i.DisplayText == "GetUsers");
        }

        [Theory]
        [InlineData("CREATE PROCEDURE dbo.P AS SELECT 1", "ALTER PROCEDURE dbo.P AS SELECT 1")]
        [InlineData("create proc dbo.P as select 1", "alter proc dbo.P as select 1")]
        [InlineData("-- comment\r\n/* c */ CREATE   VIEW v AS SELECT 1", "-- comment\r\n/* c */ ALTER   VIEW v AS SELECT 1")]
        [InlineData("CREATE OR ALTER FUNCTION f() RETURNS int AS BEGIN RETURN 1 END", "ALTER FUNCTION f() RETURNS int AS BEGIN RETURN 1 END")]
        [InlineData("ALTER PROCEDURE p AS SELECT 1", "ALTER PROCEDURE p AS SELECT 1")]
        public void TheDefinitionBecomesAlter(string definition, string expected)
        {
            Assert.Equal(expected, DefinitionScript.ToAlter(definition));
        }

        [Fact]
        public void CreateOrAlterIsKeptOrAdded()
        {
            Assert.Equal("CREATE OR ALTER VIEW v AS SELECT 1", DefinitionScript.ToAlter("CREATE VIEW v AS SELECT 1", true));
            Assert.Equal("CREATE OR ALTER VIEW v AS SELECT 1", DefinitionScript.ToAlter("CREATE OR ALTER VIEW v AS SELECT 1", true));
        }

        [Theory]
        [InlineData("ALTER PROCEDURE p\r\n    @a int\r\nAS\r\nSELECT 1", "ALTER PROCEDURE p\r\n    @a int\r\nAS\r\n")]
        [InlineData("ALTER PROCEDURE p @a AS int AS\nSELECT @a", "ALTER PROCEDURE p @a AS int AS\n")]
        [InlineData("ALTER VIEW v AS SELECT 1", "ALTER VIEW v AS SELECT 1")]
        [InlineData("ALTER TRIGGER t ON dbo.T AFTER INSERT\nAS\n  PRINT 1", "ALTER TRIGGER t ON dbo.T AFTER INSERT\nAS\n")]
        public void TheCaretGoesToTheLineAfterAs(string script, string beforeCaret)
        {
            Assert.Equal(beforeCaret, script.Substring(0, DefinitionScript.BodyStart(script)));
        }
    }
}
