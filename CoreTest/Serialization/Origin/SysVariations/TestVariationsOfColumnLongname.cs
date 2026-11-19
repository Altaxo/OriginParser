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
        public void ExpectLongname(string shortFileName, string[] expectedLongName)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "ColumnLongname", shortFileName);
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
            Assert.Equal(5, columns.Count);
            ValueType[] expectedValueTypes = [ValueType.Numeric, ValueType.TextNumeric, ValueType.TextNumeric, ValueType.TextNumeric, ValueType.Text];
            SpreadColumnType[] expectedSpreadColumnTypes = [SpreadColumnType.X, SpreadColumnType.Z, SpreadColumnType.Y, SpreadColumnType.YErr, SpreadColumnType.Y];

            OriginOpjuParser.SpreadColumn column;
            for (int iColumn = 0; iColumn < 5; iColumn++)
            {
                column = parser.Books[0].Sheets[0].Columns[iColumn];
                Assert.Equal("" + (char)('A' + iColumn), column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
                Assert.Equal(expectedValueTypes[iColumn], column.ValueType); // the columns value types are expected to be Numeric, TextNumeric, TextNumeric, TextNumeric, Text for the first 5 columns)
                Assert.Equal(expectedSpreadColumnTypes[iColumn], column.ColumnType); // the columns spread column types are expected to be X, Z, Y, YErr, Y for the first 5 columns)

                Assert.Equal("", column.Unit); // the columns unit is expected to be empty
                Assert.Equal("", column.Comment); // the columns comment is expected to be empty
                Assert.Equal(expectedLongName[iColumn], column.LongName);

                Assert.NotNull(column.Data);
                Assert.True(column.Data.Count >= 3);
                for (int j = 3; j < column.Data.Count; j++)
                {
                    Assert.True(column.Data[j].IsCellEmpty, $"Cell {column.Name}[{j}] contains {column.Data[j]}");
                }
            }

            column = columns[0];
            Assert.Equal(ValueType.Numeric, column.ValueType);
            Assert.Equal(DeserializedDataType.Double, column.DeserializedDataType);
            Assert.Equal(SpreadColumnType.X, column.ColumnType);
            Assert.Equal(12, column.Width, 1E-6);
            Assert.Equal(1.0, column.Data[0]);
            Assert.Equal(2.0, column.Data[1]);
            Assert.Equal(3.0, column.Data[2]);

            column = columns[1];
            Assert.Equal(ValueType.TextNumeric, column.ValueType);
            Assert.Equal(SpreadColumnType.Z, column.ColumnType);
            Assert.Equal(14.5, column.Width, 1E-6);
            Assert.Equal(1.5, column.Data[0]);
            Assert.Equal(4.5, column.Data[1]);
            Assert.Equal(7.5, column.Data[2]);


            column = columns[2];
            Assert.Equal(ValueType.TextNumeric, column.ValueType);
            Assert.Equal(SpreadColumnType.Y, column.ColumnType);
            Assert.Equal(9.0, column.Width, 1E-6);
            Assert.Equal(1E3, column.Data[0]);
            Assert.Equal(1E6, column.Data[1]);
            Assert.Equal(1E9, column.Data[2]);

            column = columns[3];
            Assert.Equal(ValueType.TextNumeric, column.ValueType);
            Assert.Equal(SpreadColumnType.YErr, column.ColumnType);
            Assert.Equal(18.0, column.Width, 1E-6);
            Assert.Equal(1E-3, column.Data[0]);
            Assert.Equal(1E-6, column.Data[1]);
            Assert.Equal(1E-9, column.Data[2]);

            column = columns[4];
            Assert.Equal(ValueType.Text, column.ValueType);
            Assert.Equal(SpreadColumnType.Y, column.ColumnType);
            Assert.Equal(17.0, column.Width, 1E-6);
            Assert.Equal("Müller", column.Data[0]);
            Assert.Equal("Meier", column.Data[1]);
            Assert.Equal("Schultze", column.Data[2]);

        }
    }
}
