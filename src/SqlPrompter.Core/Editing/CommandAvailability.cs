using System;
using System.Collections.Generic;

namespace SqlPrompter.Core.Editing
{
    /// <summary>The commands of the SQL Prompter menus whose state depends on the situation (#503). The commands that are always available are not listed.</summary>
    public enum MenuCommandKind
    {
        RunStatement,
        RunStatementsUpToCaret,
        DryRun,
        PreviewAsSelect,
        FormatDocument,
        FormatSelection,
        UnformatSelection,
        QualifyNames,
        ExpandStars,
        RemoveBrackets,
        InsertSemicolons,
        RemoveUnusedVariables,
        FixCodeIssues,
        AddAs,
        RemoveAs,
        AddNPrefix,
        ScriptSummary,
        ParameterInfo,
        ShowSuggestions,
        GoToDefinition,
        AddCommas,
        AddQuotesAndCommas,
        NewSnippetFromSelection,
        SurroundWithSnippet,
        QuickOpen,
        FindInCode,
        ShowDependencies,
        LocateInObjectExplorer,
        RefreshMetadata,
        RenameQueryTab,
        ReopenClosedTab,
        ScriptGridAsTempTable,
        OpenGridInExcel,
        CopyGridAs,
        CopyGridAsInList,
    }

    /// <summary>What a command needs. A command needs all the flags it has.</summary>
    [Flags]
    public enum CommandNeeds
    {
        None = 0,

        /// <summary>The active window is an editable SQL query editor.</summary>
        Editor = 1,

        /// <summary>The editor has a non-empty selection.</summary>
        Selection = 2,

        /// <summary>The window is connected to a server.</summary>
        Connection = 4,

        /// <summary>There is a query tab (also when its results grid has the focus).</summary>
        QueryTab = 8,

        /// <summary>The results grid has a selection.</summary>
        GridSelection = 16,

        /// <summary>The grid selection covers one column only.</summary>
        GridOneColumn = 32,

        /// <summary>A closed query tab is known.</summary>
        ClosedTab = 64,
    }

    /// <summary>
    /// What the SSMS layer knows about the situation when a menu opens. Every fact is three-valued: <c>null</c> means "not known" (the layer could not tell cheaply,
    /// the metadata is still loading, ...) and never disables a command (#503).
    /// </summary>
    public sealed class CommandSituation
    {
        public bool? Editor { get; set; }

        public bool? Selection { get; set; }

        public bool? Connection { get; set; }

        public bool? QueryTab { get; set; }

        public bool? GridSelection { get; set; }

        public bool? GridOneColumn { get; set; }

        public bool? ClosedTab { get; set; }

        /// <summary>A situation in which nothing is known: everything is enabled.</summary>
        public static CommandSituation Unknown => new CommandSituation();

        internal bool? Fact(CommandNeeds need)
        {
            switch (need)
            {
                case CommandNeeds.Editor: return Editor;
                case CommandNeeds.Selection: return Selection;
                case CommandNeeds.Connection: return Connection;
                case CommandNeeds.QueryTab: return QueryTab;
                case CommandNeeds.GridSelection: return GridSelection;
                case CommandNeeds.GridOneColumn: return GridOneColumn;
                case CommandNeeds.ClosedTab: return ClosedTab;
                default: return null;
            }
        }
    }

    /// <summary>The one table that says when a command of the menus can be used (#503); the menu and the shortcuts ask it, so they never disagree.</summary>
    public static class CommandAvailability
    {
        private static readonly CommandNeeds[] AllNeeds =
        {
            CommandNeeds.Editor, CommandNeeds.Selection, CommandNeeds.Connection, CommandNeeds.QueryTab, CommandNeeds.GridSelection, CommandNeeds.GridOneColumn, CommandNeeds.ClosedTab,
        };

