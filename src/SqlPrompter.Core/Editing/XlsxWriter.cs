using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SqlPrompter.Core.Editing
{
    /// <summary>What <see cref="XlsxWriter.Write"/> did: how much it wrote and what did not fit (#445).</summary>
    public sealed class XlsxResult
    {
        /// <summary>The data rows written (the header row is not counted).</summary>
        public int Rows { get; internal set; }

        public int Columns { get; internal set; }

        /// <summary>The result had more rows than a sheet holds (1,048,576 with the header); the rest was left out.</summary>
        public bool RowsTruncated { get; internal set; }

        /// <summary>The result had more columns than a sheet holds (16,384); the rest was left out.</summary>
        public bool ColumnsTruncated { get; internal set; }

        /// <summary>Texts longer than a cell holds (32,767 characters) that were cut.</summary>
        public int CutTexts { get; internal set; }
    }

    /// <summary>
    /// A small writer of the Open XML spreadsheet format (a <c>.xlsx</c> file): one sheet <c>Sheet1</c>, the column names in the first row and the
    /// values below it, written straight to a zip with no Office, Excel or third-party package (#445). The text of the results grid has no types, so
    /// every column gets one by the look of its values: whole numbers and decimals become numeric cells (with their full precision), dates and times
    /// become Excel dates with a date format, everything else is text (never a formula, even when it starts with <c>=</c>). <c>NULL</c> is an empty cell.
    /// </summary>
    public static class XlsxWriter
    {
        public const int MaxRows = 1048576;
        public const int MaxColumns = 16384;
        public const int MaxTextLength = 32767;

        private enum ColumnKind
        {
            Text,
            Integer,
            Decimal,
            Date,
            DateTime,
            Time,
        }

        private static readonly Regex IntegerPattern = new Regex(@"^-?(0|[1-9][0-9]{0,14})$", RegexOptions.Compiled);
        private static readonly Regex DecimalPattern = new Regex(@"^-?(0|[1-9][0-9]*)\.[0-9]+$", RegexOptions.Compiled);
        private static readonly Regex DatePattern = new Regex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.Compiled);
        private static readonly Regex DateTimePattern = new Regex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}[ T][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?$", RegexOptions.Compiled);
        private static readonly Regex TimePattern = new Regex(@"^[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?$", RegexOptions.Compiled);

        /// <summary>The first date Excel shows correctly (1900-03-01; before it the 1900 leap year bug shifts the serial numbers).</summary>
        private static readonly DateTime FirstExcelDate = new DateTime(1900, 3, 1);

        private static readonly DateTime Epoch = new DateTime(1899, 12, 30);

        /// <summary>Writes the file. A value that is <c>NULL</c> (or empty) leaves its cell empty; a row shorter than the header is padded with empty cells.</summary>
        public static XlsxResult Write(Stream output, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            headers = headers ?? new string[0];
            rows = rows ?? new string[0][];
            var result = new XlsxResult
            {
                Columns = Math.Min(headers.Count, MaxColumns),
                ColumnsTruncated = headers.Count > MaxColumns,
            };
            var dataRows = Math.Min(rows.Count, MaxRows - 1);
            result.Rows = dataRows;
            result.RowsTruncated = rows.Count > dataRows;
            var kinds = Enumerable.Range(0, result.Columns).Select(c => KindOf(rows, dataRows, c)).ToArray();
            var widths = Enumerable.Range(0, result.Columns).Select(c => WidthOf(headers[c], rows, dataRows, c)).ToArray();

            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteText(zip, "[Content_Types].xml", ContentTypes);
                WriteText(zip, "_rels/.rels", RootRelationships);
                WriteText(zip, "xl/workbook.xml", Workbook);
                WriteText(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
                WriteText(zip, "xl/styles.xml", Styles);
                var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
                using (var stream = entry.Open())
                using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false }))
                {
                    WriteSheet(xml, headers, rows, dataRows, kinds, widths, result);
                }
            }

            return result;
        }

        private static void WriteSheet(XmlWriter xml, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, int dataRows, ColumnKind[] kinds, int[] widths, XlsxResult result)
        {
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            xml.WriteStartDocument(true);
            xml.WriteStartElement("worksheet", ns);

            // The first row stays in view when scrolling.
            xml.WriteStartElement("sheetViews");
            xml.WriteStartElement("sheetView");
            xml.WriteAttributeString("workbookViewId", "0");
            xml.WriteStartElement("pane");
            xml.WriteAttributeString("ySplit", "1");
            xml.WriteAttributeString("topLeftCell", "A2");
            xml.WriteAttributeString("activePane", "bottomLeft");
            xml.WriteAttributeString("state", "frozen");
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();

            if (result.Columns > 0)
            {
                xml.WriteStartElement("cols");
                for (var c = 0; c < result.Columns; c++)
                {
                    xml.WriteStartElement("col");
                    xml.WriteAttributeString("min", (c + 1).ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("max", (c + 1).ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("width", widths[c].ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("customWidth", "1");
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }

            xml.WriteStartElement("sheetData");
            xml.WriteStartElement("row");
            xml.WriteAttributeString("r", "1");
            for (var c = 0; c < result.Columns; c++)
            {
                WriteText(xml, ColumnName(c) + "1", headers[c] ?? string.Empty, 1, result);
            }

            xml.WriteEndElement();
            for (var r = 0; r < dataRows; r++)
            {
                var row = rows[r];
                var rowNumber = (r + 2).ToString(CultureInfo.InvariantCulture);
                xml.WriteStartElement("row");
                xml.WriteAttributeString("r", rowNumber);
                for (var c = 0; c < result.Columns; c++)
                {
                    var value = row != null && c < row.Length ? row[c] : null;
                    if (IsEmpty(value))
                    {
                        continue;
                    }

                    WriteValue(xml, ColumnName(c) + rowNumber, value, kinds[c], result);
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        private static void WriteValue(XmlWriter xml, string reference, string value, ColumnKind kind, XlsxResult result)
        {
            switch (kind)
            {
                case ColumnKind.Integer:
                    WriteNumber(xml, reference, value, 2);
                    break;
                case ColumnKind.Decimal:
                    WriteNumber(xml, reference, value, 0);
                    break;
                case ColumnKind.Date:
                    WriteNumber(xml, reference, SerialOf(value), 3);
                    break;
                case ColumnKind.DateTime:
                    WriteNumber(xml, reference, SerialOf(value), 4);
                    break;
                case ColumnKind.Time:
                    WriteNumber(xml, reference, TimeOf(value), 5);
                    break;
                default:
                    WriteText(xml, reference, value, 0, result);
                    break;
            }
        }

        private static void WriteNumber(XmlWriter xml, string reference, string number, int style)
        {
            xml.WriteStartElement("c");
            xml.WriteAttributeString("r", reference);
            if (style != 0)
            {
                xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            }

            xml.WriteElementString("v", number);
            xml.WriteEndElement();
        }

        /// <summary>A text cell: an inline string, so it can never be read as a formula whatever it starts with.</summary>
        private static void WriteText(XmlWriter xml, string reference, string text, int style, XlsxResult result)
        {
            text = Clean(text);
            if (text.Length > MaxTextLength)
            {
                text = text.Substring(0, MaxTextLength);
                result.CutTexts++;
            }

            xml.WriteStartElement("c");
            xml.WriteAttributeString("r", reference);
            if (style != 0)
            {
                xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            }

            xml.WriteAttributeString("t", "inlineStr");
            xml.WriteStartElement("is");
            xml.WriteStartElement("t");
            xml.WriteAttributeString("xml", "space", null, "preserve");
            xml.WriteString(text);
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        /// <summary>The characters XML cannot hold (control characters other than tab, line feed and carriage return, lone surrogates) are dropped.</summary>
        private static string Clean(string text)
        {
            text = text ?? string.Empty;
            var clean = true;
            for (var i = 0; i < text.Length; i++)
            {
                if (!IsXmlChar(text, i))
                {
                    clean = false;
                    break;
                }
            }

            if (clean)
            {
                return text;
            }

            var result = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (IsXmlChar(text, i))
                {
                    result.Append(text[i]);
                }
            }

            return result.ToString();
        }

        private static bool IsXmlChar(string text, int i)
        {
            var c = text[i];
            if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD))
            {
                return true;
            }

            if (char.IsHighSurrogate(c))
            {
                return i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            }

            return char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1]);
        }

        private static bool IsEmpty(string value) => string.IsNullOrEmpty(value) || value == "NULL";

        /// <summary>The kind of a column: the one every value fits, the most general one otherwise (text).</summary>
        private static ColumnKind KindOf(IReadOnlyList<string[]> rows, int dataRows, int column)
        {
            var kind = (ColumnKind?)null;
            for (var r = 0; r < dataRows; r++)
            {
                var row = rows[r];
                var value = row != null && column < row.Length ? row[column] : null;
                if (IsEmpty(value))
                {
                    continue;
                }

                var own = KindOfValue(value);
                if (kind == null)
                {
                    kind = own;
                }
                else if (kind != own)
                {
                    // Whole numbers and decimals together are decimals; anything else mixed is text.
                    if ((kind == ColumnKind.Integer && own == ColumnKind.Decimal) || (kind == ColumnKind.Decimal && own == ColumnKind.Integer))
                    {
                        kind = ColumnKind.Decimal;
                    }
                    else
                    {
                        return ColumnKind.Text;
                    }
                }

                if (kind == ColumnKind.Text)
                {
                    return ColumnKind.Text;
                }
            }

            return kind ?? ColumnKind.Text;
        }

        private static ColumnKind KindOfValue(string value)
        {
            if (IntegerPattern.IsMatch(value))
            {
                return ColumnKind.Integer;
            }

            if (DecimalPattern.IsMatch(value) && SignificantDigits(value) <= 15)
            {
                return ColumnKind.Decimal;
            }

            if (DatePattern.IsMatch(value) && TryDate(value, out _))
            {
                return ColumnKind.Date;
            }

            if (DateTimePattern.IsMatch(value) && TryDate(value, out _))
            {
                return ColumnKind.DateTime;
            }

            return TimePattern.IsMatch(value) && TryTime(value, out _) ? ColumnKind.Time : ColumnKind.Text;
        }

        /// <summary>Excel keeps 15 significant digits; a number with more (a decimal(38,10), a long identifier) stays text, so that nothing is rounded.</summary>
        private static int SignificantDigits(string value) => value.Count(char.IsDigit) - (value.TrimStart('-').StartsWith("0.", StringComparison.Ordinal) ? 1 : 0);

        private static bool TryDate(string value, out DateTime date)
        {
            var normalized = value.Replace('T', ' ');
            var formats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF" };
            return DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date >= FirstExcelDate;
        }

        private static bool TryTime(string value, out TimeSpan time) =>
            TimeSpan.TryParseExact(value, new[] { @"hh\:mm\:ss", @"hh\:mm\:ss\.FFFFFFF" }, CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);

        private static string SerialOf(string value)
        {
            TryDate(value, out var date);
            return (date - Epoch).TotalDays.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        private static string TimeOf(string value)
        {
            TryTime(value, out var time);
            return time.TotalDays.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        /// <summary>The width of a column in characters: the longest of the first rows and the name, between 8 and 60.</summary>
        private static int WidthOf(string header, IReadOnlyList<string[]> rows, int dataRows, int column)
        {
            var longest = (header ?? string.Empty).Length;
            for (var r = 0; r < Math.Min(dataRows, 200); r++)
            {
                var row = rows[r];
                if (row != null && column < row.Length && row[column] != null)
                {
                    longest = Math.Max(longest, row[column].Length);
                }
            }

            return Math.Max(8, Math.Min(60, longest + 2));
        }

        /// <summary>A, B, ... Z, AA, ... for a zero based column.</summary>
        internal static string ColumnName(int column)
        {
            var name = new StringBuilder();
            for (var n = column + 1; n > 0; n = (n - 1) / 26)
            {
                name.Insert(0, (char)('A' + (n - 1) % 26));
            }

            return name.ToString();
        }

        private static void WriteText(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private const string ContentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";

        private const string RootRelationships =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private const string Workbook =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

        private const string WorkbookRelationships =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        // Styles: 0 general, 1 bold (the header), 2 whole number, 3 date, 4 date and time, 5 time.
        private const string Styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/><numFmt numFmtId=\"165\" formatCode=\"yyyy\\-mm\\-dd\\ hh:mm:ss\"/><numFmt numFmtId=\"166\" formatCode=\"hh:mm:ss\"/></numFmts>" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"6\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";
    }
}
