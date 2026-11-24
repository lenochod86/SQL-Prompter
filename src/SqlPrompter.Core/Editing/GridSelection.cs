using System.Collections.Generic;

namespace SqlPrompter.Core.Editing
{
    /// <summary>What the selection of the results grid covers, from the blocks of cells it consists of (#446).</summary>
    public static class GridSelection
    {
        /// <summary>
        /// True if the selected cells belong to more than one column. Each block is a range of columns (first and last, both included); blocks of
        /// the same single column, next to each other or not (several rows, rows that are not adjacent), are still one column. An empty selection is not
        /// "several columns".
        /// </summary>
        public static bool SpansSeveralColumns(IEnumerable<(int FirstColumn, int LastColumn)> blocks)
        {
            if (blocks == null)
            {
                return false;
            }

            var column = int.MinValue;
            foreach (var (first, last) in blocks)
            {
                if (last != first)
                {
                    return true;
                }

                if (column != int.MinValue && column != first)
                {
                    return true;
                }

                column = first;
            }

            return false;
        }
    }
}
