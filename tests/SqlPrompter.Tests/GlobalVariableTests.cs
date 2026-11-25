using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Core.Sql;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The global variables are offered where a variable can stand (#546).</summary>
    public class GlobalVariableTests
    {
        private static Suggestion[] Suggestions(string sql)
        {
            var caret = sql.IndexOf('|');
            return SuggestionBuilder.Build(Analyze(sql), null, ScriptModel.Build(sql.Remove(caret, 1), caret)).ToArray();
        }

        [Theory]
        [InlineData("SELECT @@|")]
        [InlineData("SELECT @@VER|")]
        [InlineData("SELECT 1, @|")]
        [InlineData("SELECT a FROM t WHERE a = @@|")]
        [InlineData("DECLARE @a int; SET @a = @@|")]
        [InlineData("IF @@|")]
        [InlineData("SELECT @@VER| AS v")]
        public void GlobalVariablesAreOffered(string sql)
        {
            Assert.Equal(ContextKind.Variable, Analyze(sql).Kind);
            Assert.Contains(Suggestions(sql), s => s.DisplayText == "@@VERSION");
        }

        [Fact]
        public void TheTypedWordIsReplacedWhole()
        {
            var context = Analyze("SELECT @@VER|");

            Assert.Equal(7, context.ReplaceStart);
            Assert.Equal("@@VER", context.TypedPrefix);
        }

        [Fact]
        public void TheCaseFollowsTheTypedText()
        {
            Assert.Contains(Suggestions("SELECT @@ver|"), s => s.DisplayText == "@@version");
        }

        [Fact]
        public void TheVariablesOfTheScriptComeFirst()
        {
            var items = Suggestions("DECLARE @a int; SELECT @|");

            Assert.True(string.CompareOrdinal(items.Single(s => s.DisplayText == "@a").SortText, items.Single(s => s.DisplayText == "@@VERSION").SortText) < 0);
        }

        [Fact]
        public void EveryGlobalVariableHasATypeAndAMeaning()
        {
            Assert.All(GlobalVariables.All, v => Assert.True(v.Name.StartsWith("@@") && v.TypeName.Length > 0 && v.Meaning.Length > 0));
            Assert.True(ContextKeywords.IsGlobalFunction("@@rowcount"));
        }
    }
}
