using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfDateContent
    {
        [Theory]
        [InlineData("OneBookOneSheetOneDateColumn.opju", new double[] { })]
        [InlineData("OneBookOneSheetOneDateColumn_Row0.opju", new double[] { 2461360.967060185185 })]
        [InlineData("OneBookOneSheetOneDateColumn_Row1.opju", new double[] { double.NaN, 2461360.967060185185 })]
        [InlineData("OneBookOneSheetOneDateColumn_Row2.opju", new double[] { double.NaN, double.NaN, 2461360.967060185185 })]
        [InlineData("OneBookOneSheetOneDateColumn_Row0To1_3To4.opju", new double[] { 2440587, 2444239, double.NaN, 2451544, 2458849 })]
        [InlineData("OneBookOneSheetOneDateColumn_Row0To8.opju", new double[] { 2441186.65231481481481481481481481481481, 2418065.52612268518518518518518518518519, 2451430.9999884259259259, 2444299.1461805555555, 2435588.59321759259259259, 2463079.5, 2395183.1299652777777777, 2439282.25423611111111111, 2432779.50888888888888888 })]
        public void ExpectContent(string shortFileName, double[] expectedValues)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "DateContent", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books);
            var book = parser.Books[0];
            Assert.Equal("M", book.Name);
            Assert.Equal("Ößberg", book.LongName);
            Assert.Equal("Krüger", book.FolderName);

            Assert.Single(book.Sheets); // with one sheet inside
            var sheet = book.Sheets[0];
            Assert.Equal("Übergang", sheet.Name);

            Assert.Single(sheet.Columns); // with the expected number of columns
            var column = sheet.Columns[0];

            Assert.Equal("A", column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
            Assert.Equal(ValueType.Date, column.ValueType); // the expected column types are checked
            Assert.Equal(SpreadColumnType.Y, column.ColumnType);
            Assert.Equal(23.5, column.Width, 1E-6); // the columns width is expected to be 23.5 for all columns
            Assert.Equal("DateColumn", column.LongName);
            Assert.Equal("Köln", column.Unit);
            Assert.Equal("Äppel", column.Comment);

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
