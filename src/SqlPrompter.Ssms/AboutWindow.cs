using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Help;
using SqlPrompter.Core.Updates;

namespace SqlPrompter.Ssms
{
    /// <summary>What the last finished update check of this session found.</summary>
    internal sealed class LastCheckResult
    {
        public LastCheckResult(Version latest, bool failed)
        {
            Latest = latest;
            Failed = failed;
        }

        public Version Latest { get; }

        public bool Failed { get; }
    }

    /// <summary>
    /// The About window of SQL Prompter (#502): version, SSMS build, license, links, the log and a button that copies the diagnostic text. It follows the
    /// theme of SSMS (the shell's own Help > About dialog cannot be extended). The update status is read from the last check of the session; a new
    /// check runs in the background from the button.
    /// </summary>
    internal sealed class AboutWindow : Window
    {
        private const string ProjectUrl = "https://github.com/" + Repository.Owner + "/" + Repository.Name;

        private static AboutWindow open;
        private readonly TextBlock updateStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly Version version = UpdateService.CurrentVersion();

        private AboutWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Title = "About SQL Prompter";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SnippetUi.Theme(this);
            ActiveEditor.SetOwner(this);

            var root = new StackPanel { Margin = new Thickness(18) };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new CrispImage { Moniker = KnownMonikers.Database, Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0) });
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "SQL Prompter", FontSize = 20, FontWeight = FontWeights.SemiBold });
            title.Children.Add(new TextBlock { Text = "Version " + AboutText.VersionLine(version), Margin = new Thickness(0, 2, 0, 0) });
            header.Children.Add(title);
            root.Children.Add(header);

            root.Children.Add(new TextBlock { Text = "IntelliSense for SQL Server Management Studio 22.", Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });

            var facts = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddRow(facts, "SSMS build", SsmsCompatibility.Current.SsmsBuild);
            AddRow(facts, "Runtime", Runtime());
            AddRow(facts, "License", AboutText.License + ". " + AboutText.Copyright + ".");
            AddRow(facts, "Project", Link(ProjectUrl, ProjectUrl));
            AddRow(facts, "License text", Link(ProjectUrl + "/blob/main/LICENSE", "LICENSE on GitHub"));
            AddRow(facts, "Log file", Log.FilePath);
            AddRow(facts, "Settings", SettingsService.FilePath);
            root.Children.Add(facts);

            var update = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };
            var check = SnippetUi.Button("Check for Updates", UpdateService.CheckNow);
            check.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(check, Dock.Right);
            update.Children.Add(check);
            update.Children.Add(updateStatus);
            root.Children.Add(update);
            RefreshStatus();

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            buttons.Children.Add(SnippetUi.Button("Open log", OpenLog));
            buttons.Children.Add(SnippetUi.Button("Copy diagnostics", CopyDiagnostics));
            var close = SnippetUi.Button("Close", Close);
            close.IsDefault = true;
            close.IsCancel = true;
            close.Margin = new Thickness(0);
            buttons.Children.Add(close);
            root.Children.Add(buttons);
            Content = root;

            UpdateService.LastCheckChanged += OnLastCheckChanged;
            Closed += (s, e) =>
            {
                UpdateService.LastCheckChanged -= OnLastCheckChanged;
                open = null;
            };
        }

        /// <summary>Opens the window (once; a second call brings the open one to the front).</summary>
        public static void Open()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (open != null)
            {
                open.Activate();
                return;
            }

            open = new AboutWindow();
            open.Show();
        }

        private static string Runtime() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Trim() + ", " + System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim();

        private static void AddRow(Grid grid, string name, string text) => AddRow(grid, name, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });

        private static void AddRow(Grid grid, string name, FrameworkElement value)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = name, Margin = new Thickness(0, 0, 16, 4), Opacity = 0.75, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(label, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            value.Margin = new Thickness(0, 0, 0, 4);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        private static TextBlock Link(string url, string text)
        {
            var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url) };
            link.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.PanelHyperlinkBrushKey);
            link.RequestNavigate += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error("Opening " + e.Uri + " failed", ex);
                }

                e.Handled = true;
            };
            return new TextBlock(link) { TextWrapping = TextWrapping.Wrap };
        }

        private static void OpenLog()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (File.Exists(Log.FilePath))
                {
                    VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, Log.FilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening the log failed", ex);
            }
        }

        private void OnLastCheckChanged(object sender, EventArgs e) => RefreshStatus();

        private void RefreshStatus()
        {
            var last = UpdateService.LastCheck;
            updateStatus.Text = AboutText.UpdateStatus(version, last?.Latest, last != null, last != null && last.Failed);
        }

        private void CopyDiagnostics()
        {
            try
            {
                var report = SsmsCompatibility.Current;
                var compatibility = report.AllFound ? "all requirements found" : report.UserMessage();
                Clipboard.SetText(AboutText.Diagnostics(version, report.SsmsBuild, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Trim(), System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim(), Log.FilePath, compatibility));
                updateStatus.Text = "The diagnostic information is on the clipboard.";
            }
            catch (Exception ex)
            {
                Log.Error("Copying the diagnostic information failed", ex);
            }
        }
    }
}
