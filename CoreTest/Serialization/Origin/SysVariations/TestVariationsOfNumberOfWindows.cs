using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfNumberOfWindows
    {
        [Theory]
        [InlineData("OneEmptyWorksheet.opju", 1, 0, 0, 0)]
        [InlineData("OneEmptyWorksheet_OneEmptyWorksheet.opju", 2, 0, 0, 0)]
        [InlineData("OneEmptyWorksheet_TwoEmptyWorksheets.opju", 3, 0, 0, 0)]
        [InlineData("OneEmptyWorksheet_OneGraph.opju", 1, 0, 1, 0)]
        [InlineData("OneEmptyWorksheet_OneMatrix.opju", 1, 1, 0, 1)]
        [InlineData("OneEmptyWorksheet_OneNote.opju", 1, 0, 0, 1)]
        [InlineData("OneEmptyWorksheet_OneMatrixOneWorksheet.opju", 2, 1, 0, 0)]
        public void ExpectNumberOfWindows(string shortFileName, int expectedNumberOfWorksheets, int expectedNumberOfMatrices, int expectedNumberOfGraphs, int expectedNumberOfNotes)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumberOfWindows", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            // Worksheets 
            if (expectedNumberOfWorksheets > 0)
                ExpectWorksheet_Biro(parser, shortFileName);
            if (expectedNumberOfWorksheets > 1)
                ExpectWorksheet_Mira(parser, shortFileName);
            if (expectedNumberOfWorksheets > 2)
                ExpectWorksheet_Annika(parser, shortFileName);


            // Matrices
            if (expectedNumberOfMatrices > 0)
                ExpectMatrix_Aachen(parser, shortFileName);



        }

        void ExpectWorksheet_Biro(OriginOpjuParser parser, string shortFileName)
        {
            var worksheet = parser.Books.Where(w => w.Name == "Biro").FirstOrDefault();
            Assert.True(worksheet is not null, $"Expected to find a worksheet named 'Biro' in file {shortFileName}, but it was not found.");

            Assert.Equal("Biro", worksheet.Name);
            Assert.Equal("Ächt", worksheet.LongName);
            Assert.Equal("München", worksheet.FolderName);
            Assert.Single(worksheet.Sheets);
            var sheet = worksheet.Sheets[0];
            Assert.Equal("Kübel", sheet.Name);
            Assert.True(sheet.Columns is null || sheet.Columns.Count == 0, $"Expected no columns in sheet 'Kübel' of worksheet 'Biro' in file {shortFileName}, but found {sheet.Columns?.Count ?? 0} columns.");
        }

        void ExpectWorksheet_Mira(OriginOpjuParser parser, string shortFileName)
        {
            var worksheet = parser.Books.Where(w => w.Name == "Mira").FirstOrDefault();
            Assert.True(worksheet is not null, $"Expected to find a worksheet named 'Mira' in file {shortFileName}, but it was not found.");

            Assert.Equal("Mira", worksheet.Name);
            Assert.Equal("Lübeck", worksheet.LongName);
            Assert.Equal("München\\Jürgen", worksheet.FolderName);
            Assert.Single(worksheet.Sheets);
            var sheet = worksheet.Sheets[0];
            Assert.Equal("Göttingen", sheet.Name);
            Assert.True(sheet.Columns is null || sheet.Columns.Count == 0, $"Expected no columns in sheet 'Göttingen' of worksheet 'Mira' in file {shortFileName}, but found {sheet.Columns?.Count ?? 0} columns.");
        }

        void ExpectWorksheet_Annika(OriginOpjuParser parser, string shortFileName)
        {
            var worksheet = parser.Books.Where(w => w.Name == "Annika").FirstOrDefault();
            Assert.True(worksheet is not null, $"Expected to find a worksheet named 'Annika' in file {shortFileName}, but it was not found.");

            Assert.Equal("Annika", worksheet.Name);
            Assert.Equal("Zürich", worksheet.LongName);
            Assert.Equal("München\\Jürgen\\Henrik", worksheet.FolderName);
            Assert.Single(worksheet.Sheets);
            var sheet = worksheet.Sheets[0];
            Assert.Equal("Bergen", sheet.Name);
            Assert.True(sheet.Columns is null || sheet.Columns.Count == 0, $"Expected no columns in sheet 'Bergen' of worksheet 'Annika' in file {shortFileName}, but found {sheet.Columns?.Count ?? 0} columns.");
        }

        void ExpectWorksheet_Quark(OriginOpjuParser parser, string shortFileName)
        {
            var worksheet = parser.Books.Where(w => w.Name == "Quark").FirstOrDefault();
            Assert.True(worksheet is not null, $"Expected to find a worksheet named 'Quark' in file {shortFileName}, but it was not found.");

            Assert.Equal("Quark", worksheet.Name);
            Assert.Equal("Soße", worksheet.LongName);
            Assert.Equal("Üssel", worksheet.FolderName);
            Assert.Single(worksheet.Sheets);
            var sheet = worksheet.Sheets[0];
            Assert.Equal("Sheet1", sheet.Name);
            Assert.True(sheet.Columns is null || sheet.Columns.Count == 0, $"Expected no columns in sheet 'Sheet1' of worksheet 'Quark' in file {shortFileName}, but found {sheet.Columns?.Count ?? 0} columns.");
        }

        void ExpectMatrix_Aachen(OriginOpjuParser parser, string shortFileName)
        {
        }
    }
}
