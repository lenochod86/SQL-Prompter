using System.Linq;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Editing
{
    /// <summary>Applies the repairs of the code analysis (#208) to a script: <c>= NULL</c>, <c>@@IDENTITY</c>, a type without a length.</summary>
    public static class CodeFixes
    {
        /// <summary>
        /// The script with the repair of every issue that has one (the rules switched on in <paramref name="settings"/>); text that is not an issue stays as it was.
        /// Overlapping repairs: the first one wins.
        /// </summary>
        public static string Apply(string text, AnalysisSettings settings, SqlPrompter.Core.Model.IDatabaseMetadata metadata = null)
        {
            if (string.IsNullOrEmpty(text) || settings == null)
            {
                return text;
            }

            // The command works also when the squiggles are off.
            var rules = settings.Clone();
            rules.Enabled = true;
            var fixes = CodeAnalyzer.Analyze(text, rules, metadata).Where(i => i.Fix != null).Select(i => i.Fix).OrderBy(f => f.Start).ToList();
            var result = text;
            var limit = int.MaxValue;
            foreach (var fix in Enumerable.Reverse(fixes))
            {
                if (fix.Start + fix.Length > limit)
                {
                    continue;
                }

                result = result.Substring(0, fix.Start) + fix.Replacement + result.Substring(fix.Start + fix.Length);
                limit = fix.Start;
            }

            return result;
        }
    }
}
