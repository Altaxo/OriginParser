using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfTimeContent
    {
        [Theory]
        [InlineData("OneBookOneSheetOneTimeColumn.opju", new double[] { })]
        [InlineData("OneBookOneSheetOneTimeColumn_Row0.opju", new double[] { 0.583172719907407407407407407407 })]
        [InlineData("OneBookOneSheetOneTimeColumn_Row1.opju", new double[] { double.NaN, 0.583172719907407407407407407407 })]
        [InlineData("OneBookOneSheetOneTimeColumn_Row2.opju", new double[] { double.NaN, double.NaN, 0.583172719907407407407407407407 })]
        [InlineData("OneBookOneSheetOneTimeColumn_Row0To1_3To4.opju", new double[] { 0.55156436342592592592592592592593, 0.98958475694444444444444444444444, double.NaN, 0.04309079861111111111111111111111, 0.08546952546296296296296296296296 })]
        // Values are day fractions. The content of the last row is 99:00:00.123 (99 hours, 0 minutes, 0 seconds, 123 milliseconds), i.e. (99*3600 + 0.123) seconds of a day.
        [InlineData("OneBookOneSheetOneTimeColumn_Row0To8.opju", new double[] { 0.0, 1 / 24d, 2 / 24d, 3 / 24d, 4 / 24d, (86400d - 0.001) / 86400d, 1.0, 1 + 1 / 24d, (99 * 3600 + 0.123) / 86400d })]
        public void ExpectContent(string shortFileName, double[] expectedValues)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "TimeContent", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books);
            var book = parser.Books[0];
            Assert.Equal("BisonRoundAbo", book.Name);
            Assert.Equal("Ößberg", book.LongName);
            Assert.Equal("Krüger", book.FolderName);

            Assert.Single(book.Sheets); // with one sheet inside
            var sheet = book.Sheets[0];
            Assert.Equal("Äalesund", sheet.Name);

            Assert.Single(sheet.Columns); // with the expected number of columns
            var column = sheet.Columns[0];

            Assert.Equal("A", column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
            Assert.Equal(ValueType.Time, column.ValueType); // the expected column types are checked
            Assert.Equal(SpreadColumnType.Y, column.ColumnType);
            Assert.Equal(17.7, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
            Assert.Equal("TimeColumn", column.LongName);
            Assert.Equal("h", column.Unit);
            Assert.Equal("ÖÜÄ", column.Comment);

            // now check the values

            if (expectedValues.Length > 0)
            {
                Assert.NotNull(column.Data);
            }

            if (column.Data is not null)
            {

                for (int i = 0; i < expectedValues.Length; i++)
                {
                    var expectedValue = expectedValues[i];
                    var actualValue = column.Data[i];
                    if (double.IsNaN(expectedValue))
                    {
                        Assert.True(actualValue.IsCellEmpty);
                    }
                    else
                    {
                        Assert.Equal(expectedValue, actualValue, 14);
                    }
                }

                for (int i = expectedValues.Length; i < column.Data.Count; i++)
                {
                    var actualValue = column.Data[i];
                    Assert.True(actualValue.IsCellEmpty);
                }
            }
        }
    }
}
