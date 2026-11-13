using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SqlPrompter.Core.Editing
{
    /// <summary>
    /// The files of Open in Excel (#445): the grid text turned into the header and the rows for <see cref="XlsxWriter"/>, and the temporary
    /// folder with the files it writes (a new name every time, old files removed).
    /// </summary>
    public static class GridExcel
    {
        /// <summary>The files older than this are deleted when a new one is written.</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

        /// <summary>
        /// Splits the text of Copy with Headers (tab separated, the column names in the first line) into the names and the rows.
        /// False when there is no header or no row.
        /// </summary>
        public static bool TryParse(string gridText, out List<string> headers, out List<string[]> rows)
        {
            headers = null;
            rows = null;
            var lines = (gridText ?? string.Empty).Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            if (lines.Count < 2)
            {
                return false;
            }

            headers = lines[0].Split('\t').ToList();
            rows = lines.Skip(1).Select(l => l.Split('\t')).ToList();
            return true;
        }

        /// <summary>A path in <paramref name="folder"/> named from the time (<c>Results_2026-11-12_12-20-52.xlsx</c>); a name that is taken gets <c>_2</c>, <c>_3</c> ... so that no file is overwritten.</summary>
        public static string NewPath(string folder, DateTime now)
        {
            var stem = "Results_" + now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, stem + ".xlsx");
            for (var i = 2; File.Exists(path); i++)
            {
                path = Path.Combine(folder, stem + "_" + i + ".xlsx");
            }

            return path;
        }

        /// <summary>Deletes the <c>Results_*.xlsx</c> files older than <see cref="MaxAge"/>; a file that is open in a program (locked) or cannot be deleted for another reason is left. Returns how many were deleted.</summary>
        public static int Cleanup(string folder, DateTime now)
        {
            var deleted = 0;
            try
            {
                if (!Directory.Exists(folder))
                {
                    return 0;
                }

                foreach (var file in Directory.GetFiles(folder, "Results_*.xlsx"))
                {
                    try
                    {
                        if (now - File.GetLastWriteTime(file) > MaxAge)
                        {
                            File.Delete(file);
                            deleted++;
                        }
                    }
                    catch (IOException)
                    {
                        // Locked by the program that shows it: next time.
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return deleted;
        }
    }
}
