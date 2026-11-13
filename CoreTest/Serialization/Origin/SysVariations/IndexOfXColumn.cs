using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class IndexOfXColumn
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
            Assert.Equal("Book1", parser.Books[0].Name); // named 'Book1'
            Assert.Single(parser.Books[0].Sheets); // with one sheet inside
            Assert.Equal(9, parser.Books[0].Sheets[0].Columns.Count); // with the expected number of columns

            for (int i = 0; i < 9; i++)
            {
                var column = parser.Books[0].Sheets[0].Columns[i];
                Assert.Equal("" + (char)('A' + i), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                Assert.Equal(i == expectedIndexOfXColumn ? SpreadColumnType.X : SpreadColumnType.Y, column.SpreadColumnType); // the expected column is the X column, all others are ignored
            }
        }
    }
}
