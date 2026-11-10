using System;
using System.Linq;
using SqlPrompter.Core.Formatting;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>ON on a new line, GROUP BY / ORDER BY on one line and the import report of the options (#305).</summary>
    public class JoinGroupByStyleTests
    {
        private const string Sql = "select a.Id,c.Name from dbo.Accounts a inner join dbo.Cities c on c.Id=a.IdCity and c.Active=1 left join dbo.Regions r on r.Id=c.IdRegion where a.Active=1 group by a.Id,c.Name order by a.Id,c.Name;";

        private static string Format(Action<FormatStyle> change)
        {
            var style = new FormatStyle { UseTabs = false, CollapseStatementsShorterThan = 0 };
            change(style);
            return SqlFormatter.Format(Sql, style).Text.Replace("\r\n", "\n");
        }

        private static string Collapse(string text) => System.Text.RegularExpressions.Regex.Replace(text, " {2,}", " ");

        [Fact]
        public void TheDefaultKeepsOnAfterTheJoinedTable()
        {
            var text = Format(s => { });

            Assert.Contains("INNER JOIN dbo.Cities c ON c.Id = a.IdCity", Collapse(text));
            Assert.DoesNotContain("GROUP BY a.Id, c.Name", Collapse(text));
        }

        [Fact]
        public void OnCanStartALineOfItsOwn()
        {
            var lines = Format(s => s.OnOnNewLine = true).Split('\n');

            var join = Array.FindIndex(lines, l => l.StartsWith("INNER JOIN", StringComparison.Ordinal));
            Assert.EndsWith("dbo.Cities c", lines[join]);
            Assert.Equal("    ON c.Id = a.IdCity", lines[join + 1]);
            Assert.Contains("AND c.Active = 1", lines[join + 2]);
            Assert.StartsWith("LEFT JOIN", lines[join + 3]);
            Assert.Equal("    ON r.Id = c.IdRegion", lines[join + 4]);
        }

        [Fact]
        public void GroupByAndOrderByCanStayOnTheLineOfTheKeyword()
        {
            var text = Format(s => s.GroupOrderBySeparateLines = false);

            Assert.Contains("GROUP BY a.Id, c.Name\n", Collapse(text));
            Assert.Contains("ORDER BY a.Id, c.Name;", Collapse(text));
        }

        [Fact]
        public void TheOptionsAreIndependent()
        {
            var text = Format(s => { s.OnOnNewLine = true; s.GroupOrderBySeparateLines = false; });

            Assert.Contains("    ON c.Id = a.IdCity", text);
            Assert.Contains("a.Id, c.Name\n", Collapse(text));
        }

        [Fact]
        public void TheImportMapsBothOptionsAndReportsTheApproximateOnes()
        {
            var json = @"{ ""lists"": { ""alignItemsToTabStops"": true },
              ""dml"": { ""listItems"": { ""placeGroupByAndOrderByOnNewLine"": ""never"" } },
              ""joinStatements"": { ""on"": { ""placeOnNewLine"": true } },
              ""operators"": { ""andOr"": { ""alignment"": ""beforeFirstListItem"" } } }";

            var result = SqlPromptStyleImporter.Import(json);

            Assert.False(result.Style.GroupOrderBySeparateLines);
            Assert.True(result.Style.OnOnNewLine);
            Assert.Contains("groupOrderBySeparateLines = False", result.Applied);
            Assert.Contains("onOnNewLine = True", result.Applied);
            Assert.Equal(2, result.Approximate.Count);
            Assert.Contains(result.Approximate, a => a.StartsWith("operators.andOr.alignment", StringComparison.Ordinal));
            Assert.Contains(result.Approximate, a => a.StartsWith("lists.alignItemsToTabStops", StringComparison.Ordinal));
            Assert.Empty(result.Notes);
        }

        [Theory]
        [InlineData("ifMultiple", true)]
        [InlineData("always", true)]
        [InlineData("never", false)]
        public void GroupByValuesOtherThanNeverKeepSeparateLines(string value, bool expected)
        {
            var result = SqlPromptStyleImporter.Import(@"{ ""dml"": { ""listItems"": { ""placeGroupByAndOrderByOnNewLine"": """ + value + @""" } } }");

            Assert.Equal(expected, result.Style.GroupOrderBySeparateLines);
        }

        [Fact]
        public void EveryNoteNamesItsReasonOrDecision()
        {
            var json = @"{ ""casing"": { ""useObjectDefinitionCase"": true }, ""cte"": { ""placeColumnsOnNewLine"": true }, ""someNewOption"": 1,
              ""parentheses"": { ""collapseParenthesesShorterThan"": 5 }, ""lists"": { ""alignComments"": true } }";

            var notes = SqlPromptStyleImporter.Import(json).Notes;

            Assert.Contains(notes, n => n.StartsWith("casing.useObjectDefinitionCase: left out", StringComparison.Ordinal));
            Assert.Contains(notes, n => n.StartsWith("cte.placeColumnsOnNewLine: left out", StringComparison.Ordinal));
            Assert.Contains(notes, n => n.StartsWith("parentheses.collapseParenthesesShorterThan: left out", StringComparison.Ordinal));
            Assert.Contains(notes, n => n.StartsWith("lists.alignComments: left out", StringComparison.Ordinal));
            Assert.Contains(notes, n => n == "someNewOption: not supported yet");
            Assert.All(notes, n => Assert.Contains(": ", n));
        }
    }
}
