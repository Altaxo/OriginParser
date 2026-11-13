using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfColumnUnit
    {
        [Theory]
        [InlineData("OneBookOneSheetFiveColumns.opju", new string[] { "", "", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_A.opju", new string[] { "Café au Lait", "", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_B.opju", new string[] { "", "München City", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_C.opju", new string[] { "", "", "Peñero Pérez", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_E.opju", new string[] { "", "", "", "", "✨Sparkle✨" })]
        [InlineData("OneBookOneSheetFiveColumns_A_B_C_D_E.opju", new string[] { "Björnsson", "✨Sparkle✨", "Peñero Pérez", "Café au Lait", "München City" })]
        public void ExpectUnit(string shortFileName, string[] units)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "ColumnUnit", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books);
            Assert.Equal("Book1", parser.Books[0].Name);
            Assert.Single(parser.Books[0].Sheets);
            var columns = parser.Books[0].Sheets[0].Columns;
            Assert.Equal(5, columns.Count);

            ValueType[] expectedValueTypes = [ValueType.Numeric, ValueType.TextNumeric, ValueType.TextNumeric, ValueType.TextNumeric, ValueType.Text];
            SpreadColumnType[] expectedSpreadColumnTypes = [SpreadColumnType.X, SpreadColumnType.Z, SpreadColumnType.Y, SpreadColumnType.YErr, SpreadColumnType.Y];

            string[] expectedLongnames = ["1 Langname 1", "2 Langname 2", "3 Langname 3", "4 Langname 4", "5 Langname 5"];

            for (int iColumn = 0; iColumn < 5; iColumn++)
            {
                var column = columns[iColumn];
                Assert.Equal("" + (char)('A' + iColumn), column.Name);
                Assert.Equal(expectedValueTypes[iColumn], column.ValueType); // the columns value types are expected to be Numeric, TextNumeric, TextNumeric, TextNumeric, Text for the first 5 columns)
                Assert.Equal(expectedSpreadColumnTypes[iColumn], column.SpreadColumnType); // the columns spread column types are expected to be X, Z, Y, YErr, Y for the first 5 columns)
                Assert.Equal(expectedLongnames[iColumn], column.Longname);
                Assert.Equal(units[iColumn], column.Unit);
                Assert.Equal("", column.Comment); // the columns comment is expected to be empty

            }
        }
    }
}
