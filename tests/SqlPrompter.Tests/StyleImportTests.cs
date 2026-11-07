using System;
using SqlPrompter.Core.Formatting;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    public class StyleImportTests
    {
        private const string Sample = @"{
  ""metadata"": { ""id"": ""x"", ""name"": ""Sample"" },
  ""whitespace"": { ""spacesOrTabs"": ""spaces"", ""newLines"": { ""preserveExistingEmptyLinesAfterBatchSeparator"": false } },
  ""lists"": { ""placeCommasBeforeItems"": false, ""alignAliases"": true },
  ""parentheses"": { ""parenthesisStyle"": ""expandedSimple"" },
  ""casing"": { ""reservedKeywords"": ""lowercase"", ""builtInFunctions"": ""uppercase"", ""builtInDataTypes"": ""leaveAsIs"" },
  ""dml"": { ""addNewLineAfterDistinctAndTopClauses"": true, ""collapseStatementsShorterThan"": 40, ""collapseShortSubqueries"": false,
           ""collapseSubqueriesShorterThan"": 99, ""listItems"": { ""placeWhereConditionOnNewLine"": ""never"" } },
  ""ddl"": { ""collapseShortStatements"": true, ""collapseStatementsShorterThan"": 60 }
}";

        [Fact]
        public void KnownOptionsAreMapped()
        {
            var style = SqlPromptStyleImporter.Import(Sample).Style;

            Assert.False(style.UseTabs);
            Assert.False(style.CommaBeforeItems);
            Assert.Equal(CaseStyle.Lower, style.KeywordCase);
            Assert.Equal(CaseStyle.Upper, style.FunctionCase);
            Assert.Equal(CaseStyle.Keep, style.DataTypeCase);
            Assert.True(style.NewLineAfterDistinctAndTop);
            Assert.Equal(40, style.CollapseStatementsShorterThan);
            Assert.Equal(0, style.CollapseSubqueriesShorterThan);
            Assert.Equal(60, style.CollapseDdlShorterThan);
            Assert.False(style.ConditionsOnSeparateLines);
        }

        [Fact]
        public void UnknownOptionsAreReportedAndMissingOnesKeepTheBase()
        {
            var baseStyle = new FormatStyle { TabWidth = 8, MaxLineLength = 90 };
            var result = SqlPromptStyleImporter.Import(Sample, baseStyle);

            Assert.Equal(8, result.Style.TabWidth);
            Assert.Equal(90, result.Style.MaxLineLength);
            Assert.Contains(result.Notes, n => n.StartsWith("parentheses.parenthesisStyle: ", StringComparison.Ordinal));
            Assert.Contains("alignAliases = True", result.Applied);
            Assert.DoesNotContain(result.Notes, n => n.StartsWith("lists.alignAliases", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Notes, n => n.StartsWith("casing.reservedKeywords", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Notes, n => n.StartsWith("metadata", StringComparison.Ordinal));
        }

        [Fact]
        public void ImportedStyleFormatsAScript()
        {
            var style = SqlPromptStyleImporter.Import(Sample).Style;
            var result = SqlFormatter.Format("SELECT Id, Name FROM dbo.Users WHERE Id = 1 AND Name = 'a' ORDER BY Id", style, "\n").Text;

            Assert.Equal("select  Id,\n        Name\nfrom    dbo.Users\nwhere   Id = 1 and Name = 'a'\norder by    Id", result);
        }

        [Fact]
        public void BrokenJsonIsRejected() => Assert.Throws<FormatException>(() => SqlPromptStyleImporter.Import("{ \"a\": "));

        [Fact]
        public void FormattingSettingsSurviveTheSettingsFile()
        {
            var settings = new PrompterSettings();
            settings.Formatting.CommaBeforeItems = false;
            settings.Formatting.KeywordCase = CaseStyle.Lower;
            settings.Formatting.TabWidth = 2;

            var back = SettingsJson.Deserialize(SettingsJson.Serialize(settings)).Formatting;

            Assert.False(back.CommaBeforeItems);
            Assert.Equal(CaseStyle.Lower, back.KeywordCase);
            Assert.Equal(2, back.TabWidth);
            Assert.Equal(75, back.CollapseStatementsShorterThan);
            Assert.True(SettingsJson.Deserialize("{}").Formatting.UseTabs);
        }
    }
}
