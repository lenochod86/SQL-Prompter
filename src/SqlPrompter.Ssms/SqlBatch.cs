using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using SqlPrompter.Core.Analysis;

namespace SqlPrompter.Ssms
{
    /// <summary>Extracts the batch containing the caret: the lines between GO separators.</summary>
    internal static class SqlBatch
    {
        /// <summary>Longer batches are not analyzed so that the UI thread does not wait.</summary>
        private const int MaxLength = 500000;

        /// <summary>Maximum number of lines searched in each direction.</summary>
        private const int MaxLines = 20000;

        private static readonly Regex Go = new Regex(@"^\s*GO(\s+\d+)?\s*(--.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryGet(ITextSnapshot snapshot, int position, out int start, out string text)
        {
            var caretLine = snapshot.GetLineFromPosition(position).LineNumber;
            var first = caretLine;
            while (first > 0 && caretLine - first < MaxLines && !IsGo(snapshot.GetLineFromLineNumber(first - 1)))
            {
                first--;
            }

            var last = caretLine;
            while (last < snapshot.LineCount - 1 && last - caretLine < MaxLines && !IsGo(snapshot.GetLineFromLineNumber(last + 1)))
            {
                last++;
            }

            start = snapshot.GetLineFromLineNumber(first).Start.Position;
            var end = snapshot.GetLineFromLineNumber(last).End.Position;
            if (end - start > MaxLength)
            {
                text = null;
                return false;
            }

            text = snapshot.GetText(start, end - start);
            return true;
        }

        /// <summary>The database of the nearest USE line above the caret (also in earlier batches, since USE lasts until changed); null if none.</summary>
        public static string FindUsedDatabase(ITextSnapshot snapshot, int position)
        {
            var caretLine = snapshot.GetLineFromPosition(position).LineNumber;
            for (var number = caretLine; number >= 0 && caretLine - number < MaxLines; number--)
            {
                var line = snapshot.GetLineFromLineNumber(number);
                if (line.Length <= 300 && UseStatement.TryParse(line.GetText(), out var database))
                {
                    return database;
                }
            }

            return null;
        }

        private static bool IsGo(ITextSnapshotLine line) => line.Length <= 40 && Go.IsMatch(line.GetText());
    }
}
