#region Copyright

/////////////////////////////////////////////////////////////////////////////
//    Altaxo:  a data processing and data plotting program
//    Copyright (C) 2002-2026 Dr. Dirk Lellinger
//
//    This program is free software; you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation; either version 2 of the License, or
//    (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this program; if not, write to the Free Software
//    Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.
//
/////////////////////////////////////////////////////////////////////////////

#endregion Copyright

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Altaxo.Serialization.Origin.Tests
{
    /// <summary>Tests heuristic parsing of Origin OPJU project headers, records, images, and candidates.</summary>
    public class OriginOpjuParserTest
    {
        /// <summary>Retrieves the parser's private matrix dimension candidates for verification.</summary>
        /// <param name="parser">The parser containing the candidates.</param>
        /// <returns>The candidate names and their inferred row and column counts.</returns>
        private static (string Name, int? Rows, int? Columns)[] GetMatrixDimensions(OriginOpjuParser parser)
        {
            var field = typeof(OriginOpjuParser).GetField("_matrixCandidates", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return ((System.Collections.IEnumerable)field.GetValue(parser)!).Cast<object>().Select(candidate =>
            {
                var type = candidate.GetType();
                return ((string)type.GetProperty("Name")!.GetValue(candidate)!,
            (int?)type.GetProperty("NumberOfRows")!.GetValue(candidate),
            (int?)type.GetProperty("NumberOfColumns")!.GetValue(candidate));
            }).ToArray();
        }

        private static object? GetPartialModelProperty(object instance, string name)
        {
            for (var type = instance.GetType(); type is not null; type = type.BaseType)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property is not null)
                    return property.GetValue(instance);
            }
            throw new InvalidOperationException($"Property {name} was not found on the partial model.");
        }

        private static object[] GetUnassociatedColumns(OriginOpjuParser parser)
        {
            return ((System.Collections.IEnumerable)GetPartialModelProperty(parser, "UnassociatedColumns")!).Cast<object>().ToArray();
        }

        private static object[] GetModelItems(object instance, string name)
        {
            return ((System.Collections.IEnumerable)GetPartialModelProperty(instance, name)!).Cast<object>().ToArray();
        }

        private static object[] GetDatasetColumns(OriginOpjuParser parser)
        {
            return GetModelItems(parser, "Books")
                .SelectMany(book => GetModelItems(book, "Sheets"))
                .SelectMany(sheet => GetModelItems(sheet, "Columns"))
                .Concat(GetUnassociatedColumns(parser)).ToArray();
        }

        private static string GetVariationPath(string fileName)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", fileName + ".opju");
        }

        private static byte[] RenameVariationDatasets(string fileName, params (string Original, string Replacement)[] names)
        {
            var source = File.ReadAllBytes(GetVariationPath(fileName));
            using var stream = new MemoryStream(source);
            var parser = new OriginOpjuParser(stream);
            var columns = GetDatasetColumns(parser);
            foreach (var (original, replacement) in names)
            {
                var column = Assert.Single(columns.Where(candidate => (string?)GetPartialModelProperty(candidate, "DatasetName") == original));
                var records = ((System.Collections.IEnumerable)GetPartialModelProperty(column, "DescriptorRecords")!).Cast<OriginOpjuParser.IRecord>();
                var nameRecord = Assert.Single(records.OfType<OriginOpjuParser.TLVRecord>().Where(record => record.Text == original));
                var replacementBytes = Encoding.UTF8.GetBytes(replacement);
                Assert.Equal(nameRecord.Payload.Length, replacementBytes.Length);
                replacementBytes.CopyTo(source, nameRecord.Offset + 2);
            }
            return source;
        }

        /// <summary>Checks inferred matrix dimensions against known dimensions in real projects.</summary>
        /// <param name="fileName">The fixture file name.</param>
        /// <param name="rows">The expected number of rows per matrix.</param>
        /// <param name="columns">The expected number of columns per matrix.</param>
        /// <param name="count">The expected number of matrix candidates.</param>
        [Theory]
        [InlineData("Matrix137x179.opju", 179, 137, 1)]
        [InlineData("Matrix71x29.opju", 29, 71, 1)]
        [InlineData("OneWorksheetOneMatrix.opju", 5, 3, 1)]
        [InlineData("Matrix2x3OfDifferentTypes.opju", 3, 2, 9)]
        public void ReadsMatrixDimensions(string fileName, int rows, int columns, int count)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", fileName);
            using var stream = File.OpenRead(path);
            var dimensions = GetMatrixDimensions(new OriginOpjuParser(stream));

            Assert.Equal(count, dimensions.Length);
            Assert.All(dimensions, matrix =>
            {
                Assert.Equal(rows, matrix.Rows);
                Assert.Equal(columns, matrix.Columns);
            });
        }

        /// <summary>Checks that a nondivisible element count does not produce an inferred row count.</summary>
        [Fact]
        public void DoesNotInferRowsFromNonDivisibleElementCount()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "Matrix137x179.opju");
            var bytes = File.ReadAllBytes(path);
            // Alter the dataset element count without changing the sheet column count.
            bytes[222]++;
            using var stream = new MemoryStream(bytes);

            var matrix = Assert.Single(GetMatrixDimensions(new OriginOpjuParser(stream)));
            Assert.Null(matrix.Rows);
            Assert.Equal(137, matrix.Columns);
        }

        /// <summary>Checks the header and root bytes of a real project and verifies that the stream remains open.</summary>
        [Fact]
        public void ReadsRealProjectHeader()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "OneWorksheet.opju");
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Equal("4.3380", parser.FormatVersion);
            Assert.Equal(188, parser.ObjectCount);
            Assert.Equal(new byte[] { 0x22, 0x01, 0x6c, 0xc0, 0x11, 0x01, 0x04 }, parser.RootHeader.ToArray());
            Assert.NotEmpty(parser.Records);
            Assert.True(stream.CanRead);
        }

        /// <summary>Checks indexed PNG boundaries and complete, contiguous record coverage in real projects.</summary>
        /// <param name="fileName">The fixture file name.</param>
        /// <param name="expectedCount">The expected number of indexed images.</param>
        [Theory]
        [InlineData("DateTimeColumns.opju", 1)]
        [InlineData("Matrix137x179.opju", 1)]
        [InlineData("Matrix2x3OfDifferentTypes.opju", 9)]
        [InlineData("Matrix71x29.opju", 1)]
        [InlineData("OneWorksheet.opju", 1)]
        [InlineData("OneWorksheetOneMatrix.opju", 2)]
        [InlineData("OneWorksheetWithTwoSheets.opju", 1)]
        [InlineData("RepresentationOfNaN.opju", 1)]
        [InlineData("TwoWorksheets.opju", 2)]
        [InlineData("WksDifferentColumnTypes.opju", 1)]
        [InlineData("WksDifferentDiagramAssoc.opju", 4)]
        [InlineData("WksDifferentNumericColumns.opju", 1)]
        [InlineData("WksDifferentNumericFormattings.opju", 1)]
        [InlineData("WksThreeGroups.opju", 4)]
        public void ReadsIndexedPngRecords(string fileName, int expectedCount)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", fileName);
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var parser = new OriginOpjuParser(stream);
            var images = parser.Records.OfType<OriginOpjuParser.PngRecord>().ToArray();
            Assert.Equal(expectedCount, images.Length);

            // Check each index entry independently; some tables are not ordered by offset.
            int index = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(61, 4));
            for (int i = 0; i < expectedCount; ++i)
            {
                int offset = (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(index + 16 + 16 * i, 8));
                int length = (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(index + 24 + 16 * i, 8));
                var image = Assert.Single(images, record => record.Offset == offset);
                Assert.Equal(bytes.AsSpan(offset, length).ToArray(), image.Payload.ToArray());
                Assert.Equal(offset + length, image.NextOffset);
            }

            // Every body byte must belong to exactly one record, with no TLV inside a PNG.
            int position = parser.BodyOffset + 7;
            foreach (var record in parser.Records)
            {
                Assert.Equal(position, record.Offset);
                Assert.True(record.NextOffset > record.Offset);
                position = record.NextOffset;
            }
            Assert.Equal(bytes.Length, position);
        }


        /// <summary>Checks that a project without any preview image has neither an image table nor PNG records.</summary>
        /// <param name="fileName">The fixture file name below the <c>SysVariations</c> folder.</param>
        /// <remarks>
        /// The <c>PrvwOPJU</c> marker is written even for projects that only hold notes windows; for those the
        /// 8 byte image table pointer behind the marker is null instead of an offset.
        /// </remarks>
        [Theory]
        [InlineData(@"NumberOfWindows\OneNote.opju")]
        [InlineData(@"NumberOfWindows\TwoNotes.opju")]
        public void ReadsWithoutPreviewImages(string fileName)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", fileName);
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var parser = new OriginOpjuParser(stream);

            Assert.Empty(parser.Records.OfType<OriginOpjuParser.PngRecord>());
            Assert.Empty(parser.Records.OfType<OriginOpjuParser.ImageTable>());

            var marker = Assert.Single(parser.Records.OfType<OriginOpjuParser.TLVRecord>(), record => record.Payload.Span.StartsWith("PrvwOPJU"u8));
            Assert.Equal(0UL, BinaryPrimitives.ReadUInt64LittleEndian(marker.Payload.Span.Slice(8, 8)));

            // As in the projects with images, every body byte must belong to exactly one record.
            int position = parser.BodyOffset + 7;
            foreach (var record in parser.Records)
            {
                Assert.Equal(position, record.Offset);
                Assert.True(record.NextOffset > record.Offset);
                position = record.NextOffset;
            }
            Assert.Equal(bytes.Length, position);
        }


        /// <summary>Checks parsed header fields and candidate record bounds against each real project.</summary>
        /// <param name="fileName">The fixture file name.</param>
        [Theory]
        [InlineData("DateTimeColumns.opju")]
        [InlineData("Matrix137x179.opju")]
        [InlineData("Matrix2x3OfDifferentTypes.opju")]
        [InlineData("Matrix71x29.opju")]
        [InlineData("OneWorksheet.opju")]
        [InlineData("OneWorksheetOneMatrix.opju")]
        [InlineData("OneWorksheetWithTwoSheets.opju")]
        [InlineData("RepresentationOfNaN.opju")]
        [InlineData("TwoWorksheets.opju")]
        [InlineData("WksDifferentColumnTypes.opju")]
        [InlineData("WksDifferentDiagramAssoc.opju")]
        [InlineData("WksDifferentNumericColumns.opju")]
        [InlineData("WksDifferentNumericFormattings.opju")]
        [InlineData("WksThreeGroups.opju")]
        public void ReadsRealProjectRecords(string fileName)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", fileName);
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var parser = new OriginOpjuParser(stream);

            Assert.Equal("4.3380", parser.FormatVersion);
            Assert.Equal(188, parser.ObjectCount);
            Assert.Equal(17, parser.BodyOffset);
            Assert.Equal(new byte[] { 0x22, 0x01, 0x6c, 0xc0, 0x11, 0x01, 0x04 }, parser.RootHeader.ToArray());

            Assert.True(parser.Records.Count > 3);
            var tlv0 = parser.Records[0] as OriginOpjuParser.TLVRecord;
            var tlv1 = parser.Records[1] as OriginOpjuParser.TLVRecord;
            var tlv2 = parser.Records[2] as OriginOpjuParser.TLVRecord;
            Assert.NotNull(tlv0);
            Assert.NotNull(tlv1);
            Assert.NotNull(tlv2);

            Assert.Equal(24, tlv0.Offset);
            Assert.Equal(0x80, tlv0.Tag);
            Assert.Equal(new byte[] { 0x10 }, tlv0.Payload.ToArray());
            Assert.Equal(0x80, tlv1.Tag);
            Assert.Equal(new byte[] { 0x40, 0x80, 0x11, 0x01, 0xe4, 0x04 }, tlv1.Payload.ToArray());

            // Compare candidate payloads with the file and ensure unknown regions remain in bounds.
            Assert.NotEmpty(parser.Records);
            foreach (var record in parser.Records.OfType<OriginOpjuParser.TLVRecord>())
            {
                Assert.InRange(record.Offset, parser.BodyOffset + 7, bytes.Length - 2);
                Assert.InRange(record.Tag, (byte)0x80, (byte)0x9f);
                Assert.Equal(bytes[record.Offset], record.Tag);
                //Assert.Equal(bytes[record.Offset + 1], record.Payload.Length);
                Assert.True(record.Offset + 2 + record.Payload.Length <= bytes.Length);
                Assert.Equal(bytes.AsSpan(record.Offset + 2, record.Payload.Length).ToArray(), record.Payload.ToArray());
            }
            foreach (var gap in parser.Records.OfType<OriginOpjuParser.UnknownRegionRecord>())
            {
                Assert.InRange(gap.Offset, parser.BodyOffset + 7, bytes.Length - 1);
                Assert.InRange(gap.Length, 1, bytes.Length - gap.Offset);
            }
        }

        /// <summary>Checks that a matrix book name is recovered as a candidate record.</summary>
        /// <param name="fileName">The fixture file name.</param>
        [Theory]
        [InlineData("Matrix137x179.opju")]
        [InlineData("Matrix71x29.opju")]
        [InlineData("OneWorksheetOneMatrix.opju")]
        public void RecoversMatrixNameCandidateFromRealProject(string fileName)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", fileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Contains(parser.Records, record => record is OriginOpjuParser.TLVRecord tlv && tlv.Text == "MBook1");
        }

        /// <summary>Checks matrix dataset and sheet name candidates in single-matrix projects.</summary>
        /// <param name="fileName">The fixture file name.</param>
        [Theory]
        [InlineData("Matrix137x179.opju")]
        [InlineData("Matrix71x29.opju")]
        [InlineData("OneWorksheetOneMatrix.opju")]
        public void ReadsSingleMatrixAndSheetNameCandidates(string fileName)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", fileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Equal("MBook1", Assert.Single(parser.MatrixDatasetCandidates).Text);
            Assert.Single(parser.SheetNameCandidates, candidate => candidate.Name == "MSheet1");
        }

        /// <summary>Checks the names of matrices with different data types and their sheet candidates.</summary>
        [Fact]
        public void ReadsDifferentTypesMatrixAndSheetNameCandidates()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "Matrix2x3OfDifferentTypes.opju");
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            string[] expectedNames = ["MByte", "MChar", "MComplex", "MDouble", "MLong", "MReal", "MShort", "MULong", "MUShort"];
            var matrixNames = parser.MatrixDatasetCandidates.Select(candidate => candidate.Text).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedNames, matrixNames);
            Assert.Equal(9, parser.SheetNameCandidates.Count);
            Assert.All(parser.SheetNameCandidates, candidate => Assert.Equal("MSheet1", candidate.Name));
        }

        /// <summary>Checks candidate offsets and observed dimension-related bytes in the 137-by-179 matrix fixture.</summary>
        [Fact]
        public void TestMatrix137x179Candidates()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "Matrix137x179.opju");
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var parser = new OriginOpjuParser(stream);

            var matrix = Assert.Single(parser.MatrixDatasetCandidates, candidate => candidate.Text == "MBook1");
            Assert.Equal(249, matrix.Offset);
            Assert.Equal(0x97, matrix.Tag);
            var sheet = Assert.Single(parser.SheetNameCandidates, candidate => candidate.Name == "MSheet1");
            Assert.Equal(704, sheet.Offset);

            // Preserve observed element-count, column-count, and numeric field locations for format research.
            Assert.Equal(new byte[] { 0x83, 0x14, 0xcb, 0x5f }, bytes.AsSpan(224, 4).ToArray());
            Assert.Equal(137 * 179, BitConverter.ToUInt16(bytes, 226));
            Assert.Equal(new byte[] { 0x89, 0x01, 0x89 }, bytes.AsSpan(694, 3).ToArray());
            Assert.Equal(1, BitConverter.ToDouble(bytes, 295));
            Assert.Equal(10, BitConverter.ToDouble(bytes, 325));
            Assert.Equal(20, BitConverter.ToDouble(bytes, 348));
            Assert.Equal(30, BitConverter.ToDouble(bytes, 371));
        }

        /// <summary>Checks that a worksheet sheet name is not reported as a matrix dataset.</summary>
        [Fact]
        public void WorksheetSheetNameIsNotAReportedMatrixDataset()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "OneWorksheet.opju");
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Empty(parser.MatrixDatasetCandidates);
            Assert.Contains(parser.SheetNameCandidates, candidate => candidate.Name == "Sheet1");
        }

        /// <summary>Checks matrix and worksheet name candidates in a project containing both.</summary>
        [Fact]
        public void FindsMatrixAndWorksheetCandidatesInMixedProject()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "OneWorksheetOneMatrix.opju");
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Contains(parser.MatrixDatasetCandidates, candidate => candidate.Text == "MBook1");
            Assert.Contains(parser.SheetNameCandidates, candidate => candidate.Name == "MSheet1");
            Assert.Contains(parser.SheetNameCandidates, candidate => candidate.Name == "Sheet1");
        }

        /// <summary>Checks that scanning resumes after opaque bytes when four consecutive candidate records follow.</summary>
        [Fact]
        public void ReadsCandidateRecordsAndResynchronizesAcrossOpaqueData()
        {
            var header = Encoding.ASCII.GetBytes("CPYUA 4.3380 3\n");
            // Place an opaque gap between an initial text record and four records used for resynchronization.
            byte[] body = [0x22, 0x01, 0x6c, 0xc0, 0x11, 0x01, 0x04,
        0x82, 0x04, (byte)'T', (byte)'e', (byte)'s', (byte)'t',
        0x00, 0x01,
        0x80, 0x01, 0x01, 0x81, 0x01, 0x02, 0x82, 0x01, 0x03, 0x83, 0x01, 0x04];
            using var stream = new MemoryStream(header.Concat(body).ToArray());
            var parser = new OriginOpjuParser(stream);

            Assert.Equal(6, parser.Records.Count);
            Assert.True(parser.Records[0] is OriginOpjuParser.TLVRecord);
            Assert.True(parser.Records[1] is OriginOpjuParser.UnknownRegionRecord);

            Assert.Equal(5, parser.Records.OfType<OriginOpjuParser.TLVRecord>().Count());
            Assert.Equal("Test", Assert.Single(parser.Records.OfType<OriginOpjuParser.TLVRecord>().Where(r => r.Text is not null)).Text);
            var gap = Assert.Single(parser.Records.OfType<OriginOpjuParser.UnknownRegionRecord>());
            Assert.Equal(header.Length + 7 + 6, gap.Offset);
            Assert.Equal(2, gap.Length);
            Assert.Empty(parser.SheetNameCandidates);
        }

        /// <summary>Checks that sheet candidates require complete signatures and printable names.</summary>
        [Fact]
        public void SheetNameCandidatesRequireCompletePrintableSignatures()
        {
            var header = Encoding.ASCII.GetBytes("CPYUA 4.3380 3\n");
            // Include a valid name, a nonprintable name, and a truncated signature.
            byte[] body = [0x22, 0x01, 0x6c, 0xc0, 0x11, 0x01, 0x04,
        0x94, 0x04, 0xff, 0xff, 0x01, 0x00, 0x03, (byte)'A', (byte)'B', (byte)'C', 0x0a, 0x00, 0x40, 0x01,
        0x94, 0x04, 0xff, 0xff, 0x01, 0x00, 0x03, (byte)'A', 0x00, (byte)'C', 0x0a, 0x00, 0x40, 0x01,
        0x94, 0x04, 0xff, 0xff, 0x01, 0x00, 0x03, (byte)'X', (byte)'Y', (byte)'Z', 0x0a, 0x00];
            using var stream = new MemoryStream(header.Concat(body).ToArray());
            var parser = new OriginOpjuParser(stream);

            var candidate = Assert.Single(parser.SheetNameCandidates);
            Assert.Equal("ABC", candidate.Name);
            Assert.Equal(header.Length + 14, candidate.Offset);
            Assert.IsType<OriginOpjuParser.TLVRecord>(parser.Records[0]);
            Assert.IsType<OriginOpjuParser.UnknownRegionRecord>(parser.Records[1]);
        }

        /// <summary>Checks that sheet-like signatures embedded inside other records do not become candidates.</summary>
        [Fact]
        public void SheetNameCandidatesIgnoreSignaturesInsideOtherRecords()
        {
            var header = Encoding.ASCII.GetBytes("CPYUA 4.3380 3\n");
            // Put one signature inside a TLV payload and another inside an opaque region.
            byte[] body = [0x22, 0x01, 0x6c, 0xc0, 0x11, 0x01, 0x04,
        0x80, 0x0e, 0x94, 0x04, 0xff, 0xff, 0x01, 0x00, 0x03, (byte)'A', (byte)'B', (byte)'C', 0x0a, 0x00, 0x40, 0x01,
        0x00, 0x94, 0x04, 0xff, 0xff, 0x01, 0x00, 0x03, (byte)'X', (byte)'Y', (byte)'Z', 0x0a, 0x00, 0x40, 0x01];
            using var stream = new MemoryStream(header.Concat(body).ToArray());
            var parser = new OriginOpjuParser(stream);

            Assert.Equal(2, parser.Records.Count);
            Assert.IsType<OriginOpjuParser.TLVRecord>(parser.Records[0]);
            Assert.IsType<OriginOpjuParser.UnknownRegionRecord>(parser.Records[1]);
            Assert.Empty(parser.SheetNameCandidates);
        }

        /// <summary>Checks that malformed sheet-name regions are rejected after a valid marker.</summary>
        [Fact]
        public void SheetNameCandidatesRejectMalformedNameRegions()
        {
            var header = Encoding.ASCII.GetBytes("CPYUA 4.3380 3\n");
            byte[] rootAndMarker = [0x22, 0x01, 0x6c, 0xc0, 0x11, 0x01, 0x04, 0x94, 0x04, 0xff, 0xff, 0x01, 0x00];
            // Test nonprintable, truncated, incorrectly suffixed, and empty names.
            byte[][] invalidRegions = [
              [0x03, (byte)'A', 0x00, (byte)'C', 0x0a, 0x00, 0x40, 0x01],
        [0x03, (byte)'A', (byte)'B', (byte)'C', 0x0a, 0x00],
        [0x03, (byte)'A', (byte)'B', (byte)'C', 0x0a, 0x00, 0x20, 0x01],
        [0x00, 0x0a, 0x00, 0x40, 0x01]
            ];
            foreach (var region in invalidRegions)
            {
                using var stream = new MemoryStream(header.Concat(rootAndMarker).Concat(region).ToArray());
                var parser = new OriginOpjuParser(stream);
                Assert.Empty(parser.SheetNameCandidates);
            }
        }



        [Theory]
        [InlineData("Änderung", -1, 0)]
        [InlineData("Änderung", 0, 0xff)]
        [InlineData("Änderung", 9, 0xff)]
        [InlineData("Revision", -2, 0xa5)]
        [InlineData("Revision", -30, 2)]
        public void KeepsWorksheetSheetNamesUnresolvedWhenMetadataIsIncomplete(string name, int offset, byte replacement)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumberOfSheets", "OneBookOneSheetTwoColumns_3MoreSheets.opju");
            var source = File.ReadAllBytes(path);
            using var originalStream = new MemoryStream(source);
            var original = new OriginOpjuParser(originalStream);
            Assert.Equal(new[] { "Änderung", "Revision", "Riskmanagement", "Meßordner" },
                Assert.Single(original.Books).Sheets.Select(sheet => sheet.Name).ToArray());
            var candidate = Assert.Single(original.SheetNameCandidates, item => item.Name == name);
            source[candidate.Offset + offset] = replacement;

            using var modifiedStream = new MemoryStream(source);
            var modified = new OriginOpjuParser(modifiedStream);
            var sheets = Assert.Single(modified.Books).Sheets;
            Assert.Equal(4, sheets.Count);
            Assert.All(sheets, sheet =>
            {
                Assert.Empty(sheet.Name);
                Assert.Equal(2, sheet.Columns.Count);
            });
        }

        [Fact]
        public void ReadsWorksheetNameFromPrimaryHeaderNotItsRepeatedDisplayMetadata()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumberOfSheets", "OneBookOneSheetTwoColumns_3MoreSheets.opju");
            var source = File.ReadAllBytes(path);
            using var originalStream = new MemoryStream(source);
            var candidate = Assert.Single(new OriginOpjuParser(originalStream).SheetNameCandidates, item => item.Name == "Meßordner");
            var replacement = Encoding.UTF8.GetBytes("NewSheet04");
            Assert.Equal(Encoding.UTF8.GetByteCount(candidate.Name), replacement.Length);
            replacement.CopyTo(source, candidate.Offset);

            using var modifiedStream = new MemoryStream(source);
            var sheets = Assert.Single(new OriginOpjuParser(modifiedStream).Books).Sheets;
            Assert.Equal(new[] { "Änderung", "Revision", "Riskmanagement", "NewSheet04" },
                sheets.Select(sheet => sheet.Name).ToArray());
        }


        [Theory]
        [InlineData("Book1_A@2147483648")]
        [InlineData("Book1_A@999999999999999999999999")]
        [InlineData("Book1_A@-1")]
        [InlineData("Book1_A@+2")]
        public void RejectsUnrepresentableOrSignedSheetOrdinals(string name)
        {
            var parse = typeof(OriginOpjuParser).GetMethod("TryParseWorksheetDatasetName", BindingFlags.Static | BindingFlags.NonPublic)!;
            object?[] arguments = [name, null, null, 0];
            Assert.False((bool)parse.Invoke(null, arguments)!);
        }

        [Fact]
        public void LeavesTextNumericBlankRepresentationUnresolved()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "TextNumericContent", "OneBookOneSheetOneColumnOneBlankTwo.opju");
            using var stream = File.OpenRead(path);
            var column = Assert.Single(new OriginOpjuParser(stream).Books[0].Sheets[0].Columns);
            Assert.NotNull(column.Data);
            Assert.True(column.Data[1].IsEmpty);
            Assert.Equal(1d, column.Data[0].AsDouble());
            Assert.Equal(2d, column.Data[2].AsDouble());
            Assert.NotNull(GetPartialModelProperty(column, "CandidateData"));
        }





        [Fact]
        public void DecodesDoubleOneBitDifferenceExactly()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumericDoubleContent", "OneBookOneSheetOneDoubleColumnOneOneBitDifference.opju");
            using var stream = File.OpenRead(path);
            var column = Assert.Single(new OriginOpjuParser(stream).Books[0].Sheets[0].Columns);
            Assert.NotNull(column.Data);
            Assert.Equal(BitConverter.DoubleToInt64Bits(1.0) + 1, BitConverter.DoubleToInt64Bits(column.Data[1].AsDouble()));
            Assert.True(double.IsNaN(column.Data[2].AsDouble()));
        }

        [Fact]
        public void DecodesDoublePredictorAcrossRepeatedValues()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumericDoubleContent", "OneBookOneSheetOneDoubleColumnTwoRepeatedRuns.opju");
            using var stream = File.OpenRead(path);
            var column = Assert.Single(new OriginOpjuParser(stream).Books[0].Sheets[0].Columns);
            Assert.NotNull(column.Data);
            Assert.Equal(new[] { 1.0, 1.0, 2.0, 2.0 }, column.Data.Take(4).Select(value => value.AsDouble()).ToArray());
            Assert.All(column.Data.Skip(4), value => Assert.True(double.IsNaN(value.AsDouble())));
            Assert.NotNull(GetPartialModelProperty(column, "CandidateData"));
        }





        [Fact]
        public void CandidateDataCannotCrossIndexedImages()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "OneWorksheet.opju");
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);
            int imageIndex = Enumerable.Range(0, parser.Records.Count).First(index => parser.Records[index] is OriginOpjuParser.PngRecord);
            var image = parser.Records[imageIndex];
            var precedingRecord = parser.Records[imageIndex - 1];
            Assert.Equal(image.Offset, precedingRecord.NextOffset);

            var copy = typeof(OriginOpjuParser).GetMethod("TryCopyRecordBytes", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object?[] beforeImage = [imageIndex - 1, image.Offset - 1, 1, null];
            Assert.True((bool)copy.Invoke(parser, beforeImage)!);
            object?[] crossingImage = [imageIndex - 1, image.Offset - 1, 2, null];
            Assert.False((bool)copy.Invoke(parser, crossingImage)!);
            Assert.Null(crossingImage[3]);
        }

        /// <summary>Checks that invalid or incomplete OPJU headers are rejected.</summary>
        /// <param name="content">The malformed ASCII header and root bytes.</param>
        [Theory]
        [InlineData("")]
        [InlineData("CPYUA 4.3380 3")]
        [InlineData("CPYA 4.3380 3\n1234567")]
        [InlineData("CPYUA 4.3380 invalid\n1234567")]
        [InlineData("CPYUA 4.3380 3\n123456")]
        public void RejectsInvalidOrTruncatedHeaders(string content)
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));
            Assert.Throws<InvalidDataException>(() => new OriginOpjuParser(stream));
        }
    }
}
