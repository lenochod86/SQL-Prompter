using System.Linq;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Personal and shared snippet folders (#228).</summary>
    public class SnippetMergeTests
    {
        private static Snippet S(string prefix, string body = "x") => Snippet.Create(prefix, "d", body);

        [Fact]
        public void SharedSnippetsAreAddedAndMarked()
        {
            var merged = SnippetMerge.Merge(new[] { S("a") }, new[] { new[] { S("b") } });

            Assert.Equal(new[] { "a", "b" }, merged.Select(s => s.Prefix).ToArray());
            Assert.False(merged[0].IsShared);
            Assert.True(merged[1].IsShared);
        }

        [Fact]
        public void ThePersonalSnippetWinsOverASharedOneWithTheSamePrefix()
        {
            var merged = SnippetMerge.Merge(new[] { S("a", "mine") }, new[] { new[] { S("A", "theirs") } });

            Assert.Equal("mine", Assert.Single(merged).Body);
        }

        [Fact]
        public void AnEarlierSharedFolderWinsOverALaterOne()
        {
            var merged = SnippetMerge.Merge(new Snippet[0], new[] { new[] { S("a", "first") }, new[] { S("a", "second"), S("b") } });

            Assert.Equal(new[] { "first", "x" }, merged.Select(s => s.Body).ToArray());
        }

        [Fact]
        public void TheSharedFolderSettingIsSplitAtSemicolons()
        {
            var settings = new SnippetSettings { SharedFolders = @" C:\a ;; D:\b; " };

            Assert.Equal(new[] { @"C:\a", @"D:\b" }, settings.SharedFolderList().ToArray());
            Assert.Empty(new SnippetSettings().SharedFolderList());
        }

        [Fact]
        public void TheSharedMarkIsNotWrittenToTheFile() => Assert.DoesNotContain("hared", SnippetStore.Write(new Snippet { Prefix = "p", IsShared = true }));
    }
}
