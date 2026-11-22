using System.Linq;
using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class CommandAvailabilityTests
    {
        private static CommandSituation Editor(bool selection = false, bool connected = true) =>
            new CommandSituation { Editor = true, QueryTab = true, Selection = selection, Connection = connected, ClosedTab = false, GridSelection = false };

        private static CommandSituation NoEditor() =>
            new CommandSituation { Editor = false, QueryTab = false, Selection = false, Connection = false, ClosedTab = false, GridSelection = false };

        [Fact]
        public void NothingKnownEnablesEveryCommand()
        {
            foreach (var command in CommandAvailability.Commands)
            {
                Assert.True(CommandAvailability.IsEnabled(command, CommandSituation.Unknown), command.ToString());
            }
        }

        [Fact]
        public void WithoutAnEditorTheRunAndRefactorCommandsAreDisabled()
        {
            var situation = NoEditor();
            foreach (var command in new[]
            {
                MenuCommandKind.RunStatement, MenuCommandKind.RunStatementsUpToCaret, MenuCommandKind.DryRun, MenuCommandKind.FormatDocument, MenuCommandKind.QualifyNames,
                MenuCommandKind.ExpandStars, MenuCommandKind.RemoveBrackets, MenuCommandKind.InsertSemicolons, MenuCommandKind.RemoveUnusedVariables, MenuCommandKind.FixCodeIssues,
                MenuCommandKind.AddAs, MenuCommandKind.RemoveAs, MenuCommandKind.AddNPrefix, MenuCommandKind.ScriptSummary, MenuCommandKind.ParameterInfo,
                MenuCommandKind.ShowSuggestions, MenuCommandKind.GoToDefinition, MenuCommandKind.PreviewAsSelect, MenuCommandKind.RenameQueryTab,
            })
            {
                Assert.False(CommandAvailability.IsEnabled(command, situation), command.ToString());
            }
        }

        [Fact]
        public void WithAnEditorTheyAreEnabled()
        {
            var situation = Editor();
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.RunStatement, situation));
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.RenameQueryTab, situation));
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.FormatSelection, situation));
        }

        [Fact]
        public void TheSelectionCommandsNeedASelection()
        {
            foreach (var command in new[]
            {
                MenuCommandKind.UnformatSelection, MenuCommandKind.AddCommas, MenuCommandKind.AddQuotesAndCommas, MenuCommandKind.NewSnippetFromSelection, MenuCommandKind.SurroundWithSnippet,
            })
            {
                Assert.False(CommandAvailability.IsEnabled(command, Editor(selection: false)), command.ToString());
                Assert.True(CommandAvailability.IsEnabled(command, Editor(selection: true)), command.ToString());
            }
        }

        [Fact]
        public void FormatSelectionWorksWithoutASelection()
        {
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.FormatSelection, Editor(selection: false)));
        }

        [Fact]
        public void TheDatabaseCommandsNeedAConnection()
        {
            foreach (var command in new[] { MenuCommandKind.QuickOpen, MenuCommandKind.FindInCode, MenuCommandKind.ShowDependencies, MenuCommandKind.LocateInObjectExplorer, MenuCommandKind.RefreshMetadata })
            {
                Assert.False(CommandAvailability.IsEnabled(command, Editor(connected: false)), command.ToString());
                Assert.True(CommandAvailability.IsEnabled(command, Editor(connected: true)), command.ToString());
            }
        }

        [Fact]
        public void AnUnknownConnectionLeavesTheDatabaseCommandsEnabled()
        {
            var situation = Editor();
            situation.Connection = null;
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.QuickOpen, situation));
        }

        [Fact]
        public void ReopenNeedsAKnownClosedTab()
        {
            var situation = Editor();
            Assert.False(CommandAvailability.IsEnabled(MenuCommandKind.ReopenClosedTab, situation));
            situation.ClosedTab = true;
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.ReopenClosedTab, situation));
            situation.ClosedTab = null;
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.ReopenClosedTab, situation));
        }

        [Fact]
        public void GridCommandsNeedAGridSelectionAndInListOnlyOneColumn()
        {
            var situation = Editor();
            Assert.False(CommandAvailability.IsEnabled(MenuCommandKind.CopyGridAs, situation));
            Assert.False(CommandAvailability.IsEnabled(MenuCommandKind.ScriptGridAsTempTable, situation));
            situation.GridSelection = true;
            situation.GridOneColumn = true;
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.CopyGridAs, situation));
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.CopyGridAsInList, situation));
            situation.GridOneColumn = false;
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.CopyGridAs, situation));
            Assert.True(CommandAvailability.IsEnabled(MenuCommandKind.OpenGridInExcel, situation));
            Assert.False(CommandAvailability.IsEnabled(MenuCommandKind.CopyGridAsInList, situation));
        }

        [Fact]
        public void GridCommandsAreDisabledWithoutAQueryTab()
        {
            var situation = NoEditor();
            situation.GridSelection = true;
            Assert.False(CommandAvailability.IsEnabled(MenuCommandKind.CopyGridAs, situation));
            Assert.False(CommandAvailability.IsEnabled(MenuCommandKind.OpenGridInExcel, situation));
        }

        [Fact]
        public void TheQuickActionsThatNeedASelectionAgreeWithTheTable()
        {
            var without = QuickActionCatalog.Actions(false).Select(a => a.Id).ToList();
            var with = QuickActionCatalog.Actions(true).Select(a => a.Id).ToList();
            var selectionOnly = with.Except(without).ToList();
            Assert.Contains(QuickActionCatalog.Unformat, selectionOnly);
            Assert.Contains(QuickActionCatalog.AddCommas, selectionOnly);
            Assert.Contains(QuickActionCatalog.AddQuotesAndCommas, selectionOnly);
            Assert.Equal(3, selectionOnly.Count);
            Assert.True(CommandAvailability.NeedsOf(MenuCommandKind.UnformatSelection).HasFlag(CommandNeeds.Selection));
            Assert.True(CommandAvailability.NeedsOf(MenuCommandKind.AddCommas).HasFlag(CommandNeeds.Selection));
            Assert.True(CommandAvailability.NeedsOf(MenuCommandKind.AddQuotesAndCommas).HasFlag(CommandNeeds.Selection));
        }
    }
}
