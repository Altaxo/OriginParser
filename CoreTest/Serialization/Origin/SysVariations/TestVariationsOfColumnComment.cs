using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfColumnComment
    {
        [Theory]
        [InlineData("OneBookOneSheetFiveColumns.opju", new string[] { "", "", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_A.opju", new string[] { "Résumé", "", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_B.opju", new string[] { "", "Español", "", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_C.opju", new string[] { "", "", "🔥Firefly🔥", "", "" })]
        [InlineData("OneBookOneSheetFiveColumns_E.opju", new string[] { "", "", "", "", "Århus" })]
        [InlineData("OneBookOneSheetFiveColumns_A_B_C_D_E.opju", new string[] { "🔥Firefly🔥", "Århus", "München", "Español", "Résumé" })]
        public void ExpectComment(string shortFileName, string[] expectedComments)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "ColumnComment", shortFileName);
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

            ValueType[] expectedValueTypes = { ValueType.Numeric, ValueType.TextNumeric, ValueType.TextNumeric, ValueType.TextNumeric, ValueType.Text };
            SpreadColumnType[] expectedSpreadColumnTypes = { SpreadColumnType.X, SpreadColumnType.Z, SpreadColumnType.Y, SpreadColumnType.YErr, SpreadColumnType.Y };

            string[] expectedLongnames = { "1 Langname 1", "2 Langname 2", "3 Langname 3", "4 Langname 4", "5 Langname 5" };
            string[] expectedUnits = { "Björnsson", "✨Sparkle✨", "Peñero Pérez", "Café au Lait", "München City" };

            OriginOpjuParser.AbstractColumn column;
            for (int iColumn = 0; iColumn < 5; iColumn++)
            {
                column = columns[iColumn];
                Assert.Equal("" + (char)('A' + iColumn), column.Name);
                Assert.Equal(expectedValueTypes[iColumn], column.ValueType); // the columns value types are expected to be Numeric, TextNumeric, TextNumeric, TextNumeric, Text for the first 5 columns)
                Assert.Equal(expectedSpreadColumnTypes[iColumn], column.SpreadColumnType); // the columns spread column types are expected to be X, Z, Y, YErr, Y for the first 5 columns)
                Assert.Equal(expectedLongnames[iColumn], column.LongName);
                Assert.Equal(expectedUnits[iColumn], column.Unit);
                Assert.Equal(expectedComments[iColumn], column.Comment);
                Assert.NotNull(column.Values);
                Assert.True(column.Values.Count >= 3);
                for (int j = 3; j < column.Values.Count; j++)
                {
                    Assert.True(column.Values[j].IsCellEmpty, $"Cell {column.Name}[{j}] contains {column.Values[j]}");
                }
            }

            column = columns[0];
            Assert.Equal(ValueType.Numeric, column.ValueType);
            Assert.Equal(DeserializedDataType.Double, column.DeserializedDataType);
            Assert.Equal(SpreadColumnType.X, column.SpreadColumnType);
            Assert.Equal(12, column.Width, 1E-6);
            Assert.Equal(1.0, column.Values[0]);
            Assert.Equal(2.0, column.Values[1]);
            Assert.Equal(3.0, column.Values[2]);

            column = columns[1];
            Assert.Equal(ValueType.TextNumeric, column.ValueType);
            Assert.Equal(SpreadColumnType.Z, column.SpreadColumnType);
            Assert.Equal(14.5, column.Width, 1E-6);
            Assert.Equal(1.5, column.Values[0]);
            Assert.Equal(4.5, column.Values[1]);
            Assert.Equal(7.5, column.Values[2]);


            column = columns[2];
            Assert.Equal(ValueType.TextNumeric, column.ValueType);
            Assert.Equal(SpreadColumnType.Y, column.SpreadColumnType);
            Assert.Equal(9.0, column.Width, 1E-6);
            Assert.Equal(1E3, column.Values[0]);
            Assert.Equal(1E6, column.Values[1]);
            Assert.Equal(1E9, column.Values[2]);

            column = columns[3];
            Assert.Equal(ValueType.TextNumeric, column.ValueType);
            Assert.Equal(SpreadColumnType.YErr, column.SpreadColumnType);
            Assert.Equal(18.0, column.Width, 1E-6);
            Assert.Equal(1E-3, column.Values[0]);
            Assert.Equal(1E-6, column.Values[1]);
            Assert.Equal(1E-9, column.Values[2]);

            column = columns[4];
            Assert.Equal(ValueType.Text, column.ValueType);
            Assert.Equal(SpreadColumnType.Y, column.SpreadColumnType);
            Assert.Equal(17.0, column.Width, 1E-6);
            Assert.Equal("Müller", column.Values[0]);
            Assert.Equal("Meier", column.Values[1]);
            Assert.Equal("Schultze", column.Values[2]);
        }
    }
}
