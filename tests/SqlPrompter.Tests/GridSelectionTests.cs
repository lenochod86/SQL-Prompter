using System.Collections.Generic;
using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>Copy as IN list needs one column: when the selection has more, the command is off (#446).</summary>
    public class GridSelectionTests
    {
        private static bool Several(params (int, int)[] blocks) => GridSelection.SpansSeveralColumns(blocks);

        [Fact]
        public void OneCellOrOneColumnIsNotSeveral()
        {
            Assert.False(Several((2, 2)));
            Assert.False(Several((0, 0)));
        }

        [Fact]
        public void SeveralRowsOfOneColumnAreOne()
        {
            Assert.False(Several((1, 1), (1, 1), (1, 1)));
        }

        [Fact]
        public void ABlockOfSeveralColumnsIsSeveral()
        {
            Assert.True(Several((0, 3)));
            Assert.True(Several((1, 2)));
        }

        [Fact]
        public void BlocksOfDifferentColumnsAreSeveral()
        {
            Assert.True(Several((0, 0), (2, 2)));
            Assert.True(Several((1, 1), (1, 1), (4, 4)));
        }

        [Fact]
        public void NothingSelectedIsNotSeveral()
        {
            Assert.False(Several());
            Assert.False(GridSelection.SpansSeveralColumns(null));
            Assert.False(GridSelection.SpansSeveralColumns(new List<(int, int)>()));
        }
    }
}
