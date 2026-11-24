using Microsoft.VisualStudio.Text.Editor;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The tab size of the SQL editor (Tools &gt; Options &gt; Text Editor &gt; Transact-SQL &gt; Tabs), for the previews that show editor text (#450).
    /// Remembered whenever an editor gets the focus or its tab size changes, so that reading it needs neither the UI thread nor the active editor.
    /// </summary>
    internal static class EditorTabSize
    {
        private static volatile int size = TabExpander.DefaultTabSize;

        /// <summary>The tab size of the editor that had the focus last; 4 when none has been seen.</summary>
        public static int Current() => size;

        /// <summary>Remembers the tab size of a view; later changes of the option are followed.</summary>
        public static void Watch(ITextView view)
        {
            Remember(view);
            view.Options.OptionChanged += (s, e) =>
            {
                if (e.OptionId == DefaultOptions.TabSizeOptionId.Name)
                {
                    Remember(view);
                }
            };
        }

        public static void Remember(ITextView view)
        {
            try
            {
                var value = view.Options.GetOptionValue(DefaultOptions.TabSizeOptionId);
                if (value > 0)
                {
                    size = value;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("Reading the tab size of the editor failed", ex);
            }
        }
    }
}
