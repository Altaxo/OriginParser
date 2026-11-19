using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfTextContent
    {
        [Theory]
        [InlineData("OneBookOneSheetTwoTextColumns.opju", new int[] { -1, -1, -1, -1 })]
        [InlineData("OneBookOneSheetTwoTextColumns_2RowsAt0_2RowsAt37.opju", new int[] { 0, 1, 37, 38 })]
        [InlineData("OneBookOneSheetTwoTextColumns_4ConsecutiveRowsStartingAtRow0.opju", new int[] { 0, 1, 2, 3 })]
        [InlineData("OneBookOneSheetTwoTextColumns_4ConsecutiveRowsStartingAtRow11.opju", new int[] { 11, 12, 13, 14 })]
        [InlineData("OneBookOneSheetTwoTextColumns_4ConsecutiveRowsStartingAtRow35.opju", new int[] { 35, 36, 37, 38 })]
        [InlineData("OneBookOneSheetTwoTextColumns_Rows_2_10_22_37.opju", new int[] { 2, 10, 22, 37 })]
        public void ExpectTextContent(string shortFileName, int[] rows)
        {
            IReadOnlyList<string> cA = new List<string> { "Müller", "Meier", "Schultze", "Lampe" };
            IReadOnlyList<string> cB = new List<string> { "Öhrig", "A very long text with spaces inbetween", "Text with the special chars !?|µ€", "Greek characters: Ταξ" };


            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "TextContent", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books);
            var book = parser.Books[0];
            Assert.Equal("Book1", book.Name);
            Assert.Equal("Ößberg", book.LongName);
            Assert.Equal("Meßfolder", book.FolderName);

            Assert.Single(book.Sheets); // with one sheet inside
            var sheet = book.Sheets[0];
            Assert.Equal("Äalesund", sheet.Name);

            Assert.NotNull(sheet.Columns); // with the expected number of columns
            Assert.Equal(2, sheet.Columns.Count);

            for (int iColumn = 0; iColumn < 2; iColumn++)
            {
                var column = sheet.Columns[iColumn];
                Assert.Equal("" + (char)('A' + iColumn), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                Assert.Equal(ValueType.Text, column.ValueType); // the expected column types are checked
                Assert.Equal(iColumn == 0 ? SpreadColumnType.X : SpreadColumnType.Y, column.ColumnType); // the expected column types are checked

                Assert.Equal(iColumn == 0 ? 16.5 : 31.5, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
                Assert.Empty(column.LongName);
                Assert.Empty(column.Unit);
                Assert.Empty(column.Comment);

                Assert.NotNull(column.Data);
                Assert.True(column.Data.Count >= 30);
                for (int j = 0; j < rows.Length; j++)
                {
                    if (rows[j] >= 0)
                        Assert.True(rows[j] < column.Data.Count);
                }
                for (int row = 0; row < Math.Min(39, column.Data.Count); row++)
                {
                    int valueIndex = Array.IndexOf(rows, row);
                    var expectedValue = valueIndex < 0 ? string.Empty : (iColumn == 0 ? cA[valueIndex] : cB[valueIndex]);
                    Assert.Equal(expectedValue, column.Data[row].AsString());
                }
            }
        }
    }
}
