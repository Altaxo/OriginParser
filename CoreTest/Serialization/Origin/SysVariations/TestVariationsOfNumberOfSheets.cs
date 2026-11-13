using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfNumberOfSheets
    {
        [Theory]
        [InlineData("OneBookOneSheetTwoColumns.opju", new string[] { "Änderung" })]
        [InlineData("OneBookOneSheetTwoColumns_1MoreSheet.opju", new string[] { "Änderung", "Revision" })]
        [InlineData("OneBookOneSheetTwoColumns_2MoreSheets.opju", new string[] { "Änderung", "Revision", "Riskmanagement" })]
        [InlineData("OneBookOneSheetTwoColumns_3MoreSheets.opju", new string[] { "Änderung", "Revision", "Riskmanagement", "Meßordner" })]
        public void ExpectNumberOfSheets(string shortFileName, string[] sheetNames)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumberOfSheets", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books); // there is only one Book
            var book = parser.Books[0];
            Assert.Equal("Book1", book.Name); // named 'Book1'
            var sheets = book.Sheets;
            Assert.Equal(sheetNames.Length, sheets.Count); // with the expected number of sheets

            for (int iSheet = 0; iSheet < sheets.Count; iSheet++)
            {
                var sheet = sheets[iSheet];
                Assert.Equal(iSheet + 1, sheet.Number);
                Assert.Equal(2, sheet.Columns.Count); // we expect 2 columns in each sheet
                var columnA = sheet.Columns[0];
                var columnB = sheet.Columns[1];

                Assert.Equal("A", columnA.Name);
                Assert.Equal("B", columnB.Name);

                Assert.Equal($"In sheet {iSheet + 1}", columnA.Comment);
                Assert.Equal($"In sheet {iSheet + 1}", columnB.Comment);

                Assert.Equal(sheetNames[iSheet], sheet.Name); // check the name of the sheet

                // check the content of the colums
                if (iSheet == 0)
                {
                    Assert.Equal(6, columnA.Values.Count);
                    Assert.Equal(6, columnB.Values.Count);

                    Assert.Equal(1.0, columnA.Values[0]);
                    Assert.Equal(2.0, columnA.Values[1]);
                    Assert.Equal(3.0, columnA.Values[2]);
                    Assert.Equal("A", columnA.Values[3]);
                    Assert.Equal("B", columnA.Values[4]);
                    Assert.Equal("C", columnA.Values[5]);

                    Assert.Equal(4.0, columnB.Values[0]);
                    Assert.Equal(5.0, columnB.Values[1]);
                    Assert.Equal(6.0, columnB.Values[2]);
                    Assert.Equal("D", columnB.Values[3]);
                    Assert.Equal("E", columnB.Values[4]);
                    Assert.Equal("F", columnB.Values[5]);

                }
                else
                {
                    Assert.Single(columnA.Values);
                    Assert.Single(columnB.Values);
                    Assert.Equal(iSheet + 1.0, columnA.Values[0]);
                    Assert.Equal(iSheet + 1.0, columnB.Values[0]);
                }
            }
        }
    }
}
