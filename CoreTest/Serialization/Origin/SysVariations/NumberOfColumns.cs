using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class NumberOfColumns
    {
        [Theory]
        [InlineData("OneBookOneSheetOneDoubleColumn.opju", 1)]
        [InlineData("OneBookOneSheetTwoDoubleColumns.opju", 2)]
        [InlineData("OneBookOneSheetThreeDoubleColumns.opju", 3)]
        [InlineData("OneBookOneSheetFourDoubleColumns.opju", 4)]
        [InlineData("OneBookOneSheetNineDoubleColumns.opju", 9)]
        public void ExpectRightNumberOfColumns(string shortFileName, int expectedNumberOfColumns)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumberOfColumns", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books); // there is only one Book
            Assert.Equal("Book1", parser.Books[0].Name); // named 'Book1'
            Assert.Single(parser.Books[0].Sheets); // with one sheet inside
            Assert.Equal(expectedNumberOfColumns, parser.Books[0].Sheets[0].Columns.Count); // with the expected number of columns
            for (int i = 0; i < expectedNumberOfColumns; i++)
            {
                var column = parser.Books[0].Sheets[0].Columns[i];
                Assert.Equal("" + (char)('A' + i), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                Assert.True(column.Values.Count <= 39); // maximum number of rows in the test files is 39, so the number of values in each column should be less than or equal to 39
                for (int j = 0; j < column.Values.Count; j++)
                {
                    Assert.True(double.IsNaN(column.Values[j].AsDouble())); // we have no values, so all values should be NaN
                }
            }
        }
    }
}
