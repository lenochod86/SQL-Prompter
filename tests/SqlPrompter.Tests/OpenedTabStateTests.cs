using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Which opened tabs start as unmodified (#512).</summary>
    public sealed class OpenedTabStateTests
    {
        [Theory]
        [InlineData(OpenedTabKind.Definition, true)]
        [InlineData(OpenedTabKind.Generated, true)]
        [InlineData(OpenedTabKind.UserText, false)]
        public void OnlyTextTheUserDidNotWriteStartsUnmodified(OpenedTabKind kind, bool expected)
        {
            Assert.Equal(expected, OpenedTabState.StartsUnmodified(kind));
        }
    }
}