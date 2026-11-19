using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfDeserializedDataType
    {
        [Theory]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_Byte_Double.opju", DeserializedDataType.Double, DeserializedDataType.Byte, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_Complex_Double.opju", DeserializedDataType.Double, DeserializedDataType.Complex, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_Double_Double.opju", DeserializedDataType.Double, DeserializedDataType.Double, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_Double_UInt32.opju", DeserializedDataType.Double, DeserializedDataType.Double, DeserializedDataType.UInt32)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_Int16_Double.opju", DeserializedDataType.Double, DeserializedDataType.Int16, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_Int32_Double.opju", DeserializedDataType.Double, DeserializedDataType.Int32, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_SByte_Double.opju", DeserializedDataType.Double, DeserializedDataType.SByte, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_Single_Double.opju", DeserializedDataType.Double, DeserializedDataType.Single, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_UInt16_Double.opju", DeserializedDataType.Double, DeserializedDataType.UInt16, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Double_UInt32_Double.opju", DeserializedDataType.Double, DeserializedDataType.UInt32, DeserializedDataType.Double)]
        [InlineData("OneBookOneSheetThreeNumericColumns_Int32_Double_Double.opju", DeserializedDataType.Int32, DeserializedDataType.Double, DeserializedDataType.Double)]
        public void ExpectDeserializedDataTypes(string shortFileName, DeserializedDataType expectedColumnType1, DeserializedDataType expectedColumnType2, DeserializedDataType expectedColumnType3)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "DeserializedDataType", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books);
            var book = parser.Books[0];
            Assert.Equal("Book1", book.Name); // named 'Book1'
            Assert.Equal("Ößberg", book.LongName);
            Assert.Equal("Meßfolder", book.FolderName);

            Assert.Single(book.Sheets);
            var sheet = book.Sheets[0];
            Assert.Equal("Äalesund", sheet.Name);
            var columns = sheet.Columns;
            Assert.Equal(3, columns.Count);


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

                Assert.Equal(ValueType.Numeric, column.ValueType); // the expected column types are checked
                Assert.Equal(expectedColumnType, column.DeserializedDataType); // the expected column types are checked
                Assert.Equal(SpreadColumnType.Y, column.ColumnType);
                Assert.Equal(6.7, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
                Assert.Empty(column.LongName);
                Assert.Empty(column.Unit);
                Assert.Empty(column.Comment);
                if (column.Data is not null)
                {
                    for (int j = 0; j < column.Data.Count; j++)
                    {
                        Assert.True(column.Data[j].IsCellEmpty); // we have no values, so all values should be NaN
                    }
                }
            }
        }
    }
}
