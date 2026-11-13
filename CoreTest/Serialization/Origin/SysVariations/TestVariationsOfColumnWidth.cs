using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfColumnWidth
    {
        [Theory]
        [InlineData("OneBookOneSheetTwoColumns.opju", 31, 17)]
        [InlineData("OneBookOneSheetTwoColumns_A29.opju", 29, 17)]
        [InlineData("OneBookOneSheetTwoColumns_A33_B19.opju", 33, 19)]
        [InlineData("OneBookOneSheetTwoColumns_B27.opju", 31, 27)]
        public void ExpectWidth(string shortFileName, double widthA, double widthB)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "ColumnWidth", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books);
            var book = parser.Books[0];
            Assert.Equal("Biro", book.Name);
            Assert.Equal("Bauerfeind", book.LongName);
            Assert.Equal("Überlingen", book.FolderName);
            Assert.Single(book.Sheets);
            var sheet = book.Sheets[0];
            Assert.Equal("Änderung", sheet.Name);
            var columns = parser.Books[0].Sheets[0].Columns;
            Assert.Equal(2, columns.Count);

            // column names
            var columnA = columns[0];
            Assert.Equal("A", columnA.Name);
            var columnB = columns[1];
            Assert.Equal("B", columnB.Name);

            // column widths
            Assert.Equal(widthA, columnA.Width);
            Assert.Equal("", columnA.Longname);
            Assert.Equal("", columnA.Unit);
            Assert.Equal("In sheet Änderung", columnA.Comment);
            Assert.True(columnA.Values.Count >= 6);
            Assert.Equal(ValueType.TextNumeric, columnA.ValueType);
            Assert.Equal(1.0, columnA.Values[0]);
            Assert.Equal(2.0, columnA.Values[1]);
            Assert.Equal(3.0, columnA.Values[2]);
            Assert.Equal("A", columnA.Values[3]);
            Assert.Equal("B", columnA.Values[4]);
            Assert.Equal("C", columnA.Values[5]);
            //for (int i = 6; i < columnA.Values.Count; i++) { Assert.True(columnA.Values[i].IsEmpty); }

            Assert.Equal(widthB, columnB.Width);
            Assert.Equal("", columnB.Longname);
            Assert.Equal("", columnB.Unit);
            Assert.Equal("In sheet Änderung", columnB.Comment);
            Assert.True(columnB.Values.Count >= 7);
            Assert.Equal(ValueType.Numeric, columnB.ValueType);
            Assert.Equal(DeserializedDataType.Double, columnB.DeserializedDataType);
            Assert.Equal(4.0, columnB.Values[0]);
            Assert.Equal(5.0, columnB.Values[1]);
            Assert.Equal(6.0, columnB.Values[2]);
            Assert.Equal(4.0, columnB.Values[3]);
            Assert.Equal(5.0, columnB.Values[4]);
            Assert.Equal(6.0, columnB.Values[5]);
            Assert.Equal(7.0, columnB.Values[6]);
            //for (int i = 7; i < columnB.Values.Count; i++) { Assert.True(columnB.Values[i].IsEmpty); }
        }
    }
}
