using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Tools → Options → SQL Prompter: the settings live in the SQL Prompter settings window (SQL Prompter > Settings...), so this page only has
    /// buttons that open it and the snippet manager. The values are not stored by the page.
    /// </summary>
    [ComVisible(true)]
    [Guid("e4b1c7a2-5d38-4f96-8a0e-3c7d9b2f1a65")]
    public sealed class OptionsLauncherPage : DialogPage
    {
        private Control panel;

        protected override IWin32Window Window
        {
            get
            {
                if (panel == null)
                {
                    var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
                    layout.Controls.Add(new Label
                    {
                        Text = "The options of SQL Prompter are in one window with groups: completion, aliases, inserts and joins, snippets, code analysis, formatting, coloring, history, warnings and the import and export of settings.",
                        AutoSize = true,
                        MaximumSize = new Size(520, 0),
                    });
                    layout.Controls.Add(Button("Open SQL Prompter settings...", () => { ThreadHelper.ThrowIfNotOnUIThread(); SqlPrompterPackage.OpenSettings(); }));
                    layout.Controls.Add(Button("Edit snippets...", () => { ThreadHelper.ThrowIfNotOnUIThread(); SqlPrompterPackage.OpenSnippetManager(); }));
                    layout.Controls.Add(new Label { Text = "The same window opens from the menu SQL Prompter > Settings...", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 12, 3, 3) });
                    panel = new UserControl { Dock = DockStyle.Fill, MinimumSize = new Size(540, 240) };
                    panel.Controls.Add(layout);
                }

                return panel;
            }
        }

        private static Button Button(string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3, 12, 3, 0) };
            button.Click += (s, e) => click();
            return button;
        }
    }
}
