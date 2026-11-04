using System;
using System.Linq;
using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    public class SnippetExpanderTests
    {
        private static Snippet Make(string body, params (string Name, string Default)[] fields) =>
            Snippet.Create("x", "", body, fields.Select(f => new SnippetPlaceholder { Name = f.Name, DefaultValue = f.Default }));

        private static readonly SnippetContext Plain = new SnippetContext { NewLine = "\n" };

        [Fact]
        public void CursorMarkerIsRemovedAndRemembered()
        {
            var result = SnippetExpander.Expand(Make("SELECT * FROM $CURSOR$ WHERE 1 = 1"), Plain);

            Assert.Equal("SELECT * FROM  WHERE 1 = 1", result.Text);
            Assert.Equal("SELECT * FROM ".Length, result.CursorOffset);
        }

        [Fact]
        public void WithoutACursorTheCaretEndsAtTheEnd()
        {
            var result = SnippetExpander.Expand(Make("SELECT 1"), Plain);

            Assert.Equal(8, result.CursorOffset);
            Assert.Equal(-1, result.SelectionStart);
        }

        [Fact]
        public void SelectedTextIsInserted()
        {
            var result = SnippetExpander.Expand(Make("ISNULL($SELECTEDTEXT$, 0)$CURSOR$"), new SnippetContext { SelectedText = "a.Amount", NewLine = "\n" });

            Assert.Equal("ISNULL(a.Amount, 0)", result.Text);
            Assert.Equal(result.Text.Length, result.CursorOffset);
        }

        [Fact]
        public void SelectionMarkersGiveTheRangeToSelect()
        {
            var result = SnippetExpander.Expand(Make("BEGIN TRY\n$SELECTIONSTART$$SELECTEDTEXT$$SELECTIONEND$\nEND TRY"), new SnippetContext { SelectedText = "SELECT 1", NewLine = "\n" });

            Assert.Equal("SELECT 1", result.Text.Substring(result.SelectionStart, result.SelectionEnd - result.SelectionStart));
        }

        [Fact]
        public void UserDatabaseAndDate()
        {
            var context = new SnippetContext { User = "jane", DatabaseName = "Sales", Now = new DateTime(2026, 11, 4, 13, 5, 0), NewLine = "\n" };

            var result = SnippetExpander.Expand(Make("-- $USER$ on $DBNAME$ at $DATE(yyyy-MM-dd HH:mm)$"), context);

            Assert.Equal("-- jane on Sales at 2026-11-04 13:05", result.Text);
        }

        [Fact]
        public void BuiltInNamesAreNotCaseSensitive()
        {
            var result = SnippetExpander.Expand(Make("$cursor$$User$"), new SnippetContext { User = "u", NewLine = "\n" });

            Assert.Equal("u", result.Text);
            Assert.Equal(0, result.CursorOffset);
        }

        [Fact]
        public void CustomFieldsGetTheirDefaultValuesAndAllPositions()
        {
            var result = SnippetExpander.Expand(Make("ALTER TABLE $schema$.$table$ ADD $col$ INT; -- $table$", ("schema", "[dbo]"), ("table", "[t]")), Plain);

            Assert.Equal("ALTER TABLE [dbo].[t] ADD col INT; -- [t]", result.Text);
            Assert.Equal(new[] { "schema", "table", "col" }, result.Fields.Select(f => f.Name).ToArray());
            var table = result.Fields.Single(f => f.Name == "table");
            Assert.Equal(2, table.Offsets.Count);
            Assert.All(table.Offsets, o => Assert.Equal("[t]", result.Text.Substring(o, table.Length)));
            Assert.Equal("col", result.Fields.Single(f => f.Name == "col").DefaultValue);
        }

        [Fact]
        public void FieldDeclaredWithAnEmptyDefaultShowsItsName()
        {
            var result = SnippetExpander.Expand(Make("UPDATE $table$ SET $col$ = $val$", ("table", ""), ("col", null), ("val", "0")), Plain);

            Assert.Equal("UPDATE table SET col = 0", result.Text);
            Assert.Equal("table", result.Fields.Single(f => f.Name == "table").DefaultValue);
            Assert.Equal("col", result.Fields.Single(f => f.Name == "col").DefaultValue);
        }

        [Fact]
        public void FieldPositionsAreRightAfterBuiltInsThatChangeTheLength()
        {
            var result = SnippetExpander.Expand(Make("$DATE(yyyy)$ $f$ $USER$ $f$", ("f", "ab")), new SnippetContext { Now = new DateTime(2026, 1, 1), User = "joe", NewLine = "\n" });

            var f = result.Fields.Single();
            Assert.Equal("2026 ab joe ab", result.Text);
            Assert.All(f.Offsets, o => Assert.Equal("ab", result.Text.Substring(o, 2)));
        }

        [Fact]
        public void TwoDollarsAreOneDollarAndAloneDollarsStay()
        {
            var result = SnippetExpander.Expand(Make("SELECT $$5, $action, $x y"), Plain);

            Assert.Equal("SELECT $5, $action, $x y", result.Text);
            Assert.Empty(result.Fields);
        }

        [Fact]
        public void LinesAfterTheFirstAreIndentedAndLineEndingsFollowTheDocument()
        {
            var snippet = Make("IF 1 = 1\r\nBEGIN\r\n\tSELECT 1\r\n\r\nEND");

            var result = SnippetExpander.Expand(snippet, new SnippetContext { NewLine = "\r\n", Indent = "    " });

            Assert.Equal("IF 1 = 1\r\n    BEGIN\r\n    \tSELECT 1\r\n\r\n    END", result.Text);
        }

        [Fact]
        public void IndentationDoesNotMoveTheCursorOrFieldsOutOfPlace()
        {
            var result = SnippetExpander.Expand(Make("a\n$f$\n$CURSOR$", ("f", "val")), new SnippetContext { NewLine = "\n", Indent = "  " });

            Assert.Equal("a\n  val\n  ", result.Text);
            Assert.Equal(result.Text.Length, result.CursorOffset);
            Assert.Equal("val", result.Text.Substring(result.Fields.Single().Offsets[0], 3));
        }

        [Fact]
        public void TheSampleFromTheUsersExportHasItsFields()
        {
            var snippet = Make(
                "IF NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.name = '$column_name$')\nALTER TABLE $schema_name$.$table_name$ ADD [$column_name$] $data_type$ NULL\nGO\n\n$CURSOR$",
                ("schema_name", "[schema_name]"), ("table_name", "[table_name]"), ("column_name", ""), ("data_type", "INT"));

            var result = SnippetExpander.Expand(snippet, Plain);

            Assert.Equal(new[] { "column_name", "schema_name", "table_name", "data_type" }, result.Fields.Select(f => f.Name).ToArray());
            Assert.Equal(2, result.Fields[0].Offsets.Count);
            Assert.Equal("column_name".Length, result.Fields[0].Length);
            Assert.EndsWith("\nGO\n\n", result.Text);
            Assert.Equal(result.Text.Length, result.CursorOffset);
        }
    }
}
