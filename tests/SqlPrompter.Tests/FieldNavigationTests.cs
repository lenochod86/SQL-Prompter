using SqlPrompter.Core.Snippets;
using Xunit;

namespace SqlPrompter.Tests
{
    public class FieldNavigationTests
    {
        [Theory]
        [InlineData(0, 3, 1)]
        [InlineData(1, 3, 2)]
        [InlineData(2, 3, 0)]
        [InlineData(0, 1, 0)]
        public void Tab_cycles_through_the_fields(int current, int count, int expected) =>
            Assert.Equal(expected, FieldNavigation.Cycle(current, count));

        [Theory]
        [InlineData(2, 3, 1)]
        [InlineData(1, 3, 0)]
        [InlineData(0, 3, 2)]
        [InlineData(0, 1, 0)]
        public void ShiftTab_cycles_backwards(int current, int count, int expected) =>
            Assert.Equal(expected, FieldNavigation.CycleBack(current, count));

        [Theory]
        [InlineData(0, 3, 1)]
        [InlineData(1, 3, 2)]
        [InlineData(2, 3, -1)]
        [InlineData(0, 1, -1)]
        public void Enter_moves_on_and_finishes_after_the_last_field(int current, int count, int expected) =>
            Assert.Equal(expected, FieldNavigation.Advance(current, count));

        [Fact]
        public void No_fields_gives_no_field()
        {
            Assert.Equal(-1, FieldNavigation.Cycle(0, 0));
            Assert.Equal(-1, FieldNavigation.CycleBack(0, 0));
        }
    }
}
