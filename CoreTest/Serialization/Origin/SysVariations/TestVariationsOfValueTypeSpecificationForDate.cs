using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfValueTypeSpecificationForDate
    {
        [Theory]
        [InlineData("OneBookOneSheetOneDateColumn_DD_MM_YYYY_HH_MM_SS.opju", ValueTypeSpecification.Date_DD_MM_YYYY_HH_MM_SS)]
        [InlineData("OneBookOneSheetOneDateColumn_DD_MM_YYYY.opju", ValueTypeSpecification.Date_DD_MM_YYYY)]
        // [InlineData("OneBookOneSheetOneDateColumn_YYMMDD.opju", ValueTypeSpecification.Date_YYMMDD)]
        public void ExpectValueTypeSpecification(string shortFileName, ValueTypeSpecification expectedValueTypeSpecification)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "ValueTypeSpecificationForDate", shortFileName);
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
            Assert.Equal(expectedValueTypeSpecification, column.ValueTypeSpecification);
            Assert.Equal(SpreadColumnType.Y, column.ColumnType);
            Assert.Equal(23.5, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
            Assert.Equal("DateColumn", column.LongName);
            Assert.Equal("Köln", column.Unit);
            Assert.Equal("Äppel", column.Comment);

            // now check the values
            var expectedValues = new string[] {
                "1971/08/23 15:39:20",
                "1908/05/04 12:37:37",
                "1999/09/09 23:59:59",
                "1980/03/01 03:30:30",
                "1956/04/25 14:14:14",
                "2031/08/01 12:00:00",
                "1845/09/09 03:07:09",
                "1966/06/06 06:06:06",
                "1948/08/16 12:12:48" };


            Assert.NotNull(column.Data);
            Assert.True(column.Data.Count >= expectedValues.Length);
            for (int i = 0; i < expectedValues.Length; i++)
            {
                var expectedDateValue = DateTime.Parse(expectedValues[i], System.Globalization.CultureInfo.InvariantCulture);
                var reference = DateTime.Parse("1970/01/01 00:00:00", System.Globalization.CultureInfo.InvariantCulture);

                var expectedValue = 2440587 + (expectedDateValue - reference).TotalDays;
                var actualValue = column.Data[i];

                var diff = actualValue - expectedValue;

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
