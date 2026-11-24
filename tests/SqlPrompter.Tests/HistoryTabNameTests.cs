using System;
using System.Collections.Generic;
using SqlPrompter.Core.History;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The tab names of the history and the opening of a tab whose connection does not work (#511).</summary>
    public sealed class HistoryTabNameTests
    {
        [Theory]
        [InlineData("~vsr5khfdjz.sql", "(unnamed query)")]
        [InlineData("~VSGBDFMKTI.SQL", "(unnamed query)")]
        [InlineData("", "(unnamed query)")]
        [InlineData(null, "(unnamed query)")]
        [InlineData("SQLQuery2.sql", "SQLQuery2.sql")]
        [InlineData("Orders check", "Orders check")]
        [InlineData("report.sql", "report.sql")]
        public void RowsShowTheNameOfTheTabNeverTheTemporaryFile(string recorded, string shown)
        {
            Assert.Equal(shown, HistoryTabName.Display(recorded));
        }

        [Theory]
        [InlineData("SQLQuery2.sql", "SQLQuery2.sql")]
        [InlineData("~vs1.sql", null)]
        [InlineData("  ", null)]
        public void AReopenedTabGetsTheRecordedNameWhenThereIsOne(string recorded, string expected)
        {
            Assert.Equal(expected, HistoryTabName.ForReopening(recorded));
        }

        [Fact]
        public void TheNameIsTheCaptionWithoutTheSuffixOfSsms()
        {
            Assert.Equal("SQLQuery2.sql", HistoryTabName.FromCaption("SQLQuery2.sql - localhost,1433.master (claude (55))*", " - localhost,1433.master (claude (55))"));
            Assert.Equal("Orders check", HistoryTabName.FromCaption("Orders check - s.d (u (1))", " - s.d (u (1))"));
            Assert.Null(HistoryTabName.FromCaption("SQLQuery2.sql", " - s.d (u (1))"));
            Assert.Null(HistoryTabName.FromCaption("~vsabc.sql - s.d (u (1))", " - s.d (u (1))"));
            Assert.Null(HistoryTabName.FromCaption(" - s.d (u (1))", " - s.d (u (1))"));
            Assert.Null(HistoryTabName.FromCaption("SQLQuery2.sql - s.d (u (1))", null));
        }

        [Fact]
        public void ANameThatIsFreeIsKept()
        {
            Assert.Equal("SQLQuery2.sql", HistoryTabName.Unique("SQLQuery2.sql", new[] { "SQLQuery1.sql", "SQLQuery3.sql" }));
            Assert.Equal("SQLQuery2.sql", HistoryTabName.Unique("SQLQuery2.sql", null));
        }

        [Fact]
        public void ATakenNameGetsTheLowestFreeNumber()
        {
            Assert.Equal("SQLQuery2 (2).sql", HistoryTabName.Unique("SQLQuery2.sql", new[] { "sqlquery2.sql" }));
            Assert.Equal("SQLQuery2 (3).sql", HistoryTabName.Unique("SQLQuery2.sql", new[] { "SQLQuery2.sql", "SQLQuery2 (2).sql" }));
            Assert.Equal("Orders check (2)", HistoryTabName.Unique("Orders check", new[] { "Orders check" }));
        }

        [Fact]
        public void TheTextIsInsertedWhenTheConnectionSucceeds()
        {
            var inserted = false;
            var outcome = NewTabOpener.Open(() => { }, () => true, () => inserted = true, ex => throw ex);
            Assert.Equal(NewTabOutcome.Filled, outcome);
            Assert.True(inserted);
        }

        [Fact]
        public void TheTextIsInsertedWhenTheConnectionIsCancelledOrThrows()
        {
            // Cancelled: SSMS returns without a connection (no exception).
            var cancelledInserted = false;
            Assert.Equal(NewTabOutcome.Filled, NewTabOpener.Open(() => { }, () => true, () => cancelledInserted = true, null));
            Assert.True(cancelledInserted);

            // Failed: the call that creates and connects the window throws, the window is there.
            var failures = new List<Exception>();
            var thrownInserted = false;
            var outcome = NewTabOpener.Open(() => throw new InvalidOperationException("VPN down"), () => true, () => thrownInserted = true, failures.Add);
            Assert.Equal(NewTabOutcome.Filled, outcome);
            Assert.True(thrownInserted);
            Assert.Single(failures);
        }

        [Fact]
        public void NothingIsInsertedWhenNoNewWindowCameOut()
        {
            var inserted = false;
            Assert.Equal(NewTabOutcome.NoWindow, NewTabOpener.Open(() => throw new InvalidOperationException(), () => false, () => inserted = true, null));
            Assert.False(inserted);
        }

        [Fact]
        public void AWindowThatCannotBeFilledIsReported()
        {
            Assert.Equal(NewTabOutcome.NotFilled, NewTabOpener.Open(() => { }, () => true, () => false, null));
        }
    }
}
