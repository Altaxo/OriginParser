using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfColumnLongname
    {
        [Theory]
        [InlineData("OneBookOneSheetFiveColumns.opju", new string[] { "", "", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_A.opju", new string[] { "Überdruck", "", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_B.opju", new string[] { "", "Überdruck", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_C.opju", new string[] { "", "", "Öberg", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_E.opju", new string[] { "", "", "", "", "A very long LongName with spaces!" })]
        [InlineData("OneBookOneSheetFiveColumns_A_B_C_D_E.opju", new string[] { "1 Langname 1", "2 Langname 2", "3 Langname 3", "4 Langname 4", "5 Langname 5" })]
        public void ExpectLongname(string shortFileName, string[] longnames)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "ColumnLongname", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books); // there is only one Book
            Assert.Equal("Book1", parser.Books[0].Name); // named 'Book1'
            Assert.Single(parser.Books[0].Sheets); // with one sheet inside
            Assert.Equal(5, parser.Books[0].Sheets[0].Columns.Count); // with the expected number of columns

            ValueType[] expectedValueTypes = [ValueType.Numeric, ValueType.TextNumeric, ValueType.TextNumeric, ValueType.TextNumeric, ValueType.Text];
            SpreadColumnType[] expectedSpreadColumnTypes = [SpreadColumnType.X, SpreadColumnType.Z, SpreadColumnType.Y, SpreadColumnType.YErr, SpreadColumnType.Y];


            for (int iColumn = 0; iColumn < 5; iColumn++)
            {
                var column = parser.Books[0].Sheets[0].Columns[iColumn];
                Assert.Equal("" + (char)('A' + iColumn), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                Assert.Equal(expectedValueTypes[iColumn], column.ValueType); // the columns value types are expected to be Numeric, TextNumeric, TextNumeric, TextNumeric, Text for the first 5 columns)
                Assert.Equal(expectedSpreadColumnTypes[iColumn], column.SpreadColumnType); // the columns spread column types are expected to be X, Z, Y, YErr, Y for the first 5 columns)

                Assert.Equal("", column.Unit); // the columns unit is expected to be empty
                Assert.Equal("", column.Comment); // the columns comment is expected to be empty
                Assert.Equal(longnames[iColumn], column.Longname);
            }
        }
    }
}
