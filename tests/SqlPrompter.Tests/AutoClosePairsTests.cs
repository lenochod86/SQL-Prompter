using SqlPrompter.Core.Editing;
using SqlPrompter.Core.Settings;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The pairs of the auto-closing can be switched one by one, and the block comment is a pair of its own (#440).</summary>
    public class AutoClosePairsTests
    {
        // The caret is marked with |; the decision is made for the text without the mark.
        private static AutoCloseAction Decide(string marked, char typed, AutoClosePairs enabled = AutoClosePairs.All)
        {
            var caret = marked.IndexOf('|');
            return AutoClose.Decide(marked.Remove(caret, 1), caret, typed, enabled);
        }

        private static string Deletion(string marked, AutoClosePairs enabled = AutoClosePairs.All)
        {
            var caret = marked.IndexOf('|');
            var text = marked.Remove(caret, 1);
            return AutoClose.TryGetPairDeletion(text, caret, enabled, out var start, out var length) ? text.Substring(start, length) : null;
        }

        [Theory]
        [InlineData("SELECT |", '\'', AutoClosePairs.Quote)]
        [InlineData("SELECT |", '(', AutoClosePairs.Parenthesis)]
        [InlineData("SELECT |", '[', AutoClosePairs.Bracket)]
        [InlineData("SELECT /|", '*', AutoClosePairs.BlockComment)]
        public void AnEnabledPairIsClosed(string marked, char typed, AutoClosePairs pair)
        {
            Assert.Equal(AutoCloseAction.InsertPair, Decide(marked, typed, pair));
        }

        [Theory]
        [InlineData("SELECT |", '\'', AutoClosePairs.Parenthesis | AutoClosePairs.Bracket | AutoClosePairs.BlockComment)]
        [InlineData("SELECT |", '(', AutoClosePairs.Quote | AutoClosePairs.Bracket | AutoClosePairs.BlockComment)]
        [InlineData("SELECT |", '[', AutoClosePairs.Quote | AutoClosePairs.Parenthesis | AutoClosePairs.BlockComment)]
        [InlineData("SELECT /|", '*', AutoClosePairs.Quote | AutoClosePairs.Parenthesis | AutoClosePairs.Bracket)]
        [InlineData("SELECT |", '(', AutoClosePairs.None)]
        public void ADisabledPairDoesNothing(string marked, char typed, AutoClosePairs enabled)
        {
            Assert.Equal(AutoCloseAction.None, Decide(marked, typed, enabled));
        }

        [Theory]
        [InlineData("SELECT (1|)", ')', AutoClosePairs.Parenthesis, AutoCloseAction.Overtype)]
        [InlineData("SELECT (1|)", ')', AutoClosePairs.Quote | AutoClosePairs.Bracket | AutoClosePairs.BlockComment, AutoCloseAction.None)]
        [InlineData("SELECT [a|]", ']', AutoClosePairs.Bracket, AutoCloseAction.Overtype)]
        [InlineData("SELECT [a|]", ']', AutoClosePairs.Quote | AutoClosePairs.Parenthesis | AutoClosePairs.BlockComment, AutoCloseAction.None)]
        [InlineData("SELECT 'a|'", '\'', AutoClosePairs.Quote, AutoCloseAction.Overtype)]
        [InlineData("SELECT 'a|'", '\'', AutoClosePairs.Parenthesis | AutoClosePairs.Bracket | AutoClosePairs.BlockComment, AutoCloseAction.None)]
        public void StepOverFollowsTheSwitchOfItsPair(string marked, char typed, AutoClosePairs enabled, AutoCloseAction expected)
        {
            Assert.Equal(expected, Decide(marked, typed, enabled));
        }

        [Theory]
        [InlineData("SELECT /|", '*', AutoCloseAction.InsertPair)]
        [InlineData("/|", '*', AutoCloseAction.InsertPair)]
        [InlineData("SELECT 1 /|", '*', AutoCloseAction.InsertPair)]
        [InlineData("SELECT *|", '*', AutoCloseAction.None)]
        [InlineData("SELECT 'a /|'", '*', AutoCloseAction.None)]
        [InlineData("SELECT [a /|]", '*', AutoCloseAction.None)]
        [InlineData("-- a /|", '*', AutoCloseAction.None)]
        [InlineData("/* a */|", '*', AutoCloseAction.None)]
        [InlineData("/* a /|", '*', AutoCloseAction.None)]
        public void TypingSlashStarClosesTheCommentOutsideStringsAndComments(string marked, char typed, AutoCloseAction expected)
        {
            Assert.Equal(expected, Decide(marked, typed));
        }

        [Theory]
        [InlineData("/* text| */", '*', AutoCloseAction.OvertypeCommentStar)]
        [InlineData("/* text|*/", '*', AutoCloseAction.OvertypeCommentStar)]
        [InlineData("/* text*|/", '/', AutoCloseAction.Overtype)]
        [InlineData("/* text| */ SELECT 1", '*', AutoCloseAction.OvertypeCommentStar)]
        [InlineData("SELECT 1 * |2", '*', AutoCloseAction.None)]
        [InlineData("/* open| text", '*', AutoCloseAction.None)]
        [InlineData("SELECT 1 /|2", '/', AutoCloseAction.None)]
        public void TypingTheEndOfTheCommentStepsOverTheInsertedClosing(string marked, char typed, AutoCloseAction expected)
        {
            Assert.Equal(expected, Decide(marked, typed));
        }

        [Fact]
        public void ATypedSlashStarDoesNotAddASecondClosing()
        {
            // The comment already has its end: only the opening is typed.
            Assert.Equal(AutoCloseAction.None, Decide("SELECT /| */", '*'));
            Assert.Equal(AutoCloseAction.None, Decide("SELECT /|*/", '*'));
        }

        [Theory]
        [InlineData("/*| */", "/* */")]
        [InlineData("SELECT /*| */", "/* */")]
        [InlineData("a (|)", "()")]
        [InlineData("a [|]", "[]")]
        [InlineData("a '|'", "''")]
        [InlineData("/*| text */", null)]
        [InlineData("/* x|*/", null)]
        [InlineData("/*|*/", null)]
        [InlineData("a (|x)", null)]
        public void BackspaceRemovesAnUntouchedEmptyPair(string marked, string expected)
        {
            Assert.Equal(expected, Deletion(marked));
        }

        [Theory]
        [InlineData("/*| */", AutoClosePairs.Quote | AutoClosePairs.Parenthesis | AutoClosePairs.Bracket)]
        [InlineData("a (|)", AutoClosePairs.Quote | AutoClosePairs.Bracket | AutoClosePairs.BlockComment)]
        [InlineData("a [|]", AutoClosePairs.Quote | AutoClosePairs.Parenthesis | AutoClosePairs.BlockComment)]
        [InlineData("a '|'", AutoClosePairs.Parenthesis | AutoClosePairs.Bracket | AutoClosePairs.BlockComment)]
        public void BackspaceKeepsThePairOfADisabledSwitch(string marked, AutoClosePairs enabled)
        {
            Assert.Null(Deletion(marked, enabled));
        }

        [Fact]
        public void TheBlockCommentIsNotClosedInsideAString()
        {
            Assert.Equal(AutoCloseAction.None, Decide("SELECT '/|'", '*'));
        }

        [Fact]
        public void AllPairsAreOnByDefault()
        {
            var behavior = new BehaviorSettings();

            Assert.Equal(AutoClosePairs.All, behavior.AutoClosePairs);
        }

        [Fact]
        public void TheOldSingleSwitchOnMapsToTheThreeOldPairsAndLeavesTheCommentOn()
        {
            var settings = SettingsJson.Deserialize("{ \"behavior\": { \"autoClose\": true } }");

            Assert.Equal(AutoClosePairs.All, settings.Behavior.AutoClosePairs);
        }

        [Fact]
        public void TheOldSingleSwitchOffTurnsTheThreeOldPairsOffAndLeavesTheCommentOn()
        {
            var settings = SettingsJson.Deserialize("{ \"behavior\": { \"autoClose\": false } }");

            Assert.False(settings.Behavior.AutoCloseQuote);
            Assert.False(settings.Behavior.AutoCloseParenthesis);
            Assert.False(settings.Behavior.AutoCloseBracket);
            Assert.True(settings.Behavior.AutoCloseBlockComment);
        }

        [Fact]
        public void AFileWithoutAnyOfThemBehavesLikeBefore()
        {
            var settings = SettingsJson.Deserialize("{ \"behavior\": { \"commitOnDot\": true } }");

            Assert.Equal(AutoClosePairs.All, settings.Behavior.AutoClosePairs);
        }

        [Fact]
        public void ThePairsAreWrittenAndTheOldValueIsNot()
        {
            var settings = new PrompterSettings();
            settings.Behavior.AutoCloseBracket = false;
            settings.Behavior.AutoCloseBlockComment = false;

            var json = SettingsJson.Serialize(settings);

            Assert.DoesNotContain("\"autoClose\"", json);
            Assert.Contains("\"autoCloseBracket\":false", json.Replace(" ", string.Empty));
            var back = SettingsJson.Deserialize(json);
            Assert.Equal(AutoClosePairs.Quote | AutoClosePairs.Parenthesis, back.Behavior.AutoClosePairs);
        }

        [Fact]
        public void APairInTheFileWinsOverTheOldValueAndSurvivesClone()
        {
            var settings = SettingsJson.Deserialize("{ \"behavior\": { \"autoClose\": false, \"autoCloseQuote\": true } }");

            Assert.True(settings.Behavior.AutoCloseQuote);
            Assert.False(settings.Behavior.AutoCloseParenthesis);
            Assert.Equal(settings.Behavior.AutoClosePairs, settings.Clone().Behavior.AutoClosePairs);
        }
    }
}
