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
        public void ExpectNumberOfBooks(string shortFileName, string[] folderNames, string[] bookNamesShort, string[] bookNamesLong, string[] sheetNames)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumberOfBooks", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Equal(folderNames.Length, bookNamesShort.Length); // we expect the expected number of books
            Assert.Equal(folderNames.Length, bookNamesLong.Length); // we expect the expected number of books
            Assert.Equal(folderNames.Length, sheetNames.Length); // we expect the expected number of books
            Assert.Equal(bookNamesShort.Length, parser.Books.Count); // we expect the expected number of books

            for (int iBook = 0; iBook < parser.Books.Count; iBook++)
            {
                var book = parser.Books[iBook];
                Assert.Equal(bookNamesShort[iBook], book.Name); // check the name of the book
                Assert.Equal(bookNamesLong[iBook], book.LongName); // check the long name of the book
                Assert.Equal(folderNames[iBook], book.FolderName); // check the folder name of the book

                var sheets = book.Sheets;
                Assert.Single(sheets); // we expect only one sheet in each book
                var sheet = sheets[0];
                Assert.Equal(sheetNames[iBook], sheet.Name); // check the name of the sheet

                Assert.Equal(2, sheet.Columns.Count); // we expect 2 columns in each sheet
                var columnA = sheet.Columns[0];
                var columnB = sheet.Columns[1];

                Assert.Equal("A", columnA.Name);
                Assert.Equal("B", columnB.Name);

                Assert.Equal($"In sheet {sheetNames[iBook]}", columnA.Comment);
                Assert.Equal($"In sheet {sheetNames[iBook]}", columnB.Comment);
            }
        }
    }
}
