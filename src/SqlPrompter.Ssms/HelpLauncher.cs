using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using SqlPrompter.Core.Help;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Opens the help (#296): help\index.html next to the assembly, in the default browser; the online copy when the local one is missing.
    /// The browser is found through the https handler of the user, not through the .html association: on a computer without a default app for
    /// .html, opening the file would show the "How do you want to open this file" dialog.
    /// </summary>
    internal static class HelpLauncher
    {
        private const string OnlineUrl = "https://github.com/lenochod86/SQL-Prompter/blob/main/docs/help/index.html";

        public static void Open()
        {
            try
            {
                var local = Path.Combine(Path.GetDirectoryName(typeof(HelpLauncher).Assembly.Location), "help", "index.html");
                if (File.Exists(local) && TryOpenInBrowser(new Uri(local).AbsoluteUri))
                {
                    return;
                }

                Process.Start(new ProcessStartInfo(OnlineUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Opening the help failed", ex);
            }
        }

        private static bool TryOpenInBrowser(string url)
        {
            try
            {
                var command = BrowserOpenCommand();
                if (command != null && BrowserCommand.TryBuild(command, url, out var program, out var arguments))
                {
                    Log.Write("Opening the help: " + program + " " + arguments);
                    Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening the help in the default browser failed, trying the shell", ex);
            }

            return false;
        }

        /// <summary>The open command of the program that handles https links for this user, or null.</summary>
        private static string BrowserOpenCommand()
        {
            var progId = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId", null) as string;
            if (string.IsNullOrEmpty(progId))
            {
                return null;
            }

            return Registry.GetValue(@"HKEY_CLASSES_ROOT\" + progId + @"\shell\open\command", null, null) as string;
        }
    }
}
