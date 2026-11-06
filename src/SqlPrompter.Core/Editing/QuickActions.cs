using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Core.Editing
{
    /// <summary>One entry of the quick actions popup (the same actions as the SQL Prompter menu); the editor layer maps <see cref="Id"/> to the code.</summary>
    public sealed class QuickAction
    {
        public QuickAction(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
        }

        public string Id { get; }

        public string Name { get; }

        public string Description { get; }
    }

    /// <summary>Which actions and snippets the quick actions popup offers, depending on whether text is selected.</summary>
    public static class QuickActionCatalog
    {
        public const string Format = "format";
        public const string RemoveBrackets = "removeBrackets";
        public const string InsertSemicolons = "insertSemicolons";
        public const string RemoveUnusedVariables = "removeUnusedVariables";
        public const string FixCodeIssues = "fixCodeIssues";
        public const string PreviewAsSelect = "previewAsSelect";
        public const string DryRun = "dryRun";
        public const string ExpandStars = "expandStars";
        public const string QualifyNames = "qualifyNames";
        public const string AddNPrefix = "addNPrefix";
        public const string AddAs = "addAs";
        public const string RemoveAs = "removeAs";
        public const string AddCommas = "addCommas";
        public const string AddQuotesAndCommas = "addQuotesAndCommas";

        /// <summary>The actions for the selection, or for the whole script when nothing is selected.</summary>
        public static IReadOnlyList<QuickAction> Actions(bool hasSelection)
        {
            var scope = hasSelection ? "the selection" : "the script";
            var list = new List<QuickAction>
            {
                new QuickAction(Format, hasSelection ? "Format Selection" : "Format Document", "Formats " + scope + " with the formatting style."),
                new QuickAction(QualifyNames, "Qualify Object Names", "Adds the schema to tables and views in " + scope + "."),
                new QuickAction(ExpandStars, "Expand All Select Stars", "Replaces every * in " + scope + " with the column list."),
                new QuickAction(RemoveBrackets, "Remove Square Brackets", "Removes brackets that are not needed in " + scope + "."),
                new QuickAction(InsertSemicolons, "Insert Semicolons", "Ends the statements of " + scope + " with a semicolon."),
                new QuickAction(RemoveUnusedVariables, "Remove Unused Variables", "Removes DECLAREs that nothing uses in " + scope + "."),
                new QuickAction(FixCodeIssues, "Fix Code Analysis Issues", "Repairs = NULL, @@IDENTITY and types without a length in " + scope + "."),
                new QuickAction(PreviewAsSelect, "Preview UPDATE/DELETE As SELECT", "Opens a SELECT of the rows that the statement of " + scope + " would change in a new window."),
                new QuickAction(DryRun, "Run And Roll Back (Dry Run)", "Opens the statement of " + scope + " inside BEGIN TRANSACTION ... ROLLBACK with its row count in a new window."),
                new QuickAction(AddNPrefix, "Add N Prefix To Strings", "Turns 'text' into N'text' in " + scope + "."),
                new QuickAction(AddAs, "Add AS To Aliases", "Writes AS before every alias in " + scope + "."),
                new QuickAction(RemoveAs, "Remove AS From Aliases", "Drops AS before the aliases in " + scope + "."),
            };
            if (hasSelection)
            {
                list.Add(new QuickAction(AddCommas, "Add Commas To Selected Lines", "Puts a comma at the end of every line but the last."));
                list.Add(new QuickAction(AddQuotesAndCommas, "Add Quotes And Commas To Selected Lines", "Turns the lines into a list of quoted strings."));
            }

            return list;
        }

        /// <summary>With a selection the snippets that wrap it ($SELECTEDTEXT$), otherwise the snippets that do not need one.</summary>
        public static IReadOnlyList<Snippet> SnippetsFor(IEnumerable<Snippet> snippets, bool hasSelection) =>
            snippets.Where(s => SnippetText.UsesSelection(s) == hasSelection).OrderBy(s => s.Prefix, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Entries whose name or description contains every word of <paramref name="text"/> (case-insensitive).</summary>
        public static bool Matches(string name, string description, string text)
        {
            var words = (text ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => (name ?? string.Empty).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  (description ?? string.Empty).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
