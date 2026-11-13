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
        public void ExpectIndexOfXColumn(string shortFileName, SpreadColumnType expectedColumnType1, SpreadColumnType expectedColumnType2, SpreadColumnType expectedColumnType3)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "SpreadColumnType", shortFileName);
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

                Assert.Equal(expectedColumnType, column.SpreadColumnType); // the expected column types are checked
            }
        }
    }
}
