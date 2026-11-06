using System;
using System.Windows.Forms;
using SqlPrompter.Core.Formatting;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Ssms
{
    /// <summary>Export and import of the settings file and of a SQL Prompt formatting style, shared by the menu commands and the settings window.</summary>
    internal static class SettingsFiles
    {
        public static void Export(IWin32Window owner)
        {
            try
            {
                using (var dialog = new SaveFileDialog
                {
                    Title = "Export SQL Prompter settings",
                    Filter = "SQL Prompter settings (*.json)|*.json",
                    FileName = "sqlprompter-settings.json",
                })
                {
                    if (dialog.ShowDialog(owner) == DialogResult.OK)
                    {
                        SettingsJson.Save(dialog.FileName, SettingsService.Current);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exporting the settings failed", ex);
                Show(owner, "Exporting the settings failed: " + ex.Message);
            }
        }

        /// <summary>Returns true when the settings changed.</summary>
        public static bool Import(IWin32Window owner)
        {
            try
            {
                using (var dialog = new OpenFileDialog
                {
                    Title = "Import settings (SQL Prompter .json or SQL Prompt .settings)",
                    Filter = "Settings files (*.json;*.settings)|*.json;*.settings|All files (*.*)|*.*",
                })
                {
                    if (dialog.ShowDialog(owner) != DialogResult.OK)
                    {
                        return false;
                    }

                    var result = SettingsImporter.ImportFile(dialog.FileName, SettingsService.Current);
                    SettingsService.Update(result.Settings);
                    var text = "Imported:\n- " + string.Join("\n- ", result.Applied);
                    if (result.Notes.Count > 0)
                    {
                        text += "\n\nNot applied:\n- " + string.Join("\n- ", result.Notes);
                    }

                    Show(owner, text);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Importing the settings failed", ex);
                Show(owner, "Importing the settings failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Returns true when the settings changed.</summary>
        public static bool ImportFormatStyle(IWin32Window owner)
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
                        return false;
                    }

                    var json = System.IO.File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8);
                    var result = SqlPromptStyleImporter.Import(json, SettingsService.Current.Formatting);
                    var settings = SettingsService.Current.Clone();
                    settings.Formatting = result.Style;
                    SettingsService.Update(settings);
                    var text = "Imported:\n- " + (result.Applied.Count == 0 ? "(nothing)" : string.Join("\n- ", result.Applied));
                    if (result.Notes.Count > 0)
                    {
                        text += "\n\nNot applied (the formatter has no such option):\n- " + string.Join("\n- ", result.Notes);
                    }

                    Show(owner, text);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Importing the formatting style failed", ex);
                Show(owner, "Importing the formatting style failed: " + ex.Message);
                return false;
            }
        }

        private static void Show(IWin32Window owner, string text) =>
            MessageBox.Show(owner, text, "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
