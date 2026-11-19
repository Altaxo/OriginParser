using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfNumberOfBooks
    {
        [Theory]
        [InlineData("OneBookOneSheetTwoColumns.opju", new string[] { "Überlingen" }, new string[] { "Biro" }, new string[] { "Bauerfeind" }, new string[] { "Änderung" })]
        [InlineData("OneBookOneSheetTwoColumns_1MoreBook.opju", new string[] { "Überlingen", "Überlingen" }, new string[] { "Biro", "Strasse" }, new string[] { "Bauerfeind", "Köln" }, new string[] { "Änderung", "Piñata" })]
        [InlineData("OneBookOneSheetTwoColumns_2MoreBooks.opju", new string[] { "Überlingen", "Überlingen", "Überlingen\\Grüezi" }, new string[] { "Biro", "Strasse", "Kitcat" }, new string[] { "Bauerfeind", "Köln", "Español" }, new string[] { "Änderung", "Piñata", "🔥Firefly🔥" })]
        [InlineData("OneBookOneSheetTwoColumns_3MoreBooks.opju", new string[] { "Überlingen", "Überlingen", "Überlingen\\Grüezi", "" }, new string[] { "Biro", "Strasse", "Kitcat", "Cacao" }, new string[] { "Bauerfeind", "Köln", "Español", "✨Sparkle✨" }, new string[] { "Änderung", "Piñata", "🔥Firefly🔥", "Århus" })]
        public void ExpectNumberOfBooks(string shortFileName, string[] expectedFolderNames, string[] expectedBookNamesShort, string[] expectedBookNamesLong, string[] expectedSheetNames)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumberOfBooks", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Equal(expectedFolderNames.Length, expectedBookNamesShort.Length); // we expect the expected number of books
            Assert.Equal(expectedFolderNames.Length, expectedBookNamesLong.Length); // we expect the expected number of books
            Assert.Equal(expectedFolderNames.Length, expectedSheetNames.Length); // we expect the expected number of books


            Assert.Equal(expectedBookNamesShort.Length, parser.Books.Count); // we expect the expected number of books
            OriginOpjuParser.Book book;
            for (int iBook = 0; iBook < parser.Books.Count; iBook++)
            {
                book = parser.Books[iBook];
                Assert.Equal(expectedBookNamesShort[iBook], book.Name); // check the name of the book
                Assert.Equal(expectedBookNamesLong[iBook], book.LongName); // check the long name of the book
                Assert.Equal(expectedFolderNames[iBook], book.FolderName); // check the folder name of the book

                var sheets = book.Sheets;
                Assert.Single(sheets); // we expect only one sheet in each book
                var sheet = sheets[0];
                Assert.Equal(expectedSheetNames[iBook], sheet.Name); // check the name of the sheet
                Assert.NotNull(sheet.Columns); // we expect the columns to be not null
                Assert.Equal(2, sheet.Columns.Count); // we expect 2 columns in each sheet
                var columnA = sheet.Columns[0];
                var columnB = sheet.Columns[1];

                Assert.Equal("A", columnA.Name);
                Assert.Equal("B", columnB.Name);

                Assert.Empty(columnA.LongName);
                Assert.Empty(columnB.LongName);
                Assert.Empty(columnA.Unit);
                Assert.Empty(columnB.Unit);
                Assert.Equal($"In sheet {expectedSheetNames[iBook]}", columnA.Comment);
                Assert.Equal($"In sheet {expectedSheetNames[iBook]}", columnB.Comment);

                switch (iBook)
                {
                    case 0:
                        {
                            Assert.Equal(ValueType.TextNumeric, columnA.ValueType);
                            Assert.Equal(SpreadColumnType.X, columnA.ColumnType);
                            Assert.Equal(31, columnA.Width);

                            Assert.NotNull(columnA.Data);
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
                            Assert.Equal(17, columnB.Width);

                            Assert.NotNull(columnB.Data);
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
                        break;
                    case 1:
                        {
                            Assert.Equal(ValueType.Numeric, columnA.ValueType);
                            Assert.Equal(DeserializedDataType.Double, columnA.DeserializedDataType);
                            Assert.Equal(SpreadColumnType.Z, columnA.ColumnType);
                            Assert.Equal(6.7, columnA.Width, 1e-6);

                            Assert.NotNull(columnA.Data);
                            Assert.True(columnA.Data.Count >= 2);
                            Assert.Equal(345.0, columnA.Data[0]);
                            Assert.Equal(1324.0, columnA.Data[1]);
                            for (int i = 2; i < columnA.Data.Count; i++) { Assert.True(columnA.Data[i].IsCellEmpty, $"Cell A[{i}] contains {columnA.Data[i]}"); }

                            Assert.Equal(ValueType.TextNumeric, columnB.ValueType);
                            Assert.Equal(SpreadColumnType.Y, columnB.ColumnType);
                            Assert.Equal(6.7, columnB.Width, 1e-6);

                            Assert.NotNull(columnB.Data);
                            Assert.True(columnB.Data.Count >= 2);
                            Assert.Equal(623.0, columnB.Data[0]);
                            Assert.Equal(5234.0, columnB.Data[1]);
                            for (int i = 2; i < columnB.Data.Count; i++) { Assert.True(columnB.Data[i].IsCellEmpty, $"Cell B[{i}] contains {columnB.Data[i]}"); }

                        }
                        break;
                    case 2:
                        {
                            Assert.Equal(ValueType.TextNumeric, columnA.ValueType);
                            Assert.Equal(SpreadColumnType.X, columnA.ColumnType);
                            Assert.Equal(6.7, columnA.Width, 1e-6);

                            Assert.NotNull(columnA.Data);
                            Assert.True(columnA.Data.Count >= 3);
                            Assert.Equal("Ä", columnA.Data[0]);
                            Assert.Equal("Ö", columnA.Data[1]);
                            Assert.Equal("Ü", columnA.Data[2]);
                            for (int i = 3; i < columnA.Data.Count; i++) { Assert.True(columnA.Data[i].IsCellEmpty, $"Cell A[{i}] contains {columnA.Data[i]}"); }

                            Assert.Equal(ValueType.TextNumeric, columnB.ValueType);
                            Assert.Equal(SpreadColumnType.Y, columnB.ColumnType);
                            Assert.Equal(6.7, columnB.Width, 1e-6);

                            Assert.NotNull(columnB.Data);
                            Assert.True(columnB.Data.Count >= 3);
                            Assert.Equal(9.0, columnB.Data[0]);
                            Assert.Equal(8.0, columnB.Data[1]);
                            Assert.Equal(7.0, columnB.Data[2]);
                            for (int i = 3; i < columnB.Data.Count; i++) { Assert.True(columnB.Data[i].IsCellEmpty, $"Cell B[{i}] contains {columnB.Data[i]}"); }

                        }
                        break;
                    case 3:
                        {
                            Assert.Equal(ValueType.TextNumeric, columnA.ValueType);
                            Assert.Equal(SpreadColumnType.X, columnA.ColumnType);
                            Assert.Equal(6.7, columnA.Width, 1e-6);

                            Assert.NotNull(columnA.Data);
                            Assert.True(columnA.Data.Count >= 2);
                            Assert.Equal(1111.0, columnA.Data[0]);
                            Assert.Equal(3333.0, columnA.Data[1]);
                            for (int i = 2; i < columnA.Data.Count; i++) { Assert.True(columnA.Data[i].IsCellEmpty, $"Cell A[{i}] contains {columnA.Data[i]}"); }

                            Assert.Equal(ValueType.TextNumeric, columnB.ValueType);
                            Assert.Equal(SpreadColumnType.Y, columnB.ColumnType);
                            Assert.Equal(6.7, columnB.Width, 1e-6);

                            Assert.NotNull(columnB.Data);
                            Assert.True(columnB.Data.Count >= 2);
                            Assert.Equal(2222.0, columnB.Data[0]);
                            Assert.Equal(4444.0, columnB.Data[1]);
                            for (int i = 2; i < columnB.Data.Count; i++) { Assert.True(columnB.Data[i].IsCellEmpty, $"Cell B[{i}] contains {columnB.Data[i]}"); }

                        }
                        break;
                }
            }
        }
    }
}
