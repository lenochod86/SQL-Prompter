using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using SqlPrompter.Core.Updates;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The notice that a new version exists (#297): a window that does not block SSMS (it opens without taking the focus), the installed and the new version and three
    /// buttons. Closing it with the title bar button counts as "Remind me tomorrow".
    /// </summary>
    internal sealed class UpdateWindow : Window
    {
        private readonly Version current;
        private readonly ReleaseInfo release;
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        private readonly Button update;
        private readonly Button remind;
        private readonly Button skip;
        private bool decided;
        private bool busy;

        public UpdateWindow(Version current, ReleaseInfo release)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.current = current;
            this.release = release;
            Title = "SQL Prompter update";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SnippetUi.Theme(this);
            ActiveEditor.SetOwner(this);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = "A new version of SQL Prompter is available.", FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            var versions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            versions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            versions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddRow(versions, 0, "Installed version", current.ToString());
            AddRow(versions, 1, "New version", release.Version.ToString());
            root.Children.Add(versions);
            root.Children.Add(new TextBlock
            {
                Text = "Update downloads the new version, checks it and starts the installer, which waits until SSMS is closed.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Opacity = 0.75,
            });
            root.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            update = SnippetUi.Button("Update", StartUpdate);
            remind = SnippetUi.Button("Remind me tomorrow", OnRemind);
            skip = SnippetUi.Button("Skip this version", OnSkip);
            skip.Margin = new Thickness(0);
            buttons.Children.Add(update);
            buttons.Children.Add(remind);
            buttons.Children.Add(skip);
            root.Children.Add(buttons);
            Content = root;

            Closing += (s, e) =>
            {
                if (busy)
                {
                    e.Cancel = true;
                }
            };
            Closed += (s, e) =>
            {
                if (!decided)
                {
                    Decide(() => UpdateService.Remember(u => UpdateDecision.RemindTomorrow(UpdateDecision.Checked(u, DateTime.Now), DateTime.Now)));
                }
            };
        }

        private static void AddRow(Grid grid, int row, string name, string value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = name, Margin = new Thickness(0, 0, 16, 2) };
            var number = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
            Grid.SetRow(label, row);
            Grid.SetRow(number, row);
            Grid.SetColumn(number, 1);
            grid.Children.Add(label);
            grid.Children.Add(number);
        }

        private void Decide(Action remember)
        {
            decided = true;
            try
            {
                remember();
            }
            catch (Exception ex)
            {
                Log.Error("Saving the update decision failed", ex);
            }
        }

        private void OnRemind()
        {
            Decide(() => UpdateService.Remember(u => UpdateDecision.RemindTomorrow(UpdateDecision.Checked(u, DateTime.Now), DateTime.Now)));
            Close();
        }

        private void OnSkip()
        {
            Decide(() => UpdateService.Remember(u => UpdateDecision.Skip(UpdateDecision.Checked(u, DateTime.Now), release)));
            Close();
        }

        private void StartUpdate()
        {
#pragma warning disable VSSDK007 // fire and forget on purpose: the button returns at once, the window shows the progress
            ThreadHelper.JoinableTaskFactory.RunAsync(OnUpdateAsync).Task.FileAndForget("SqlPrompter/Update");
#pragma warning restore VSSDK007
        }

        private async Task OnUpdateAsync()
        {
            if (busy)
            {
                return;
            }

            busy = true;
            update.IsEnabled = remind.IsEnabled = skip.IsEnabled = false;
            ShowStatus("Downloading version " + release.Version + "...");
            string error;
            try
            {
                error = await UpdateService.DownloadAndInstallAsync(release, current);
            }
            catch (Exception ex)
            {
                Log.Error("Update failed", ex);
                error = ex.Message;
            }

            busy = false;
            if (error == null)
            {
                // The installer is running and waits for SSMS to close; if the user cancels it, the next day asks again.
                Decide(() => UpdateService.Remember(u => UpdateDecision.RemindTomorrow(UpdateDecision.Checked(u, DateTime.Now), DateTime.Now)));
                Close();
                return;
            }

            ShowStatus(error + " You can also get it from the release page.");
            update.IsEnabled = remind.IsEnabled = skip.IsEnabled = true;
            update.Content = "Try again";
            if (!(status.Tag is Button))
            {
                var page = SnippetUi.Button("Release page", () => UpdateService.OpenReleasePage(release));
                ((StackPanel)update.Parent).Children.Insert(1, page);
                status.Tag = page;
            }
        }

        private void ShowStatus(string text)
        {
            status.Text = text;
            status.Visibility = Visibility.Visible;
        }
    }
}
