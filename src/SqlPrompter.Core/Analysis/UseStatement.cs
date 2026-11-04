using System.Text.RegularExpressions;

namespace SqlPrompter.Core.Analysis
{
    /// <summary>
    /// Recognizes a line that switches database: <c>USE Sales</c>, <c>USE [Sales db];</c>. Completion after it uses that database.
    /// Only a USE that starts its line counts; the scan is line based so that it stays fast on long scripts.
    /// </summary>
    public static class UseStatement
    {
        private static readonly Regex Pattern = new Regex(
            @"^\s*USE\s+(?:\[(?<name>(?:[^\]]|\]\])+)\]|""(?<name>[^""]+)""|(?<name>[A-Za-z_@#][\w@#$]*))\s*(?:;|--.*|/\*.*)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParse(string line, out string database)
        {
            database = null;
            if (line == null || line.Length > 300)
            {
                return false;
            }

            var match = Pattern.Match(line);
            if (!match.Success)
            {
                return false;
            }

            database = match.Groups["name"].Value.Replace("]]", "]");
            return true;
        }
    }
}
