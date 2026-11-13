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

            Assert.Single(parser.Books); // there is only one Book
            Assert.Equal("Book1", parser.Books[0].Name); // named 'Book1'
            Assert.Single(parser.Books[0].Sheets); // with one sheet inside
            Assert.Equal(2, parser.Books[0].Sheets[0].Columns.Count); // with the expected number of columns

            for (int iColumn = 0; iColumn < 2; iColumn++)
            {
                var column = parser.Books[0].Sheets[0].Columns[iColumn];
                Assert.Equal("" + (char)('A' + iColumn), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                Assert.Equal(ValueType.Text, column.ValueType); // the expected column types are checked


                Assert.NotNull(column.Values);
                Assert.True(column.Values.Count >= 30);
                for (int j = 0; j < rows.Length; j++)
                {
                    if (rows[j] >= 0)
                        Assert.True(rows[j] < column.Values.Count);
                }
                for (int row = 0; row < Math.Min(39, column.Values.Count); row++)
                {
                    int valueIndex = Array.IndexOf(rows, row);
                    var expectedValue = valueIndex < 0 ? string.Empty : (iColumn == 0 ? cA[valueIndex] : cB[valueIndex]);
                    Assert.Equal(expectedValue, column.Values[row].AsString());
                }
            }
        }
    }
}
