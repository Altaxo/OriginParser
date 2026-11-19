using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfIndexOfXColumn
    {
        [Theory]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtA.opju", 0)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtB.opju", 1)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtC.opju", 2)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtD.opju", 3)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtE.opju", 4)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtF.opju", 5)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtG.opju", 6)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtH.opju", 7)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtI.opju", 8)]
        [InlineData("OneBookOneSheetNineDoubleColumnsXAtA_Again.opju", 0)]
        public void ExpectIndexOfXColumn(string shortFileName, int expectedIndexOfXColumn)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "IndexOfXColumn", shortFileName);
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
            Assert.Equal(9, sheet.Columns.Count); // with the expected number of columns

            for (int i = 0; i < 9; i++)
            {
                var column = sheet.Columns[i];
                Assert.Equal("" + (char)('A' + i), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                Assert.Equal(ValueType.Numeric, column.ValueType);
                Assert.Equal(DeserializedDataType.Double, column.DeserializedDataType);
                Assert.Equal(6.7, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
                Assert.Empty(column.LongName);
                Assert.Empty(column.Unit);
                Assert.Empty(column.Comment);
                Assert.True(column.Data.Count <= 39); // maximum number of rows in the test files is 39, so the number of values in each column should be less than or equal to 39
                for (int j = 0; j < column.Data.Count; j++)
                {
                    Assert.True(column.Data[j].IsCellEmpty);
                }
                Assert.Equal(i == expectedIndexOfXColumn ? SpreadColumnType.X : SpreadColumnType.Y, column.ColumnType); // the expected column is the X column, all others are ignored
            }
        }
    }
}
