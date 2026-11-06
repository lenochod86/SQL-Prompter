using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SqlPrompter.Core.Completion;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Loose matching of names: CamelCase humps, words, characters in order (#219).</summary>
    public class NameMatcherTests
    {
        [Theory]
        [InlineData("BurzaKlientTyp", "burzaklienttyp", MatchKind.Exact)]
        [InlineData("BurzaKlientTyp", "Burza", MatchKind.Prefix)]
        [InlineData("BurzaKlientTyp", "BKT", MatchKind.Words)]
        [InlineData("BurzaKlientTyp", "bkt", MatchKind.Words)]
        [InlineData("BurzaKlientTyp", "BuKl", MatchKind.Words)]
        [InlineData("BurzaKlientTyp", "KT", MatchKind.Words)]
        [InlineData("BurzaKlientTyp", "Klient", MatchKind.Words)]
        [InlineData("Burza_Klient_Typ", "kt", MatchKind.Words)]
        [InlineData("Burza_Klient_Typ", "bkt", MatchKind.Words)]
        [InlineData("dbo.Users", "us", MatchKind.Words)]
        [InlineData("XMLParser", "xp", MatchKind.Words)]
        [InlineData("BurzaKlientTyp", "lientT", MatchKind.Substring)]
        [InlineData("BurzaKlientTyp", "bzkl", MatchKind.Subsequence)]
        [InlineData("BurzaKlientTyp", "bztp", MatchKind.Subsequence)]
        public void TheKindOfTheMatch(string name, string typed, MatchKind expected)
        {
            Assert.Equal(expected, NameMatcher.Match(name, typed).Kind);
        }

        [Theory]
        [InlineData("BurzaKlientTyp", "xyz")]
        [InlineData("BurzaKlientTyp", "tkb")]
        [InlineData("Users", "userss")]
        public void NoMatch(string name, string typed)
        {
            Assert.Null(NameMatcher.Match(name, typed));
        }

        [Fact]
        public void TheMatchedCharactersAreTheOnesToHighlight()
        {
            Assert.Equal(new[] { 0, 5, 11 }, NameMatcher.Match("BurzaKlientTyp", "BKT").Positions);
            Assert.Equal(new[] { 0, 1, 5, 6 }, NameMatcher.Match("BurzaKlientTyp", "BuKl").Positions);
            Assert.Equal(new[] { 0, 6, 13 }, NameMatcher.Match("Burza_Klient_Typ", "bkt").Positions);
            Assert.Equal(new[] { 0, 3, 5, 6 }, NameMatcher.Match("BurzaKlientTyp", "bzkl").Positions);
        }

        [Fact]
        public void RunsJoinNeighbouringCharacters()
        {
            var runs = NameMatcher.Match("BurzaKlientTyp", "BuKl").Runs();

            Assert.Equal(new[] { new KeyValuePair<int, int>(0, 2), new KeyValuePair<int, int>(5, 2) }, runs);
        }

        [Fact]
        public void WithoutLooseMatchingOnlyPrefixAndSubstringMatch()
        {
            Assert.Null(NameMatcher.Match("BurzaKlientTyp", "BKT", loose: false));
            Assert.Null(NameMatcher.Match("BurzaKlientTyp", "bzkl", loose: false));
            Assert.Equal(MatchKind.Substring, NameMatcher.Match("BurzaKlientTyp", "lient", loose: false).Kind);
            Assert.Equal(MatchKind.Prefix, NameMatcher.Match("BurzaKlientTyp", "bur", loose: false).Kind);
        }

        [Fact]
        public void TheInitialsListTheNameBeforeNamesThatMerelyContainTheLetters()
        {
            var names = new[] { "xbxkxtx", "Bukett", "Backup", "BurzaKlientTyp" };

            var found = ListOrdering.Filter(names, "BKT", n => n);

            Assert.Equal("BurzaKlientTyp", found[0]);
            Assert.Equal(new[] { "BurzaKlientTyp", "xbxkxtx", "Bukett" }, found.ToArray());
        }

        [Fact]
        public void TheRankingIsExactPrefixWordsSubstringSubsequence()
        {
            var names = new[] { "xbkx", "tbkt", "BKT", "bktTable", "BurzaKlientTyp", "SubBktThing" };

            var found = ListOrdering.Filter(names, "bkt", n => n);

            Assert.Equal(new[] { "BKT", "bktTable", "BurzaKlientTyp", "SubBktThing", "tbkt" }, found.ToArray());
        }

        [Fact]
        public void AMatchWithinAKindKeepsTheOrderOfTheInput()
        {
            var found = ListOrdering.Filter(new[] { "UserTwo", "UserOne", "UserAll" }, "user", n => n);

            Assert.Equal(new[] { "UserTwo", "UserOne", "UserAll" }, found.ToArray());
        }

        [Fact]
        public void FilteringALargeListIsFast()
        {
            var words = new[] { "Burza", "Klient", "Typ", "Order", "Item", "Customer", "Address", "Invoice", "Payment", "Detail" };
            var names = new List<string>();
            for (var i = 0; i < 50000; i++)
            {
                names.Add(words[i % 10] + words[(i / 10) % 10] + words[(i / 100) % 10] + i);
            }

            ListOrdering.Filter(names, "bkt", n => n);
            var watch = Stopwatch.StartNew();
            var found = ListOrdering.Filter(names, "bkt", n => n);
            watch.Stop();

            Assert.NotEmpty(found);
            Assert.True(watch.ElapsedMilliseconds < 500, "filtering took " + watch.ElapsedMilliseconds + " ms");
        }
    }
}
