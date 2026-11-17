using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfNumberOfColumns
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
            var book = parser.Books[0];
            Assert.Equal("Book1", book.Name); // named 'Book1'
            Assert.Equal("Ößberg", book.LongName);
            Assert.Equal("Meßfolder", book.FolderName);
            Assert.Single(book.Sheets); // with one sheet inside
            var sheet = book.Sheets[0];
            Assert.Equal("Äalesund", sheet.Name);
            Assert.Equal(expectedNumberOfColumns, sheet.Columns.Count); // with the expected number of columns
            for (int i = 0; i < expectedNumberOfColumns; i++)
            {
                var column = sheet.Columns[i];
                Assert.Equal("" + (char)('A' + i), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                Assert.Equal(ValueType.Numeric, column.ValueType);
                Assert.Equal(DeserializedDataType.Double, column.DeserializedDataType);
                Assert.Equal(SpreadColumnType.Y, column.SpreadColumnType); // the expected column is the X column, all others are ignored
                Assert.Equal(6.7, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
                Assert.Empty(column.LongName);
                Assert.Empty(column.Unit);
                Assert.Empty(column.Comment);
                Assert.True(column.Values.Count <= 39); // maximum number of rows in the test files is 39, so the number of values in each column should be less than or equal to 39
                for (int j = 0; j < column.Values.Count; j++)
                {
                    Assert.True(double.IsNaN(column.Values[j].AsDouble())); // we have no values, so all values should be NaN
                }
            }
        }
    }
}