        private static readonly Dictionary<MenuCommandKind, CommandNeeds> Table = new Dictionary<MenuCommandKind, CommandNeeds>
        {
            // The editor: the commands that read or change the script.
            [MenuCommandKind.RunStatement] = CommandNeeds.Editor,
            [MenuCommandKind.RunStatementsUpToCaret] = CommandNeeds.Editor,
            [MenuCommandKind.DryRun] = CommandNeeds.Editor,
            [MenuCommandKind.PreviewAsSelect] = CommandNeeds.Editor,
            [MenuCommandKind.FormatDocument] = CommandNeeds.Editor,
            [MenuCommandKind.FormatSelection] = CommandNeeds.Editor, // without a selection it formats the document
            [MenuCommandKind.QualifyNames] = CommandNeeds.Editor,
            [MenuCommandKind.ExpandStars] = CommandNeeds.Editor,
            [MenuCommandKind.RemoveBrackets] = CommandNeeds.Editor,
            [MenuCommandKind.InsertSemicolons] = CommandNeeds.Editor,
            [MenuCommandKind.RemoveUnusedVariables] = CommandNeeds.Editor,
            [MenuCommandKind.FixCodeIssues] = CommandNeeds.Editor,
            [MenuCommandKind.AddAs] = CommandNeeds.Editor,
            [MenuCommandKind.RemoveAs] = CommandNeeds.Editor,
            [MenuCommandKind.AddNPrefix] = CommandNeeds.Editor,
            [MenuCommandKind.ScriptSummary] = CommandNeeds.Editor,
            [MenuCommandKind.ParameterInfo] = CommandNeeds.Editor,
            [MenuCommandKind.ShowSuggestions] = CommandNeeds.Editor,
            [MenuCommandKind.GoToDefinition] = CommandNeeds.Editor,

            // A selection as well.
            [MenuCommandKind.UnformatSelection] = CommandNeeds.Editor | CommandNeeds.Selection,
            [MenuCommandKind.AddCommas] = CommandNeeds.Editor | CommandNeeds.Selection,
            [MenuCommandKind.AddQuotesAndCommas] = CommandNeeds.Editor | CommandNeeds.Selection,
            [MenuCommandKind.NewSnippetFromSelection] = CommandNeeds.Editor | CommandNeeds.Selection,
            [MenuCommandKind.SurroundWithSnippet] = CommandNeeds.Editor | CommandNeeds.Selection,

            // The database: an editor with a connection.
            [MenuCommandKind.QuickOpen] = CommandNeeds.Editor | CommandNeeds.Connection,
            [MenuCommandKind.FindInCode] = CommandNeeds.Editor | CommandNeeds.Connection,
            [MenuCommandKind.ShowDependencies] = CommandNeeds.Editor | CommandNeeds.Connection,
            [MenuCommandKind.LocateInObjectExplorer] = CommandNeeds.Editor | CommandNeeds.Connection,
            [MenuCommandKind.RefreshMetadata] = CommandNeeds.Connection,

            // Tabs.
            [MenuCommandKind.RenameQueryTab] = CommandNeeds.QueryTab,
            [MenuCommandKind.ReopenClosedTab] = CommandNeeds.ClosedTab,

            // The results grid.
            [MenuCommandKind.ScriptGridAsTempTable] = CommandNeeds.QueryTab | CommandNeeds.GridSelection,
            [MenuCommandKind.OpenGridInExcel] = CommandNeeds.QueryTab | CommandNeeds.GridSelection,
            [MenuCommandKind.CopyGridAs] = CommandNeeds.QueryTab | CommandNeeds.GridSelection,
            [MenuCommandKind.CopyGridAsInList] = CommandNeeds.QueryTab | CommandNeeds.GridSelection | CommandNeeds.GridOneColumn,
        };

        /// <summary>What the command needs.</summary>
        public static CommandNeeds NeedsOf(MenuCommandKind command) => Table[command];

        /// <summary>All commands of the table.</summary>
        public static IEnumerable<MenuCommandKind> Commands => Table.Keys;

        /// <summary>False only when a fact the command needs is known to be false; an unknown fact leaves the command enabled.</summary>
        public static bool IsEnabled(MenuCommandKind command, CommandSituation situation)
        {
            var needs = Table[command];
            foreach (var need in AllNeeds)
            {
                if ((needs & need) != 0 && situation.Fact(need) == false)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
