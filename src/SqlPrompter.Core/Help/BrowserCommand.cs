using System;

namespace SqlPrompter.Core.Help
{
    /// <summary>Turns the open command of a browser from the registry (<c>"C:\...\msedge.exe" --single-argument %1</c>) into a program and its arguments for one address (#296).</summary>
    public static class BrowserCommand
    {
        /// <summary>Returns false when the command is empty or has no program.</summary>
        public static bool TryBuild(string command, string url, out string program, out string arguments)
        {
            program = null;
            arguments = null;
            if (string.IsNullOrWhiteSpace(command) || string.IsNullOrEmpty(url))
            {
                return false;
            }

            var text = command.Trim();
            string rest;
            if (text[0] == '"')
            {
                var end = text.IndexOf('"', 1);
                if (end < 0)
                {
                    return false;
                }

                program = text.Substring(1, end - 1);
                rest = text.Substring(end + 1).Trim();
            }
            else
            {
                var space = text.IndexOf(' ');
                program = space < 0 ? text : text.Substring(0, space);
                rest = space < 0 ? string.Empty : text.Substring(space + 1).Trim();
            }

            if (program.Length == 0)
            {
                return false;
            }

            // The address replaces %1 (or %L); a command without a placeholder gets it at the end.
            if (rest.Contains("%1") || rest.Contains("%L") || rest.Contains("%l"))
            {
                arguments = rest.Replace("%1", url).Replace("%L", url).Replace("%l", url);
            }
            else
            {
                arguments = (rest + " \"" + url + "\"").Trim();
            }

            return true;
        }
    }
}
