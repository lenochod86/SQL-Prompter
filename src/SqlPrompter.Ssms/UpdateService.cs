using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SqlPrompter.Core.Settings;
using SqlPrompter.Core.Updates;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The check for a new version (#297). Once a day at the start it asks GitHub for the latest release (one anonymous HTTPS request, off the UI thread,
    /// a short timeout, every error ignored) and, when a newer one is out and the user has not skipped or postponed it, shows <see cref="UpdateWindow"/>.
    /// Update downloads the VSIX of the release into %LOCALAPPDATA%\SqlPrompter\updates, verifies its SHA-256 against the checksum of the same release
    /// and starts the VSIX installer of this SSMS, which waits for SSMS to close.
    /// </summary>
    internal static class UpdateService
    {
        /// <summary>For a test: the URL of a local server that answers like the GitHub API (loopback only, ignored otherwise).</summary>
        private const string TestUrlVariable = "SQLPROMPTER_UPDATE_URL";

        private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);

        private static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(CreateClient);
        private static int running;

        public static string UpdatesFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SqlPrompter",
            "updates");

        /// <summary>The version of the installed VSIX, read from the manifest next to the assembly; null when it cannot be read.</summary>
        public static Version CurrentVersion()
        {
            try
            {
                var directory = Path.GetDirectoryName(typeof(UpdateService).Assembly.Location);
                var manifest = Path.Combine(directory, "extension.vsixmanifest");
                if (File.Exists(manifest))
                {
                    var text = File.ReadAllText(manifest, Encoding.UTF8);
                    var xml = new System.Xml.XmlDocument();
                    xml.LoadXml(text);
                    var identity = xml.GetElementsByTagName("Identity").Cast<System.Xml.XmlElement>().FirstOrDefault();
                    if (identity != null && Version.TryParse(identity.GetAttribute("Version"), out var version))
                    {
                        return version;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading the installed version failed", ex);
            }

            return null;
        }

        /// <summary>Called once from the package: deletes leftovers of earlier downloads and, after a delay, makes the daily check.</summary>
        public static void Start()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var current = CurrentVersion();
                    if (current == null)
                    {
                        Log.Write("Update check: the installed version is not known, skipped.");
                        return;
                    }

                    DeleteLeftovers(current, null);
                    await Task.Delay(StartDelay).ConfigureAwait(false);
                    await RunAsync(current, manual: false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error("Update check failed", ex);
                }
            });
        }

        /// <summary>The menu command: checks now, whatever the settings and the earlier decisions say, and tells the result.</summary>
        public static void CheckNow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var current = CurrentVersion();
            if (current == null)
            {
                MessageBox.Show(new MainWindowOwner(), "The installed version of SQL Prompter could not be read.", "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await RunAsync(current, manual: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error("Update check failed", ex);
                }
            });
        }

        /// <summary>Downloads and verifies the VSIX of the release and starts the installer. Returns null on success, otherwise what went wrong.</summary>
        public static async Task<string> DownloadAndInstallAsync(ReleaseInfo release, Version current)
        {
            try
            {
                var testHost = TestEndpoint()?.Host;
                if (!UpdateFiles.IsTrustedDownload(release.VsixUrl, testHost) || !UpdateFiles.IsTrustedDownload(release.ChecksumUrl, testHost))
                {
                    Log.Write("Update: the download address is not a release of the repository, refused.");
                    return "The download address is not a release of the SQL Prompter repository.";
                }

                var checksumText = Encoding.UTF8.GetString(await GetBytesAsync(release.ChecksumUrl, CheckTimeout).ConfigureAwait(false));
                var expected = UpdateFiles.ParseChecksum(checksumText);
                if (expected == null)
                {
                    return "The checksum of the release could not be read.";
                }

                var data = await GetBytesAsync(release.VsixUrl, DownloadTimeout).ConfigureAwait(false);
                if (!UpdateFiles.Matches(data, expected))
                {
                    Log.Write("Update: the checksum of the download does not match, deleted.");
                    return "The downloaded file does not match its checksum and was discarded.";
                }

                Directory.CreateDirectory(UpdatesFolder);
                var part = Path.Combine(UpdatesFolder, UpdateFiles.PartName(release.Version));
                var target = Path.Combine(UpdatesFolder, UpdateFiles.VsixName(release.Version));
                File.WriteAllBytes(part, data);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(part, target);
                DeleteLeftovers(current, release.Version);
                Log.Write("Update: " + release.Version + " downloaded and verified (" + data.Length + " bytes), starting the installer.");
                return StartInstaller(target);
            }
            catch (Exception ex)
            {
                Log.Error("Update failed", ex);
                DeleteLeftovers(current, null);
                return "The update failed: " + ex.Message;
            }
        }

        /// <summary>Opens the page of the release in the browser (only a page of github.com).</summary>
        public static void OpenReleasePage(ReleaseInfo release)
        {
            try
            {
                if (Uri.TryCreate(release.PageUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
                    string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening the release page failed", ex);
            }
        }

        /// <summary>Remembers a decision of the user in the settings file.</summary>
        public static void Remember(Func<UpdateSettings, UpdateSettings> change)
        {
            // The notifications of a settings change are handled on the UI thread, so the change is made there (a call from the UI thread runs at once).
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var settings = SettingsService.Current.Clone();
                settings.Updates = change(settings.Updates);
                SettingsService.Update(settings);
            });
        }

        private static async Task RunAsync(Version current, bool manual)
        {
            if (Interlocked.Exchange(ref running, 1) == 1)
            {
                return;
            }

            try
            {
                var today = DateTime.Now;
                if (!manual && !UpdateDecision.ShouldCheck(SettingsService.Current.Updates, today))
                {
                    return;
                }

                ReleaseInfo release = null;
                var failed = false;
                try
                {
                    var json = Encoding.UTF8.GetString(await GetBytesAsync(LatestReleaseUrl(), CheckTimeout).ConfigureAwait(false));
                    release = ReleaseInfo.Parse(json);
                }
                catch (Exception ex)
                {
                    // No network, a timeout, a repository that cannot be read: nothing is shown, the next day tries again.
                    failed = true;
                    Log.Write("Update check: no answer (" + ex.GetType().Name + ": " + ex.Message + ").");
                }

                // The settings change notifications are handled on the UI thread (the tab colors), so everything below that touches them runs there.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var settings = SettingsService.Current.Updates;
                var announce = release != null && (manual ? release.Version > current : UpdateDecision.ShouldAnnounce(settings, current, release, today));
                Log.Write("Update check: installed " + current + ", latest " + (release?.Version.ToString() ?? "unknown") + (announce ? ", announcing." : ", nothing to announce."));
                if (announce)
                {
                    // The day is marked when the user decides, so a window that was never answered (SSMS closed) comes back at the next start.
                    new UpdateWindow(current, release).Show();
                    return;
                }

                if (!failed)
                {
                    Remember(s => UpdateDecision.Checked(s, today));
                }

                if (manual)
                {
                    var text = release == null
                        ? "The latest version could not be found. The network may be unreachable, or the releases of the repository are not public yet. The log has the details."
                        : "SQL Prompter " + current + " is the latest version.";
                    MessageBox.Show(new MainWindowOwner(), text, "SQL Prompter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                Interlocked.Exchange(ref running, 0);
            }
        }

        private static Uri TestEndpoint()
        {
            var text = Environment.GetEnvironmentVariable(TestUrlVariable);
            return !string.IsNullOrEmpty(text) && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IsLoopback ? uri : null;
        }

        private static string LatestReleaseUrl() => TestEndpoint()?.AbsoluteUri ?? Repository.LatestReleaseApi;

        private static async Task<byte[]> GetBytesAsync(string url, TimeSpan timeout)
        {
            using (var cancel = new CancellationTokenSource(timeout))
            using (var response = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancel.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            // A generic client name only: nothing about the user, the computer or the installation is sent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SqlPrompter-UpdateCheck");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>Deletes partial downloads and VSIX files of the running or an older version (and, with <paramref name="keep"/>, of every other version). Only files with our names, only in the updates folder.</summary>
        private static void DeleteLeftovers(Version current, Version keep)
        {
            try
            {
                if (!Directory.Exists(UpdatesFolder))
                {
                    return;
                }

                var names = Directory.GetFiles(UpdatesFolder).Select(Path.GetFileName).ToList();
                foreach (var name in UpdateFiles.Leftovers(names, current, keep))
                {
                    try
                    {
                        File.Delete(Path.Combine(UpdatesFolder, name));
                        Log.Write("Update: deleted " + name + ".");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Deleting " + name + " failed", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Cleaning the updates folder failed", ex);
            }
        }

        /// <summary>Starts the VSIX installer of the running SSMS (next to Ssms.exe); it waits for SSMS to close and then installs. Returns null on success.</summary>
        private static string StartInstaller(string vsixPath)
        {
            try
            {
                var ide = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                var installer = Path.Combine(ide, "VSIXInstaller.exe");
                if (!File.Exists(installer))
                {
                    return "The VSIX installer of SSMS was not found; open the downloaded file " + vsixPath + " yourself.";
                }

                Process.Start(new ProcessStartInfo(installer, "\"" + vsixPath + "\"") { UseShellExecute = false });
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Starting the VSIX installer failed", ex);
                return "The VSIX installer could not be started: " + ex.Message;
            }
        }

        private sealed class MainWindowOwner : IWin32Window
        {
            public IntPtr Handle => Process.GetCurrentProcess().MainWindowHandle;
        }
    }
}
