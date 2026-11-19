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
        [InlineData("OneEmptyWorksheet_OneGraph.opju", 1, 0, 1, 0)]
        [InlineData("OneEmptyWorksheet_OneMatrix.opju", 1, 1, 0, 0)]
        [InlineData("OneEmptyWorksheet_OneMatrixOneWorksheet.opju", 2, 1, 0, 0)]
        [InlineData("OneEmptyWorksheet_OneMatrixOneWorksheetOneNote.opju", 2, 1, 0, 1)]
        [InlineData("OneEmptyWorksheet_OneNote.opju", 1, 0, 0, 1)]
        [InlineData("OneEmptyWorksheet_TwoEmptyWorksheets.opju", 3, 0, 0, 0)]
        [InlineData("OneGraph.opju", 0, 0, 1, 0)]
        [InlineData("OneNote.opju", 0, 0, 0, 1)]
        [InlineData("TwoNotes.opju", 0, 0, 0, 2)]
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

            // Graphs
            if (expectedNumberOfGraphs > 0)
                ExpectGraph_Valencia(parser, shortFileName);

            // Notes
            if (expectedNumberOfNotes > 0)
                ExpectNote_Lenja(parser, shortFileName);
            if (expectedNumberOfNotes > 1)
                ExpectNote_Lissabon(parser, shortFileName);


        }

        void ExpectWorksheet_Biro(OriginOpjuParser parser, string shortFileName)
        {
            var worksheet = parser.Books.Where(w => w.Name == "Biro").FirstOrDefault();
            Assert.True(worksheet is not null, $"Expected to find a worksheet named 'Biro' in file {shortFileName}, but it was not found.");

            Assert.Equal("Biro", worksheet.Name);
            Assert.Equal("Ächt", worksheet.LongName);
            Assert.Equal("München", worksheet.FolderName);
            Assert.Equal("This is the worksheet window B_i_r_o", worksheet.Comment);
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
            Assert.Equal("This is the worksheet window M_i_r_a", worksheet.Comment);
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
            Assert.Equal("This is the worksheet window A_n_n_i_k_a", worksheet.Comment);
            Assert.Single(worksheet.Sheets);
            var sheet = worksheet.Sheets[0];
            Assert.Equal("Bergen", sheet.Name);
            Assert.True(sheet.Columns is null || sheet.Columns.Count == 0, $"Expected no columns in sheet 'Bergen' of worksheet 'Annika' in file {shortFileName}, but found {sheet.Columns?.Count ?? 0} columns.");
        }

        void ExpectMatrix_Aachen(OriginOpjuParser parser, string shortFileName)
        {
            var matrix = parser.Matrices.Where(w => w.Name == "Aachen").FirstOrDefault();
            Assert.True(matrix is not null, $"Expected to find a matrix named 'Aachen' in file {shortFileName}, but it was not found.");

            Assert.Equal("Aachen", matrix.Name);
            Assert.Equal("Locomotive", matrix.LongName);
            Assert.Equal("Mömmern\\Döra", matrix.FolderName);
            Assert.Equal("This is the matrix window A_a_c_h_e_n", matrix.Comment);
            Assert.Single(matrix.Sheets);
            var sheet = matrix.Sheets[0];
            Assert.Equal("Maßricht", sheet.Name);
        }

        void ExpectGraph_Valencia(OriginOpjuParser parser, string shortFileName)
        {
            var graph = parser.Graphs.Where(w => w.Name == "Valencia").FirstOrDefault();
            Assert.True(graph is not null, $"Expected to find a graph named 'Valencia' in file {shortFileName}, but it was not found.");
            Assert.Equal("Valencia", graph.Name);
            Assert.Equal("Tromsø", graph.LongName);
            Assert.Equal("München\\Östen", graph.FolderName);
            Assert.Equal("This is the graph window V_a_l_e_n_c_i_a", graph.Comment);
        }

        void ExpectNote_Lenja(OriginOpjuParser parser, string shortFileName)
        {
            var note = parser.Notes.Where(w => w.Name == "Lenja").FirstOrDefault();
            Assert.True(note is not null, $"Expected to find a note named 'Lenja' in file {shortFileName}, but it was not found.");
            Assert.Equal("Lenja", note.Name);
            Assert.Equal("Kassel", note.LongName);
            Assert.Equal("München\\Björn", note.FolderName);
            Assert.Equal("This is the note window L_e_n_j_a", note.Comment);
            Assert.Equal("This is content inside the notes window:\r\nHere a second line.", note.Text);
        }

        void ExpectNote_Lissabon(OriginOpjuParser parser, string shortFileName)
        {
            var note = parser.Notes.Where(w => w.Name == "Lissabon").FirstOrDefault();
            Assert.True(note is not null, $"Expected to find a note named 'Lissabon' in file {shortFileName}, but it was not found.");
            Assert.Equal("Lissabon", note.Name);
            Assert.Equal("Ärmel", note.LongName);
            Assert.Equal("Oscar", note.FolderName);
            Assert.Equal("This is the note window L_i_s_s_a_b_o_n", note.Comment);
            Assert.Equal("Here we have the text inside the note window L-i-s-s-a-b-o-n.", note.Text);
        }

    }
}
