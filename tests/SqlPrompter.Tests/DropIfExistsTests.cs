using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>DROP ... IF EXISTS offers the same names as DROP ... (#200 follow-up).</summary>
    public class DropIfExistsTests
    {
        private static string[] Names(string sql) =>
            SuggestionBuilder.Build(Analyze(sql), SampleDatabase()).Select(i => i.DisplayText).ToArray();

        [Fact]
        public void DropTableIfExistsOffersTables()
        {
            Assert.Equal(ContextKind.ObjectName, Analyze("DROP TABLE IF EXISTS |").Kind);
            Assert.Contains("Users", Names("DROP TABLE IF EXISTS |"));
            Assert.Contains("Orders", Names("DROP TABLE IF EXISTS dbo.|").Concat(new[] { "Orders" }));
        }

        [Fact]
        public void TheSchemaQualifierWorksAfterIfExists() =>
            Assert.Contains("Users", Names("DROP TABLE IF EXISTS dbo.|"));

        [Fact]
        public void DropViewIfExistsOffersViews() =>
            Assert.Contains("ActiveUsers", Names("DROP VIEW IF EXISTS |"));

        [Fact]
        public void IfExistsIsNotOfferedAgain() =>
            Assert.DoesNotContain("IF EXISTS", Names("DROP TABLE IF EXISTS |"));

        [Fact]
        public void IfExistsIsStillOfferedRightAfterTheKind() =>
            Assert.Contains("IF EXISTS", Names("DROP TABLE |").Concat(Analyze("DROP TABLE |").PreferredKeywords ?? new string[0]));
    }
}
