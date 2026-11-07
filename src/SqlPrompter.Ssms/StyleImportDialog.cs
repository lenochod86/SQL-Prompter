using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SqlPrompter.Core.Formatting;

namespace SqlPrompter.Ssms
{
    /// <summary>The import of a SQL Prompt style (#304): the file dialog, the new style in the list and a summary of what was and was not taken over.</summary>
    internal static class StyleImportDialog
    {
        /// <summary>
        /// Asks for a .json file and adds its style to <paramref name="styles"/>; returns the new style, or null when cancelled or the file is not a style
        /// (an error is shown and nothing changes).
        /// </summary>
        public static NamedFormatStyle Run(IWin32Window owner, IList<NamedFormatStyle> styles)
        {
            try
            {
                using (var dialog = new OpenFileDialog
                {
                    Title = "Import a SQL Prompt formatting style (.json)",
                    Filter = "SQL Prompt styles (*.json)|*.json|All files (*.*)|*.*",
                })
                {
                    if (dialog.ShowDialog(owner) != DialogResult.OK)
                    {
                        return null;
                    }

                    var json = System.IO.File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8);
                    var imported = SqlPromptStyleImporter.ImportInto(styles, json, dialog.FileName, out var result);
                    var text = "Imported as the style \"" + imported.Name + "\":\n- " + (result.Applied.Count == 0 ? "(no option of this formatter was set)" : string.Join("\n- ", result.Applied));
                    if (result.Approximate.Count > 0)
                    {
                        text += "\n\nMapped approximately (" + result.Approximate.Count + "):\n- " + string.Join("\n- ", result.Approximate);
                    }

                    if (result.Notes.Count > 0)
                    {
                        text += "\n\nNot taken over (" + result.Notes.Count + "), left at the default values; adjust by hand if you need them:\n- " + string.Join("\n- ", result.Notes);
                    }

                    MessageBox.Show(owner, text, "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return imported;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("Importing the formatting style failed", ex);
                MessageBox.Show(owner, "The style could not be imported: " + ex.Message, "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }
    }
}
