using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfValueType
    {
        [Theory]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_Binary_Numeric.opju", ValueType.Numeric, ValueType.Binary, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_Color_Numeric.opju", ValueType.Numeric, ValueType.Color, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_Date_Numeric.opju", ValueType.Numeric, ValueType.Date, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_Day_Numeric.opju", ValueType.Numeric, ValueType.Day, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_Month_Numeric.opju", ValueType.Numeric, ValueType.Month, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_Numeric_Numeric.opju", ValueType.Numeric, ValueType.Numeric, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_Text_Numeric.opju", ValueType.Numeric, ValueType.Text, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_TextNumeric_Numeric.opju", ValueType.Numeric, ValueType.TextNumeric, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Numeric_Time_Numeric.opju", ValueType.Numeric, ValueType.Time, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Text_Numeric_Numeric.opju", ValueType.Text, ValueType.Numeric, ValueType.Numeric)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_TextNumeric_Numeric_Numeric.opju", ValueType.TextNumeric, ValueType.Numeric, ValueType.Numeric)]
        public void ExpectValueTypes(string shortFileName, ValueType expectedColumnType1, ValueType expectedColumnType2, ValueType expectedColumnType3)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "ValueType", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books); // there is only one Book
            Assert.Equal("Book1", parser.Books[0].Name); // named 'Book1'
            Assert.Single(parser.Books[0].Sheets); // with one sheet inside
            Assert.Equal(3, parser.Books[0].Sheets[0].Columns.Count); // with the expected number of columns

            for (int i = 0; i < 3; i++)
            {
                var column = parser.Books[0].Sheets[0].Columns[i];
                Assert.Equal("" + (char)('A' + i), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                var expectedColumnType = i switch
                {
                    0 => expectedColumnType1,
                    1 => expectedColumnType2,
                    2 => expectedColumnType3,
                    _ => throw new InvalidOperationException("Unexpected column index")
                };

                Assert.Equal(expectedColumnType, column.ValueType); // the expected column types are checked
            }
        }
    }
}
