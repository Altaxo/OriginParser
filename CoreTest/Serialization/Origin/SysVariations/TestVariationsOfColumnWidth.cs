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
            var columns = sheet.Columns;
            Assert.Equal(2, columns.Count);

            // column names
            var columnA = columns[0];
            Assert.Equal("A", columnA.Name);
            var columnB = columns[1];
            Assert.Equal("B", columnB.Name);

            // column widths
            Assert.Equal(ValueType.TextNumeric, columnA.ValueType);
            Assert.Equal(SpreadColumnType.X, columnA.ColumnType);
            Assert.Equal(widthA, columnA.Width);
            Assert.Equal("", columnA.LongName);
            Assert.Equal("", columnA.Unit);
            Assert.Equal("In sheet Änderung", columnA.Comment);
            Assert.True(columnA.Data.Count >= 6);
            Assert.Equal(1.0, columnA.Data[0]);
            Assert.Equal(2.0, columnA.Data[1]);
            Assert.Equal(3.0, columnA.Data[2]);
            Assert.Equal("A", columnA.Data[3]);
            Assert.Equal("B", columnA.Data[4]);
            Assert.Equal("C", columnA.Data[5]);
            for (int i = 6; i < columnA.Data.Count; i++) { Assert.True(columnA.Data[i].IsCellEmpty, $"Cell A[{i}] contains {columnA.Data[i]}"); }

            Assert.Equal(ValueType.Numeric, columnB.ValueType);
            Assert.Equal(DeserializedDataType.Double, columnB.DeserializedDataType);
            Assert.Equal(SpreadColumnType.Y, columnB.ColumnType);
            Assert.Equal(widthB, columnB.Width);
            Assert.Equal("", columnB.LongName);
            Assert.Equal("", columnB.Unit);
            Assert.Equal("In sheet Änderung", columnB.Comment);
            Assert.True(columnB.Data.Count >= 7);
            Assert.Equal(4.0, columnB.Data[0]);
            Assert.Equal(5.0, columnB.Data[1]);
            Assert.Equal(6.0, columnB.Data[2]);
            Assert.Equal(4.0, columnB.Data[3]);
            Assert.Equal(5.0, columnB.Data[4]);
            Assert.Equal(6.0, columnB.Data[5]);
            Assert.Equal(7.0, columnB.Data[6]);
            for (int i = 7; i < columnB.Data.Count; i++) { Assert.True(columnB.Data[i].IsCellEmpty, $"Cell B[{i}] contains {columnB.Data[i]}"); }
        }

        [Fact]
        public void ReadsDistinctWidthsFromMixedScannerLayoutsInColumnOrder()
        {
            var bytes = ReadMixedLayoutFixture();
            // The first two widths use the combined layout; the third uses the standard layout.
            Assert.Equal(0x43, bytes[0xB18]);
            Assert.Equal(0x43, bytes[0xB4C]);
            Assert.Equal(0x43, bytes[0xB7B]);
            bytes[0xB18] = 44;
            bytes[0xB4C] = 45;
            bytes[0xB7B] = 46;

            using var stream = new MemoryStream(bytes);
            var columns = Assert.Single(Assert.Single(new OriginOpjuParser(stream).Books).Sheets).Columns;
            Assert.Equal(9, columns.Count);
            Assert.Equal(4.4, columns[0].Width, 1E-6);
            Assert.Equal(4.5, columns[1].Width, 1E-6);
            Assert.Equal(4.6, columns[2].Width, 1E-6);
            for (int i = 3; i < columns.Count; i++)
                Assert.Equal(6.7, columns[i].Width, 1E-6);
        }



        private static byte[] ReadMixedLayoutFixture()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "IndexOfXColumn", "OneBookOneSheetNineDoubleColumnsXAtC.opju");
            return File.ReadAllBytes(path);
        }
    }
}

