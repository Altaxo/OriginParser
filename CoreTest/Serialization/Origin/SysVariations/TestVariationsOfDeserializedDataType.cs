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

                Assert.Equal(ValueType.Numeric, column.ValueType); // the expected column types are checked
                Assert.Equal(expectedColumnType, column.DeserializedDataType); // the expected column types are checked
            }
        }
    }
}
