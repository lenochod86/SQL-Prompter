using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    /// <summary>The Open XML writer of Open in Excel (#445), checked by reading the generated file back.</summary>
    public class XlsxWriterTests
    {
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        private sealed class Cell
        {
            public string Reference;
            public string Type;
            public string Style;
            public string Value;
        }

        private sealed class Sheet
        {
            public ZipArchive Zip;
            public List<List<Cell>> Rows = new List<List<Cell>>();
            public XDocument Document;
        }

        private static Sheet Generate(string[] headers, params string[][] rows) => Generate(out _, headers, rows);

        private static Sheet Generate(out XlsxResult result, string[] headers, string[][] rows)
        {
            var stream = new MemoryStream();
            result = XlsxWriter.Write(stream, headers, rows);
            stream.Position = 0;
            var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var sheet = new Sheet { Zip = zip };
            using (var entry = zip.GetEntry("xl/worksheets/sheet1.xml").Open())
            {
                sheet.Document = XDocument.Load(entry);
            }

            foreach (var row in sheet.Document.Descendants(Main + "row"))
            {
                sheet.Rows.Add(row.Elements(Main + "c").Select(c => new Cell
                {
                    Reference = (string)c.Attribute("r"),
                    Type = (string)c.Attribute("t"),
                    Style = (string)c.Attribute("s"),
                    Value = c.Element(Main + "v")?.Value ?? c.Element(Main + "is")?.Element(Main + "t")?.Value,
                }).ToList());
            }

            return sheet;
        }

        [Fact]
        public void TheFileIsAValidPackageWithTheRequiredParts()
        {
            var sheet = Generate(new[] { "a" }, new[] { "1" });

            foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml", "xl/styles.xml" })
            {
                var entry = sheet.Zip.GetEntry(part);
                Assert.NotNull(entry);
                using (var stream = entry.Open())
                {
                    XDocument.Load(stream);
                }
            }

            using (var workbook = sheet.Zip.GetEntry("xl/workbook.xml").Open())
            {
                Assert.Equal("Sheet1", (string)XDocument.Load(workbook).Descendants(Main + "sheet").Single().Attribute("name"));
            }
        }

        [Fact]
        public void TheHeaderRowHoldsTheNamesAsBoldText()
        {
            var sheet = Generate(new[] { "Name", "Total" }, new[] { "x", "1" });

            var header = sheet.Rows[0];
            Assert.Equal(new[] { "Name", "Total" }, header.Select(c => c.Value).ToArray());
            Assert.All(header, c => Assert.Equal("inlineStr", c.Type));
            Assert.All(header, c => Assert.Equal("1", c.Style));
            Assert.Equal(new[] { "A1", "B1" }, header.Select(c => c.Reference).ToArray());
        }

        [Fact]
        public void NumbersKeepTheirFullPrecisionAsNumericCells()
        {
            var sheet = Generate(new[] { "Id", "Price", "Mixed" }, new[] { "7", "742.5", "1" }, new[] { "-12", "4083.75", "2.50" }, new[] { "0", "0.125", "3" });

            var first = sheet.Rows[1];
            Assert.Null(first[0].Type);
            Assert.Equal("7", first[0].Value);
            Assert.Equal("2", first[0].Style);
            Assert.Equal("742.5", first[1].Value);
            Assert.Null(first[1].Type);
            Assert.Null(first[1].Style);
            Assert.Equal("4083.75", sheet.Rows[2][1].Value);
            Assert.Equal("0.125", sheet.Rows[3][1].Value);

            // Whole numbers and decimals in one column: numeric, not text.
            Assert.All(sheet.Rows.Skip(1), r => Assert.Null(r[2].Type));
            Assert.Equal("2.50", sheet.Rows[2][2].Value);
        }

        [Fact]
        public void NullIsAnEmptyCell()
        {
            var sheet = Generate(new[] { "A", "B", "C" }, new[] { "1", "NULL", "x" });

            Assert.Equal(new[] { "A2", "C2" }, sheet.Rows[1].Select(c => c.Reference).ToArray());
        }

        [Fact]
        public void ARowShorterThanTheHeaderIsPadded()
        {
            var sheet = Generate(new[] { "A", "B", "C" }, new[] { "1" });

            Assert.Single(sheet.Rows[1]);
        }

        [Fact]
        public void TextIsNeverAFormula()
        {
            var sheet = Generate(new[] { "T" }, new[] { "=1+1" }, new[] { "+cmd" }, new[] { "-2x" }, new[] { "@SUM(A1)" });

            Assert.All(sheet.Rows.Skip(1), r => Assert.Equal("inlineStr", r[0].Type));
            Assert.Equal(new[] { "=1+1", "+cmd", "-2x", "@SUM(A1)" }, sheet.Rows.Skip(1).Select(r => r[0].Value).ToArray());
            Assert.Empty(sheet.Document.Descendants(Main + "f"));
        }

        [Fact]
        public void XmlCharactersAreEscapedAndInvalidOnesDropped()
        {
            var sheet = Generate(new[] { "T" }, new[] { "a < b & \"c\" > 'd'" }, new[] { "bad\u0001char" }, new[] { "line1\r\nline2\ttab" });

            Assert.Equal("a < b & \"c\" > 'd'", sheet.Rows[1][0].Value);
            Assert.Equal("badchar", sheet.Rows[2][0].Value);
            Assert.Equal("line1\nline2\ttab", sheet.Rows[3][0].Value); // an XML reader folds CR LF into LF
        }

        [Fact]
        public void LeadingAndTrailingSpacesSurvive()
        {
            Assert.Equal("  x  ", Generate(new[] { "T" }, new[] { "  x  " }).Rows[1][0].Value);
        }

        [Fact]
        public void NumbersWithLeadingZerosOrTooManyDigitsStayText()
        {
            var sheet = Generate(new[] { "Zip", "Big", "Decimal" }, new[] { "00123", "12345678901234567", "1234567890.123456789" });

            Assert.All(sheet.Rows[1], c => Assert.Equal("inlineStr", c.Type));
            Assert.Equal("00123", sheet.Rows[1][0].Value);
        }

        [Fact]
        public void DatesBecomeExcelDatesWithADateFormat()
        {
            var sheet = Generate(new[] { "D", "DT", "T" }, new[] { "2026-11-12", "2026-11-12 12:30:00.000", "12:30:00" });

            var row = sheet.Rows[1];
            Assert.Null(row[0].Type);
            Assert.Equal("3", row[0].Style);
            Assert.Equal("46338", row[0].Value);
            Assert.Equal("4", row[1].Style);
            Assert.StartsWith("46338.5208333", row[1].Value);
            Assert.Equal("5", row[2].Style);
            Assert.StartsWith("0.52083", row[2].Value);
        }

        [Fact]
        public void ADateBeforeWhatExcelCanShowStaysText()
        {
            var sheet = Generate(new[] { "D" }, new[] { "0001-01-01" });

            Assert.Equal("inlineStr", sheet.Rows[1][0].Type);
            Assert.Equal("0001-01-01", sheet.Rows[1][0].Value);
        }

        [Fact]
        public void AColumnOfMixedKindsIsText()
        {
            var sheet = Generate(new[] { "M" }, new[] { "12" }, new[] { "abc" }, new[] { "2026-01-01" });

            Assert.All(sheet.Rows.Skip(1), r => Assert.Equal("inlineStr", r[0].Type));
            Assert.Equal("12", sheet.Rows[1][0].Value);
        }

        [Fact]
        public void GuidsXmlAndBinaryAreText()
        {
            var sheet = Generate(new[] { "G", "X", "B" }, new[] { "6F9619FF-8B86-D011-B42D-00C04FC964FF", "<a>1</a>", "0x00FF" });

            Assert.All(sheet.Rows[1], c => Assert.Equal("inlineStr", c.Type));
        }

        [Fact]
        public void AVeryLongTextIsCutAndCounted()
        {
            var sheet = Generate(out var result, new[] { "T" }, new[] { new[] { new string('x', 40000) } });

            Assert.Equal(XlsxWriter.MaxTextLength, sheet.Rows[1][0].Value.Length);
            Assert.Equal(1, result.CutTexts);
        }

        [Fact]
        public void MoreRowsThanASheetHoldsAreCutAndReported()
        {
            var rows = Enumerable.Range(0, XlsxWriter.MaxRows + 5).Select(i => new[] { "1" }).ToArray();

            Generate(out var result, new[] { "N" }, rows);

            Assert.True(result.RowsTruncated);
            Assert.Equal(XlsxWriter.MaxRows - 1, result.Rows);
        }

        [Fact]
        public void MoreColumnsThanASheetHoldsAreCutAndReported()
        {
            var headers = Enumerable.Range(0, XlsxWriter.MaxColumns + 3).Select(i => "c" + i).ToArray();

            Generate(out var result, headers, new[] { new[] { "1" } });

            Assert.True(result.ColumnsTruncated);
            Assert.Equal(XlsxWriter.MaxColumns, result.Columns);
        }

        [Theory]
        [InlineData(0, "A")]
        [InlineData(25, "Z")]
        [InlineData(26, "AA")]
        [InlineData(701, "ZZ")]
        [InlineData(702, "AAA")]
        [InlineData(16383, "XFD")]
        public void ColumnNamesAreLetters(int column, string expected) => Assert.Equal(expected, XlsxWriter.ColumnName(column));

        [Fact]
        public void NoColumnsGivesAValidEmptySheet()
        {
            var sheet = Generate(new string[0], new string[0][]);

            Assert.Single(sheet.Rows);
            Assert.Empty(sheet.Rows[0]);
        }

        [Fact]
        public void TheGridTextIsSplitIntoNamesAndRows()
        {
            Assert.True(GridExcel.TryParse("a\tb\r\n1\tNULL\r\n2\tx\r\n", out var headers, out var rows));

            Assert.Equal(new[] { "a", "b" }, headers.ToArray());
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "2", "x" }, rows[1]);
            Assert.False(GridExcel.TryParse("only a header", out _, out _));
            Assert.False(GridExcel.TryParse(null, out _, out _));
        }

        [Fact]
        public void ANewFileNeverOverwritesAnExistingOne()
        {
            var folder = Path.Combine(Path.GetTempPath(), "sqlprompter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var now = new DateTime(2026, 11, 12, 12, 20, 52);
                var first = GridExcel.NewPath(folder, now);
                File.WriteAllText(first, "x");
                var second = GridExcel.NewPath(folder, now);
                File.WriteAllText(second, "x");

                Assert.Equal("Results_2026-11-12_12-20-52.xlsx", Path.GetFileName(first));
                Assert.Equal("Results_2026-11-12_12-20-52_2.xlsx", Path.GetFileName(second));
                Assert.Equal("Results_2026-11-12_12-20-52_3.xlsx", Path.GetFileName(GridExcel.NewPath(folder, now)));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void FilesOlderThanADayAreDeletedAndLockedOnesAreLeft()
        {
            var folder = Path.Combine(Path.GetTempPath(), "sqlprompter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var now = DateTime.Now;
                var old = Path.Combine(folder, "Results_old.xlsx");
                var recent = Path.Combine(folder, "Results_recent.xlsx");
                var locked = Path.Combine(folder, "Results_locked.xlsx");
                var other = Path.Combine(folder, "notes.txt");
                foreach (var path in new[] { old, recent, locked, other })
                {
                    File.WriteAllText(path, "x");
                }

                File.SetLastWriteTime(old, now.AddDays(-2));
                File.SetLastWriteTime(locked, now.AddDays(-2));
                File.SetLastWriteTime(other, now.AddDays(-2));
                // Only Windows refuses to delete a file that another program has open; elsewhere it is deleted like the old one.
                var windows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.Equal(windows ? 1 : 2, GridExcel.Cleanup(folder, now));
                }

                Assert.False(File.Exists(old));
                Assert.True(File.Exists(recent));
                Assert.Equal(windows, File.Exists(locked));
                Assert.True(File.Exists(other));
                Assert.Equal(0, GridExcel.Cleanup(Path.Combine(folder, "missing"), now));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
