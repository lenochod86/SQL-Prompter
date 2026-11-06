using System.Linq;
using SqlPrompter.Core.Navigation;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    /// <summary>The search of the quick open window (#223).</summary>
    public class ObjectFinderTests
    {
        private static string[] Names(string typed) => ObjectFinder.Search(SampleDatabase(false), typed).Select(f => f.FullName).ToArray();

        [Fact]
        public void EverythingIsListedWithoutTextExceptSystemObjects() =>
            Assert.Equal(5, Names(string.Empty).Length);

        [Fact]
        public void AnExactNameComesBeforeAPrefixAndAnInitialsMatch()
        {
            Assert.Equal("dbo.Users", Names("users")[0]);
            Assert.Equal("dbo.ActiveUsers", Names("users")[1]);
        }

        [Fact]
        public void InitialsFindTheWordsOfAName() =>
            Assert.Contains("dbo.ActiveUsers", Names("AU"));

        [Fact]
        public void TheKindIsReported()
        {
            var view = ObjectFinder.Search(SampleDatabase(false), "ActiveUsers").First();

            Assert.Equal("View", view.Kind);
            Assert.Equal("Table", ObjectFinder.Search(SampleDatabase(false), "Orders").First().Kind);
        }

        [Fact]
        public void ASchemaInTheTextFiltersByTheFullName()
        {
            Assert.Equal(new[] { "sales.Customers" }, Names("sales.cust"));
            Assert.Empty(Names("sys.objects"));
        }

        [Fact]
        public void NothingMatchesGivesAnEmptyListAndNoMetadataToo()
        {
            Assert.Empty(Names("zzzz"));
            Assert.Empty(ObjectFinder.Search(null, "a"));
        }

        [Fact]
        public void TheListIsLimited() =>
            Assert.Single(ObjectFinder.Search(SampleDatabase(false), string.Empty, 1));
    }
}
