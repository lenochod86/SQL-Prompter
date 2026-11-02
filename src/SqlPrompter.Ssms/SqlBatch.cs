using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;

namespace SqlPrompter.Ssms
{
    /// <summary>Vyřízne dávku s kurzorem: řádky mezi oddělovači GO.</summary>
    internal static class SqlBatch
    {
        /// <summary>Delší dávka se nerozebírá, aby vlákno UI nečekalo.</summary>
        private const int MaxLength = 500000;

        /// <summary>Kolik řádků se nejvýš prohledá oběma směry.</summary>
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

        private static bool IsGo(ITextSnapshotLine line) => line.Length <= 40 && Go.IsMatch(line.GetText());
    }
}
