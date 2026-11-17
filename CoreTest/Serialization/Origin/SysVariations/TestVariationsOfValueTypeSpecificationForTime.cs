using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfValueTypeSpecificationForTime
    {
        [Theory]
        [InlineData("OneBookOneSheetOneTimeColumn_HH_MM.opju", ValueTypeSpecification.Time_HH_MM)]
        [InlineData("OneBookOneSheetOneTimeColumn_HH.opju", ValueTypeSpecification.Time_HH)]
        [InlineData("OneBookOneSheetOneTimeColumn_HH_MM_SS.opju", ValueTypeSpecification.Time_HH_MM_SS)]
        [InlineData("OneBookOneSheetOneTimeColumn_HH_MM_SS_ZZ.opju", ValueTypeSpecification.Time_HH_MM_SS_ZZ)]
        [InlineData("OneBookOneSheetOneTimeColumn_HH_AP.opju", ValueTypeSpecification.Time_HH_AP)]
        [InlineData("OneBookOneSheetOneTimeColumn_HH_MM_AP.opju", ValueTypeSpecification.Time_HH_MM_AP)]
        [InlineData("OneBookOneSheetOneTimeColumn_MM_SS.opju", ValueTypeSpecification.Time_MM_SS)]
        [InlineData("OneBookOneSheetOneTimeColumn_MM_SS_ZZ.opju", ValueTypeSpecification.Time_MM_SS_ZZ)]
        [InlineData("OneBookOneSheetOneTimeColumn_HHMM.opju", ValueTypeSpecification.Time_HHMM)]
        [InlineData("OneBookOneSheetOneTimeColumn_HHMMSS.opju", ValueTypeSpecification.Time_HHMMSS)]
        [InlineData("OneBookOneSheetOneTimeColumn_HH_MM_SS_ZZZ.opju", ValueTypeSpecification.Time_HH_MM_SS_ZZZ)]
        [InlineData("OneBookOneSheetOneTimeColumn_DDD.opju", ValueTypeSpecification.Time_DDD)]
        [InlineData("OneBookOneSheetOneTimeColumn_DDD_HH.opju", ValueTypeSpecification.Time_DDD_HH)]
        [InlineData("OneBookOneSheetOneTimeColumn_DDD_HH_MM.opju", ValueTypeSpecification.Time_DDD_HH_MM)]
        [InlineData("OneBookOneSheetOneTimeColumn_DDD_HH_MM_SS.opju", ValueTypeSpecification.Time_DDD_HH_MM_SS)]
        [InlineData("OneBookOneSheetOneTimeColumn_DDD_HH_MM_SS_ZZ.opju", ValueTypeSpecification.Time_DDD_HH_MM_SS_ZZ)]
        [InlineData("OneBookOneSheetOneTimeColumn_DDD_HH_MM_SS_ZZZZZZ.opju", ValueTypeSpecification.Time_DDD_HH_MM_SS_ZZZZZZ)]
        [InlineData("OneBookOneSheetOneTimeColumn_Custom1.opju", ValueTypeSpecification.Time_Custom1)]
        public void ExpectValueTypeSpecification(string shortFileName, ValueTypeSpecification expectedValueTypeSpecification)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "ValueTypeSpecificationForTime", shortFileName);
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
            Assert.Equal(expectedValueTypeSpecification, column.ValueTypeSpecification);
            Assert.Equal(SpreadColumnType.Y, column.SpreadColumnType);
            Assert.Equal(17.7, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
            Assert.Equal("TimeColumn", column.LongName);
            Assert.Equal("h", column.Unit);
            Assert.Equal("ÖÜÄ", column.Comment);

            // now check the values
            var expectedValues = new double[] { 0.0, 1 / 24d, 2 / 24d, 3 / 24d, 4 / 24d, (86400d - 0.001) / 86400d, 1.0, 1 + 1 / 24d, (99 * 3600 + 0.123) / 86400d };
            Assert.NotNull(column.Values);
            Assert.True(column.Values.Count >= expectedValues.Length);
            for (int i = 0; i < expectedValues.Length; i++)
            {
                var expectedValue = expectedValues[i];
                var actualValue = column.Values[i];
                if (double.IsNaN(expectedValue))
                {
                    Assert.True(actualValue.IsCellEmpty);
                }
                else
                {
                    Assert.Equal(expectedValue, actualValue, 14);
                }
            }

            for (int i = expectedValues.Length; i < column.Values.Count; i++)
            {
                var actualValue = column.Values[i];
                Assert.True(actualValue.IsCellEmpty);
            }
        }
    }
}
