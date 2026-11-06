using System.Linq;
using SqlPrompter.Core.Completion;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class NoMetadataTests
    {
        [Theory]
        [InlineData("SELECT * FROM dbo.Users u WHERE ty|")]
        [InlineData("SELECT |")]
        [InlineData("SELECT a, |")]
        public void ExpressionsWithoutLoadedMetadataStillOfferKeywordsAndFunctions(string sql)
        {
            var items = SuggestionBuilder.Build(Analyze(sql), null, null, SuggestionOptions.Default);

            Assert.NotEmpty(items);
        }
    }
}
