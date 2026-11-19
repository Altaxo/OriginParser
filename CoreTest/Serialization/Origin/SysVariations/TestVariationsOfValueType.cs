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

            Assert.Single(parser.Books);
            var book = parser.Books[0];
            Assert.Equal("Book1", book.Name);
            Assert.Equal("Ößberg", book.LongName);
            Assert.Equal("Meßfolder", book.FolderName);

            Assert.Single(book.Sheets); // with one sheet inside
            var sheet = book.Sheets[0];
            Assert.Equal("Äalesund", sheet.Name);

            Assert.NotNull(sheet.Columns); // with the expected number of columns
            Assert.Equal(3, sheet.Columns.Count);

            for (int i = 0; i < 3; i++)
            {
                var column = sheet.Columns[i];
                Assert.Equal("" + (char)('A' + i), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                var expectedColumnType = i switch
                {
                    0 => expectedColumnType1,
                    1 => expectedColumnType2,
                    2 => expectedColumnType3,
                    _ => throw new InvalidOperationException("Unexpected column index")
                };
                Assert.Equal(expectedColumnType, column.ValueType); // the expected column types are checked
                if (expectedColumnType == ValueType.Numeric)
                {
                    Assert.Equal(DeserializedDataType.Double, column.DeserializedDataType); // the expected column types are checked
                }
                Assert.Equal(i == 0 ? SpreadColumnType.X : SpreadColumnType.Y, column.ColumnType); // the expected column types are checked
                Assert.Equal(6.7, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
                Assert.Empty(column.LongName);
                Assert.Empty(column.Unit);
                Assert.Empty(column.Comment);

                if (column.Data is not null)
                {
                    for (int j = 0; j < column.Data.Count; j++)
                    {
                        Assert.True(column.Data[j].IsCellEmpty);
                    }
                }
            }
        }
    }
}
