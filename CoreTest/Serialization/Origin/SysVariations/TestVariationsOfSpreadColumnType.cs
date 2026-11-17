using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfSpreadColumnType
    {
        [Theory]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Ignore_Y_Y.opju", SpreadColumnType.Ignore, SpreadColumnType.Y, SpreadColumnType.Y)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_X_XErr_Y.opju", SpreadColumnType.X, SpreadColumnType.XErr, SpreadColumnType.Y)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_X_Y_Group.opju", SpreadColumnType.X, SpreadColumnType.Y, SpreadColumnType.Group)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_X_Y_Ignore.opju", SpreadColumnType.X, SpreadColumnType.Y, SpreadColumnType.Ignore)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_X_Y_Label.opju", SpreadColumnType.X, SpreadColumnType.Y, SpreadColumnType.Label)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_X_Y_Subject.opju", SpreadColumnType.X, SpreadColumnType.Y, SpreadColumnType.Subject)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_X_Y_Y.opju", SpreadColumnType.X, SpreadColumnType.Y, SpreadColumnType.Y)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_X_Y_YErr.opju", SpreadColumnType.X, SpreadColumnType.Y, SpreadColumnType.YErr)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_X_Y_Z.opju", SpreadColumnType.X, SpreadColumnType.Y, SpreadColumnType.Z)]
        [InlineData("OneBookOneSheetThreeDoubleColumns_Z_Y_Z.opju", SpreadColumnType.Z, SpreadColumnType.Y, SpreadColumnType.Z)]
        public void ExpectIndexOfXColumn(string shortFileName, SpreadColumnType expectedSpreadColumnType1, SpreadColumnType expectedSpreadColumnType2, SpreadColumnType expectedSpreadColumnType3)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "SpreadColumnType", shortFileName);
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
                Assert.Equal(ValueType.Numeric, column.ValueType); // the expected column types are checked
                Assert.Equal(DeserializedDataType.Double, column.DeserializedDataType); // the expected column types are checked
                var expectedColumnType = i switch
                {
                    0 => expectedSpreadColumnType1,
                    1 => expectedSpreadColumnType2,
                    2 => expectedSpreadColumnType3,
                    _ => throw new InvalidOperationException("Unexpected column index")
                };
                Assert.Equal(expectedColumnType, column.SpreadColumnType); // the expected column types are checked

                Assert.Equal(6.7, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
                Assert.Empty(column.LongName);
                Assert.Empty(column.Unit);
                Assert.Empty(column.Comment);
            }
        }
    }
}
