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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Altaxo.Serialization.Origin
{
    /// <summary>
    /// Reads the header and candidate value records of an Origin OPJU project.
    /// </summary>
    /// <remarks>
    /// The object model and cell data are only partially decoded. Candidate records may occur by chance
    /// inside binary data. See the format research at <see href="https://github.com/exbound/opj"/>,
    /// the engineering specs at <see href="https://github.com/exbound/opj/blob/main/docs/engineering-spec.md"/>
    /// <para>
    /// The parser works in two stages. <see cref="ParseIntoRecords"/> turns the file into a flat list of
    /// candidate <see cref="IRecord"/> entries (see <see cref="Records"/>). The constructor then runs a
    /// sequence of interpretation passes over that list, each of which recovers one property of the
    /// worksheet object model (dataset headers, column designations, long names, units, comments, sheet
    /// names, widths, book metadata). Every pass is self validating: when its expectations are not met it
    /// leaves the affected objects unresolved instead of guessing, so unresolved ownership and raw encoded
    /// data stay separated from the decoded values.
    /// </para>
    /// </remarks>
    public class OriginOpjuParser
    {
        // ----- Record types: the raw, partially reassembled view of the file body -----

        /// <summary>
        /// Interface for the record type used in .opju files.
        /// </summary>
        public interface IRecord
        {
            /// <summary>Gets the byte offset from the beginning of the file.</summary>
            public int Offset { get; }

            /// <summary>
            /// The offset of the next record.
            /// </summary>
            public int NextOffset { get; }

            /// <summary>
            /// Dumps the content to the specified writer.
            /// </summary>
            /// <param name="writer">The writer.</param>
            public virtual void Dump(TextWriter writer)
            {
                writer.WriteLine($"Offset: {Offset:X}, NextOffset: {NextOffset:X}");
            }
        }

        /// <summary>A candidate length-prefixed value in the project stream.</summary>
        /// <param name="Offset">The byte offset from the beginning of the file.</param>
        /// <param name="Tag">The value tag.</param>
        /// <param name="Payload">The bytes following the tag and length.</param>
        [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay,nq}")]
        public record TLVRecord(int Offset, byte Tag, ReadOnlyMemory<byte> Payload) : IRecord
        {
            /// <summary>Gets a one-line view of the tag, length and first payload bytes for the debugger.</summary>
            private string DebuggerDisplay => $"TLV Offs={Offset:X}, Tag={Tag:X2}, L={Payload.Length:X2}, C={Text ?? BitConverter.ToString(Payload.Span[..Math.Min(Payload.Length, 16)].ToArray()).Replace('-', ' ')}";

            /// <summary>Gets the payload as ASCII text when every byte is printable; otherwise, null.</summary>
            public string? Text
            {
                get
                {
                    var bytes = Payload.Span;
                    if (bytes.IsEmpty)
                        return null;
                    foreach (var b in bytes)
                        if (b < 0x20 || b >= 0x7f)
                            return null;
                    return Encoding.UTF8.GetString(bytes); // to be proven: is ASCII the right encoding? A better candidate would be UTF8
                }
            }
            /// <inheritdoc/>
            public int NextOffset => Offset + 2 + Payload.Length;

            /// <summary>
            /// Dumps the content to the specified writer.
            /// </summary>
            /// <param name="writer">The writer.</param>
            public virtual void Dump(TextWriter writer)
            {
                writer.Write($"{Offset:X8}: TLV Tag={Tag:X2} Content={BitConverter.ToString(Payload.Span[..Payload.Length].ToArray()).Replace('-', ' ')}");

                if (Text is not null)
                    writer.Write($" (\"{Text}\")");
                writer.WriteLine();
            }
        }

        /// <summary>A region that could not be interpreted as candidate value records.</summary>
        /// <param name="Offset">The byte offset from the beginning of the file.</param>
        /// <param name="Length">The length in bytes.</param>
        /// <param name="Payload">The bytes following the tag and length.</param>
        [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay,nq}")]
        public record UnknownRegionRecord(int Offset, int Length, ReadOnlyMemory<byte> Payload) : IRecord
        {
            /// <summary>Gets a one-line view of the length and first payload bytes for the debugger.</summary>
            private string DebuggerDisplay => $"UNK Offs={Offset:X}, L={Length:X}, C={BitConverter.ToString(Payload.Span[..Math.Min(Payload.Length, 8)].ToArray()).Replace('-', ' ')}";

            /// <inheritdoc/>
            public int NextOffset => Offset + Length;

            /// <summary>
            /// Dumps the content to the specified writer.
            /// </summary>
            /// <param name="writer">The writer.</param>
            public virtual void Dump(TextWriter writer)
            {
                writer.WriteLine($"{Offset:X8}: UNKNOWN {BitConverter.ToString(Payload.Span[..Math.Min(16, Payload.Length)].ToArray()).Replace('-', ' ')}");

                if (Payload.Length > 16)
                {
                    for (int i = 16; i < Payload.Length; i += 16)
                    {
                        var length = Math.Min(16, Payload.Length - i);
                        writer.WriteLine($"          {Offset + i:X8}: {BitConverter.ToString(Payload.Span.Slice(i, length).ToArray()).Replace('-', ' ')}");
                    }
                }
            }

        }

        /// <summary>
        /// A window description record. In contrast to the other candidate records, its payload is not opaque: it
        /// itself consists of a name field followed by a run of child records and a trailing block.
        /// </summary>
        /// <remarks>
        /// A window description is the only observed record whose declared length exceeds the length limit that the
        /// scanner uses to resynchronize, so it is never recognized as a plain candidate record. Its payload is
        /// <c>&lt;name field length&gt; 00 00 &lt;name&gt;</c>, then the child records, then a trailing block that begins
        /// where the child records end and that carries e.g. the long name of the window.
        /// </remarks>
        /// <param name="Offset">The byte offset of the tag from the beginning of the file.</param>
        /// <param name="Tag">The tag of the record.</param>
        /// <param name="Payload">The bytes between the length byte and the end of the record.</param>
        /// <param name="Name">The window name taken from the name field of the payload.</param>
        /// <param name="Children">The child records of the payload in file order.</param>
        [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay,nq}")]
        public sealed record WindowRecord(int Offset, byte Tag, ReadOnlyMemory<byte> Payload, string Name, IReadOnlyList<TLVRecord> Children) : IRecord
        {
            /// <summary>Gets a one-line view of the window name and child count for the debugger.</summary>
            private string DebuggerDisplay => $"WIN Offs={Offset:X}, Tag={Tag:X2}, L={Payload.Length:X2}, Name={Name}, Children={Children.Count}";

            /// <inheritdoc/>
            public int NextOffset => Offset + 2 + Payload.Length;

            /// <summary>
            /// Dumps the content, including the child records, to the specified writer.
            /// </summary>
            /// <param name="writer">The writer.</param>
            public void Dump(TextWriter writer)
            {
                writer.WriteLine($"{Offset:X8}: WINDOW Tag={Tag:X2} Length={Payload.Length:X2} Name=\"{Name}\" Children={Children.Count}");
                foreach (var child in Children)
                    writer.WriteLine($"          {child.Offset:X8}:   TLV Tag={child.Tag:X2} Content={BitConverter.ToString(child.Payload.Span[..Math.Min(16, child.Payload.Length)].ToArray()).Replace('-', ' ')}");
            }
        }

        /// <summary>
        /// Represents the preview image table record, which contains the offsets and lengths of the embedded PNG images in the OPJU file.
        /// </summary>
        /// <param name="Offset">The byte offset of the table in the file.</param>
        /// <param name="Length">The length of the table in bytes, including its 16 byte prefix.</param>
        /// <param name="Payload">The table bytes.</param>
        [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay,nq}")]
        public record ImageTable(int Offset, int Length, ReadOnlyMemory<byte> Payload) : UnknownRegionRecord(Offset, Length, Payload)
        {
            /// <summary>Gets a one-line view of the length and first payload bytes for the debugger.</summary>
            private string DebuggerDisplay => $"ITA Offs={Offset:X}, L={Length:X}, C={BitConverter.ToString(Payload.Span[..Math.Min(Payload.Length, 8)].ToArray()).Replace('-', ' ')}";

            /// <summary>
            /// Dumps the content to the specified writer.
            /// </summary>
            /// <param name="writer">The writer.</param>
            public override void Dump(TextWriter writer)
            {
                writer.WriteLine($"{Offset:X8} IMAGETABLE (Content not dumped here)");
            }
        }

        /// <summary>A PNG image located by the observed embedded-image index.</summary>
        /// <param name="Offset">The byte offset from the beginning of the file.</param>
        /// <param name="Payload">The complete PNG image bytes.</param>
        [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay,nq}")]
        public record PngRecord(int Offset, ReadOnlyMemory<byte> Payload) : IRecord
        {
            /// <summary>Gets a one-line view of the image offset and length for the debugger.</summary>
            private string DebuggerDisplay => $"PNG Offs={Offset:X}, L={Payload.Length:X}";
            /// <inheritdoc/>
            public int NextOffset => Offset + Payload.Length;

            /// <summary>
            /// Dumps the content to the specified writer.
            /// </summary>
            /// <param name="writer">The writer.</param>
            public virtual void Dump(TextWriter writer)
            {
                writer.WriteLine($"{Offset:X8} PNGIMAGE (Content not dumped here)");
            }
        }

        /// <summary>A possible sheet name recovered from an observed binary signature.</summary>
        /// <param name="Offset">The byte offset of the name in the file.</param>
        /// <param name="Name">The decoded sheet name.</param>
        public sealed record SheetNameCandidate(int Offset, string Name);

        /// <summary>A worksheet book (workbook) assembled from the dataset names of its columns.</summary>
        /// <param name="Name">The book name, which is the part before the last underscore of a dataset name.</param>
        /// <param name="Label">The book label. Not recovered yet, therefore always <see langword="null"/>.</param>
        /// <param name="Sheets">The sheets of the book, addressable by their one-based sheet number.</param>
        public record Book(string? Name, string? Label, IReadOnlyList<Sheet> Sheets)
        {
            /// <summary>Gets or sets the project tree path of the folder the book is stored in; empty when unresolved.</summary>
            public string FolderName { get; set; } = string.Empty;
            /// <summary>Gets or sets the long name of the book; empty when it was not recovered.</summary>
            public string LongName { get; set; } = string.Empty;
            /// <summary>Gets or sets the comment of the book; empty when it was not recovered.</summary>
            public string Comment { get; set; } = string.Empty;
        }

        /// <summary>A worksheet sheet, identified by the sheet number encoded in the dataset names.</summary>
        /// <param name="Number">The one-based sheet number within the book.</param>
        /// <param name="RowCount">The number of rows. Not recovered yet, therefore always <see langword="null"/>.</param>
        /// <param name="Columns">The columns of the sheet in file order.</param>
        public record Sheet(int Number, int? RowCount, IReadOnlyList<AbstractColumn> Columns)
        {
            /// <summary>Gets or sets the sheet name; empty when it was not recovered.</summary>
            public string Name { get; set; } = string.Empty;
        }

        /// <summary>
        /// Base class of a worksheet dataset (column) recovered from the project stream.
        /// </summary>
        public abstract class AbstractColumn
        {
            /// <summary>Initializes a new instance of the <see cref="AbstractColumn"/> class.</summary>
            /// <param name="datasetName">The dataset name as stored in the file.</param>
            /// <param name="descriptorRecords">The records that make up the dataset header.</param>
            protected AbstractColumn(string datasetName, IReadOnlyList<IRecord> descriptorRecords)
            {
                DatasetName = datasetName;
                DescriptorRecords = descriptorRecords;
            }

            /// <summary>Gets the raw dataset name, e.g. <c>Book1_A</c> for sheet 1 or <c>Book1_A@2</c> for sheet 2.</summary>
            public string DatasetName { get; }
            /// <summary>Gets or sets the column name part of the dataset name; <see langword="null"/> while the book and sheet could not be resolved.</summary>
            public string? Name { get; set; }
            /// <summary>Gets the records that form the dataset header, from the first recognized header record up to the value block envelope.</summary>
            public IReadOnlyList<IRecord> DescriptorRecords { get; }
            // Null means the values have not been decoded, not that the column is empty.
            /// <summary>Gets the decoded cell values, or <see langword="null"/> when the value block was not decoded.</summary>
            public IReadOnlyList<Variant>? Values { get; init; }

            /// <summary>Gets or sets the role of the column in a plot; <see cref="SpreadColumnType.Ignore"/> when unresolved.</summary>
            public SpreadColumnType SpreadColumnType { get; set; } = SpreadColumnType.Ignore;

            /// <summary>Gets or sets the declared value type of the column; <see langword="null"/> when unresolved.</summary>
            public ValueType? ValueType { get; set; }

            /// <summary>Gets or sets the data type the value block was decoded as; <see langword="null"/> when the block was not decoded.</summary>
            public DeserializedDataType? DeserializedDataType { get; set; }
            /// <summary>Gets or sets the long name of the column; empty when not recovered.</summary>
            public string LongName { get; set; } = string.Empty;
            /// <summary>Gets or sets the unit of the column; empty when not recovered or not present.</summary>
            public string Unit { get; set; } = string.Empty;
            /// <summary>Gets or sets the comment of the column; empty when not recovered or not present.</summary>
            public string Comment { get; set; } = string.Empty;
            /// <summary>Gets or sets the display width in column units; 0 when not recovered.</summary>
            public double Width { get; set; }
            /// <summary>Gets or sets the format of the column values; <see cref="ValueTypeSpecification.Numeric_Decimal"/> when unresolved.</summary>
            public ValueTypeSpecification ValueTypeSpecification { get; set; }

            /// <summary>Gets or sets the records that carry the column designation and value type; <see langword="null"/> when not recovered.</summary>
            internal IReadOnlyList<IRecord>? TypeDescriptorRecords { get; set; }

        }

        /// <summary>A worksheet dataset whose value block is introduced by a 24 byte envelope record.</summary>
        public sealed class DatasetColumn : AbstractColumn
        {
            /// <summary>Initializes a new instance of the <see cref="DatasetColumn"/> class.</summary>
            /// <param name="datasetName">The dataset name as stored in the file.</param>
            /// <param name="elementWidth">The element width byte of the width record that introduced the dataset.</param>
            /// <param name="descriptorRecords">The records that make up the dataset header.</param>
            /// <param name="dataStartRecord">The record in which the value block starts.</param>
            /// <param name="dataOffset">The byte offset of the value block within the file.</param>
            /// <param name="candidateDataLength">The element count announced by the envelope, when the envelope carries one.</param>
            /// <param name="candidateData">The bytes the scanner could provide for the value block.</param>
            public DatasetColumn(string datasetName, byte elementWidth, IReadOnlyList<IRecord> descriptorRecords,
                IRecord dataStartRecord, int dataOffset, ulong? candidateDataLength, ReadOnlyMemory<byte>? candidateData)
                : base(datasetName, descriptorRecords)
            {
                ElementWidth = elementWidth;
                DataStartRecord = dataStartRecord;
                DataOffset = dataOffset;
                CandidateDataLength = candidateDataLength;
                CandidateData = candidateData;
            }

            /// <summary>Gets the element width byte: 1, 2, 4, 8, 16, 50 or 10 (text/numeric) select the value block layout.</summary>
            internal byte ElementWidth { get; }
            /// <summary>Gets the record in which the value block starts.</summary>
            internal IRecord DataStartRecord { get; }
            /// <summary>Gets the file offset of the value block, i.e. the offset just behind the envelope's two byte block prefix.</summary>
            internal int DataOffset { get; }
            /// <summary>Gets the element count announced by the envelope, or <see langword="null"/> when the envelope does not carry one.</summary>
            internal ulong? CandidateDataLength { get; }
            // The length and bytes are hypotheses; neither establishes a native block boundary.
            /// <summary>Gets the bytes the scanner made available for the value block, or <see langword="null"/> when they were not available.</summary>
            internal ReadOnlyMemory<byte>? CandidateData { get; }
            /// <summary>Gets the number of rows the value block was decoded to, or <see langword="null"/> when it was not decoded.</summary>
            internal int? DataRowCount { get; init; }

        }


        /// <summary>A matrix dataset together with the dimensions inferred from the surrounding records.</summary>
        /// <param name="Name">The dataset name of the matrix.</param>
        /// <param name="NumberOfRows">The inferred row count, or <see langword="null"/> when it could not be inferred.</param>
        /// <param name="NumberOfColumns">The inferred column count, or <see langword="null"/> when it could not be inferred.</param>
        private sealed record MatrixCandidate(string Name, int? NumberOfRows, int? NumberOfColumns);

        /// <summary>The matrix dimensions inferred by <see cref="FindMatrixDatasetCandidates"/>; kept for later use.</summary>
        private IReadOnlyList<MatrixCandidate> _matrixCandidates = [];

        /// <summary>Gets the format version from the file header.</summary>
        public string FormatVersion { get; private set; }

        /// <summary>Gets the object count reported by the file header.</summary>
        public int ObjectCount { get; private set; }

        /// <summary>Gets the offset immediately following the header line.</summary>
        public int BodyOffset { get; private set; }

        /// <summary>Gets the seven-byte root header.</summary>
        public ReadOnlyMemory<byte> RootHeader { get; private set; }

        /// <summary>The records built up by <see cref="ParseIntoRecords"/> in ascending offset order.</summary>
        private List<IRecord> _records = new List<IRecord>();

        /// <summary>
        /// The maximum payload length a plain candidate record may declare. Longer records are only accepted as
        /// window descriptions (see <see cref="TryReadWindowRecord"/>), because a longer length byte would make the
        /// resynchronization after an unknown region unreliable.
        /// </summary>
        private const int MaxTLVLength = 0x60;
        /// <summary>The complete project bytes, used where raw byte access is needed instead of the record view.</summary>
        private byte[] _fileData = [];

        /// <summary>Gets candidate records recovered by scanning and resynchronizing the body.</summary>
        public IReadOnlyList<IRecord> Records { get; }



        /// <summary>Gets possible worksheet or matrix sheet names from an observed sheet signature.</summary>
        /// <remarks>The sheet type and parent book cannot be established from these matches alone.</remarks>
        public IReadOnlyList<SheetNameCandidate> SheetNameCandidates { get; }

        /// <summary>Gets the books resolved from the worksheet dataset names, in the order the datasets occur in the file.</summary>
        public IReadOnlyList<Book> Books { get; }

        /// <summary>Gets the datasets whose names do not follow the Book_Column@Sheet pattern and therefore have no resolved owner.</summary>
        internal IReadOnlyList<AbstractColumn> UnassociatedColumns { get; }

        /// <summary>Reads an OPJU project from the beginning of a stream without closing it.</summary>
        /// <param name="originFile">A readable, seekable stream containing the project.</param>
        /// <remarks>
        /// The passes below run in a fixed order: the dataset headers are located first, because the later passes
        /// match designations, long names and widths positionally against the datasets found in file order. A pass
        /// that cannot prove this correspondence leaves the property unresolved instead of guessing.
        /// </remarks>
        /// <exception cref="ArgumentException">The stream is not readable or seekable.</exception>
        /// <exception cref="InvalidDataException">The header is invalid or incomplete.</exception>
        public OriginOpjuParser(Stream originFile)
        {
            ArgumentNullException.ThrowIfNull(originFile);
            if (!originFile.CanRead)
                throw new ArgumentException("The project stream must be readable and seekable.", nameof(originFile));

            // Read records data into memory.
            Records = ParseIntoRecords(originFile);

            // Interpret the parsed records; matrix inference uses the sheet candidates.
            SheetNameCandidates = FindSheetNameCandidates();
            MatrixDatasetCandidates = FindMatrixDatasetCandidates();
            var worksheetColumns = FindWorksheetDatasetCandidates();
            ApplyWorksheetColumnDesignations(worksheetColumns);
            ApplyWorksheetColumnLongnames(worksheetColumns);
            (var books, UnassociatedColumns) = GroupWorksheetDatasets(worksheetColumns);
            // A worksheet without any column produces no dataset name, so its book has to be taken from its window.
            Books = AddWindowOnlyBooks(books);
            ApplyWorksheetSheetNames();
            ApplyWorksheetColumnWidths();
            ApplyWorksheetBookMetadata();
        }

        // ----- Interpretation passes: worksheet books, sheets and their columns -----

        /// <summary>
        /// Applies the long name and the comment of every book that is shown in exactly one window.
        /// </summary>
        /// <remarks>
        /// The long name of a book is stored behind a fixed marker inside the description of its window; the comment
        /// of the window, if it has one, follows in the same text behind a line break. The searched
        /// area ends at the next window candidate or at the first indexed preview image. A book is only given a long
        /// name when its name occurs exactly once among the books and among the windows, and when the area contains
        /// exactly one decodable long name.
        /// </remarks>
        private void ApplyWorksheetBookMetadata()
        {
            var windows = FindWindowCandidates();
            ReadOnlySpan<byte> longNameMarker = [0x80, 0x01, 0xfc, 0xc0, 0xff, 0x02, 0x00, 0x39, 0x8c, 0x01, 0x48, 0x8b, 0x04, 0xff, 0x7f, 0x00, 0x00];
            for (int w = 0; w < windows.Count; ++w)
            {
                Book? book = null;
                int bookCount = 0;
                int windowCount = 0;
                // A book can be attributed to a window only if its name occurs exactly once as a book and once as a window.
                foreach (var candidate in Books)
                    if (candidate.Name == windows[w].Name)
                    {
                        book = candidate;
                        ++bookCount;
                    }
                foreach (var window in windows)
                    if (window.Name == windows[w].Name)
                        ++windowCount;
                if (book is null || bookCount != 1 || windowCount != 1)
                    continue;

                // The long name of a window description is stored inside the record itself, so the search area
                // starts at the record and not behind it.
                var windowRecord = Records[windows[w].Index];
                int start = windowRecord is WindowRecord ? windowRecord.Offset : windowRecord.NextOffset;
                int end = w + 1 < windows.Count ? Records[windows[w + 1].Index].Offset : _fileData.Length;
                for (int i = windows[w].Index + 1; i < Records.Count && Records[i].Offset < end; ++i)
                    if (Records[i] is PngRecord)
                    {
                        end = Records[i].Offset;
                        break;
                    }
                string? longName = null;
                string? comment = null;
                bool ambiguous = false;
                // Behind the marker follows a length byte, the text bytes, a NUL byte and a 0x0a terminator.
                for (int p = start; p <= end - longNameMarker.Length - 1; ++p)
                {
                    if (!_fileData.AsSpan(p, longNameMarker.Length).SequenceEqual(longNameMarker))
                        continue;
                    int lengthOffset = p + longNameMarker.Length;
                    int length = _fileData[lengthOffset];
                    if (length == 0 || lengthOffset + 1 + length >= end ||
                        _fileData[lengthOffset + length] != 0 || _fileData[lengthOffset + length + 1] != 0x0a)
                        continue;
                    try
                    {
                        var nameBytes = _fileData.AsSpan(lengthOffset + 1, length - 1);
                        if (nameBytes.Contains((byte)0))
                            continue;
                        var text = new UTF8Encoding(false, true).GetString(nameBytes);
                        // The text holds the long name and, behind a line break, the comment of the window.
                        int lineBreak = text.IndexOf("\r\n", StringComparison.Ordinal);
                        ambiguous |= longName is not null;
                        longName = lineBreak < 0 ? text : text[..lineBreak];
                        comment = lineBreak < 0 ? null : text[(lineBreak + 2)..];
                    }
                    catch (DecoderFallbackException)
                    {
                        // Invalid candidates do not establish metadata.
                    }
                }
                if (!ambiguous && longName is not null)
                {
                    book.LongName = longName;
                    if (comment is not null)
                        book.Comment = comment;
                }
            }

            ApplyWorksheetFolders(windows);
        }

        /// <summary>
        /// Applies the folder path of the project tree to the books listed in it.
        /// </summary>
        /// <remarks>
        /// The project tree refers to the book windows by their ordinal within <paramref name="windows"/>. The tree is
        /// accepted only when it parses without leftovers and assigns every window exactly once; the path is then still
        /// applied to a book only if that book and its window are uniquely named.
        /// </remarks>
        /// <param name="windows">The window candidates in file order.</param>
        private void ApplyWorksheetFolders(List<(int Index, string Name)> windows)
        {
            // The project-tree block follows the final indexed preview and has a bounded native length.
            int afterImages = 0;
            foreach (var record in Records)
                if (record is PngRecord)
                    afterImages = Math.Max(afterImages, record.NextOffset);
            if (afterImages == 0)
                return;
            ReadOnlySpan<byte> treeMarker = [0x80, 0x06, 0x04, 0xde, 0x36, 0x10, 0x03, 0x89];
            var assignments = new Dictionary<int, string>();
            Dictionary<int, string>? resolved = null;
            for (int start = afterImages + 5; start <= _fileData.Length - treeMarker.Length; ++start)
            {
                if (_fileData[start - 1] != 0x0a || !_fileData.AsSpan(start, treeMarker.Length).SequenceEqual(treeMarker))
                    continue;
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(_fileData.AsSpan(start - 5, 4));
                if (length < 4 + treeMarker.Length || length - 4 > _fileData.Length - start)
                    continue;
                int end = start + (int)length - 4;
                int position = start + treeMarker.Length;
                assignments.Clear();
                if (!TryReadFolder(ref position, end, string.Empty, true, 0, assignments, windows.Count) ||
                    assignments.Count != windows.Count || position > end || end - position > 3)
                    continue;
                bool trailingZeros = true;
                for (int i = position; i < end; ++i)
                    trailingZeros &= _fileData[i] == 0;
                if (!trailingZeros)
                    continue;
                if (resolved is not null)
                    return;
                resolved = new Dictionary<int, string>(assignments);
            }
            if (resolved is null)
                return;
            foreach (var entry in resolved)
            {
                string name = windows[entry.Key].Name;
                int windowCount = 0;
                int bookCount = 0;
                Book? book = null;
                foreach (var window in windows)
                    if (window.Name == name)
                        ++windowCount;
                foreach (var candidate in Books)
                    if (candidate.Name == name)
                    {
                        book = candidate;
                        ++bookCount;
                    }
                if (windowCount == 1 && bookCount == 1)
                    book!.FolderName = entry.Value;
            }
        }

        /// <summary>
        /// Reads one folder entry of the project tree and maps the files it lists to the folder path.
        /// </summary>
        /// <param name="position">On input the offset of the folder entry, on output the offset directly behind it.</param>
        /// <param name="end">The exclusive end of the project tree block.</param>
        /// <param name="parent">The path of the parent folder.</param>
        /// <param name="root"><see langword="true"/> for the invisible root entry, whose own name is not part of the path.</param>
        /// <param name="depth">Current recursion depth, limited so that a corrupted block cannot recurse endlessly.</param>
        /// <param name="assignments">Receives the file ordinal to folder path mapping; a duplicate ordinal rejects the block.</param>
        /// <param name="windowCount">The number of window candidates, which bounds the number of files of a folder.</param>
        /// <returns><see langword="true"/> if the folder entry was read completely; otherwise, <see langword="false"/>.</returns>
        private bool TryReadFolder(ref int position, int end, string parent, bool root, int depth,
            Dictionary<int, string> assignments, int windowCount)
        {
            if (depth > 64 || position + 2 > end || _fileData[position] != 0x80)
                return false;
            int headerLength = _fileData[position + 1];
            position += 2;
            if (headerLength < 18 || headerLength > end - position || position + headerLength + 6 > end)
                return false;
            position += headerLength;
            if (_fileData[position++] != 0)
                return false;
            uint nameLength = BinaryPrimitives.ReadUInt32LittleEndian(_fileData.AsSpan(position, 4));
            position += 4;
            if (nameLength < 1 || nameLength > end - position - 1 || _fileData[position++] != 0x0a ||
                _fileData[position + (int)nameLength - 1] != 0)
                return false;
            string name;
            try
            {
                var nameBytes = _fileData.AsSpan(position, (int)nameLength - 1);
                if (nameBytes.Contains((byte)0))
                    return false;
                name = new UTF8Encoding(false, true).GetString(nameBytes);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            position += (int)nameLength;
            ReadOnlySpan<byte> folderMarker = [0x0a, 0x04, 0x07, 0x01, 0x47, 0xc0, 0x11, 0x01, 0x01, 0x9c, 0x0a];
            if (folderMarker.Length > end - position || !_fileData.AsSpan(position, folderMarker.Length).SequenceEqual(folderMarker))
                return false;
            position += folderMarker.Length;
            if (position >= end)
                return false;
            int propertiesLength = _fileData[position++];
            if (propertiesLength > 0)
            {
                if (propertiesLength >= end - position || _fileData[position + propertiesLength] != 0x0a)
                    return false;
                position += propertiesLength + 1;
            }
            ReadOnlySpan<byte> entriesMarker = [0x04, 0xa1, 0x01, 0x64, 0x80, 0x0a, 0x02, 0x75, 0x62, 0x0a];
            if (entriesMarker.Length > end - position || !_fileData.AsSpan(position, entriesMarker.Length).SequenceEqual(entriesMarker))
                return false;
            position += entriesMarker.Length;
            if (position >= end || (_fileData[position] & 1) != 0)
                return false;
            int fileCount = _fileData[position++] / 2;
            string path = root ? string.Empty : parent.Length == 0 ? name : parent + "\\" + name;
            if (!root && (name.Length == 0 || name.Contains('\\')))
                return false;
            for (int i = 0; i < fileCount; ++i)
            {
                // Every file entry consists of a zero byte, a single value record and a terminating zero byte. The
                // value record either carries the window ordinal explicitly (0x81 01 <ordinal> 80) or is the short
                // form of the entry of the window with ordinal 0 (0x85). Other window types, a notes window for
                // example, use a value record whose meaning is not known; their entries are recognized so that the
                // following entries and sub folders stay readable, but they contribute no assignment.
                // The byte behind the last entry is the number of sub folders.
                if (end - position < 4 || _fileData[position] != 0 || _fileData[position + 1] != 0x80)
                    return false;
                int valueLength = _fileData[position + 2];
                if (valueLength < 1 || end - position < 4 + valueLength || _fileData[position + 3 + valueLength] != 0)
                    return false;
                var value = _fileData.AsSpan(position + 3, valueLength);
                int ordinal = -1;
                if (valueLength == 1 && value[0] == 0x85)
                    ordinal = 0;
                else if (valueLength == 4 && value[0] == 0x81 && value[1] == 0x01 && value[3] == 0x80)
                    ordinal = value[2];
                if (ordinal >= 0 && (ordinal >= windowCount || !assignments.TryAdd(ordinal, path)))
                    return false;
                position += 4 + valueLength;
            }
            if (position >= end || (_fileData[position] & 1) != 0)
                return false;
            int folderCount = _fileData[position++] / 2;
            if (folderCount > end - position)
                return false;
            // The sub folders follow and extend the path of their parent.
            for (int i = 0; i < folderCount; ++i)
                if (!TryReadFolder(ref position, end, path, false, depth + 1, assignments, windowCount))
                    return false;
            return true;
        }

        /// <summary>
        /// Adds a book for every window that describes a worksheet without a single column.
        /// </summary>
        /// <param name="books">The books that were derived from the dataset names of their columns.</param>
        /// <returns>The given books followed by one book per column-less worksheet window.</returns>
        /// <remarks>
        /// A book is normally derived from the dataset names of its columns, so a worksheet without columns has no
        /// book at all. Such a book is created from its window candidate, but only when the window area contains a
        /// complete, numbered sequence of sheet names whose column count is zero, because that signature is only
        /// produced by an empty worksheet sheet.
        /// </remarks>
        private IReadOnlyList<Book> AddWindowOnlyBooks(IReadOnlyList<Book> books)
        {
            var windows = FindWindowCandidates();
            var result = new List<Book>(books);
            for (int w = 0; w < windows.Count; ++w)
            {
                bool hasBook = false;
                foreach (var book in result)
                    hasBook |= book.Name == windows[w].Name;
                if (hasBook)
                    continue;

                int end = w + 1 < windows.Count ? windows[w + 1].Index : Records.Count;
                var sheets = new List<Sheet>();
                bool isColumnlessWorksheet = false;
                for (int i = windows[w].Index + 1; i < end; ++i)
                {
                    if (!TryReadWorksheetSheetName(i, out _, out int number, out int columns))
                        continue;
                    // Any sheet with columns belongs to a book that the dataset names already resolved.
                    if (columns != 0 || number != sheets.Count + 1)
                    {
                        isColumnlessWorksheet = false;
                        break;
                    }
                    sheets.Add(new Sheet(number, null, []));
                    isColumnlessWorksheet = true;
                }

                if (isColumnlessWorksheet)
                    result.Add(new Book(windows[w].Name, null, sheets));
            }
            return result;
        }

        /// <summary>
        /// Applies the sheet names found in the window area of a book to the sheets of that book.
        /// </summary>
        /// <remarks>
        /// The names are applied only when their count, their one-based numbers and their column counts match the
        /// sheets of the book exactly. A book with an ambiguous or missing window candidate keeps its sheet names empty.
        /// </remarks>
        private void ApplyWorksheetSheetNames()
        {
            var windows = FindWindowCandidates();
            foreach (var book in Books)
            {
                int windowIndex = -1;
                bool ambiguous = false;
                for (int i = 0; i < windows.Count; ++i)
                {
                    if (windows[i].Name != book.Name)
                        continue;
                    ambiguous |= windowIndex >= 0;
                    windowIndex = i;
                }
                if (ambiguous || windowIndex < 0)
                    continue;

                int end = windowIndex + 1 < windows.Count ? windows[windowIndex + 1].Index : Records.Count;
                var names = new List<(int Number, int Columns, string Name)>();
                for (int i = windows[windowIndex].Index + 1; i < end; ++i)
                    if (TryReadWorksheetSheetName(i, out var candidate, out int number, out int columns) &&
                        (long)candidate.Offset + Encoding.UTF8.GetByteCount(candidate.Name) + 4 <= Records[end - 1].NextOffset)
                        names.Add((number, columns, candidate.Name));

                // Resolve ownership only within a unique book window with a complete, numbered sheet sequence.
                if (names.Count != book.Sheets.Count)
                    continue;
                bool matches = true;
                for (int i = 0; i < names.Count; ++i)
                    matches &= names[i].Number == i + 1 && names[i].Columns == book.Sheets[i].Columns.Count;
                if (!matches)
                    continue;
                for (int i = 0; i < names.Count; ++i)
                    book.Sheets[i].Name = names[i].Name;
            }
        }

        /// <summary>
        /// Applies the display widths of the columns of a book, taken from the width records in its window area.
        /// </summary>
        /// <remarks>
        /// Widths are stored little-endian in tenths of a column unit in a <c>0x8d</c> record. Because the short
        /// layout is ambiguous in workbooks that use the format code <c>0x20</c>, two candidate lists are collected
        /// and the one whose length matches the number of columns of the book wins. If neither matches, no width is
        /// applied at all rather than assigning widths to the wrong columns.
        /// </remarks>
        private void ApplyWorksheetColumnWidths()
        {
            var windows = FindWindowCandidates();
            foreach (var book in Books)
            {
                int windowIndex = -1;
                bool ambiguous = false;
                for (int i = 0; i < windows.Count; ++i)
                {
                    if (windows[i].Name != book.Name)
                        continue;
                    ambiguous |= windowIndex >= 0;
                    windowIndex = i;
                }
                if (ambiguous || windowIndex < 0)
                    continue;

                int end = windowIndex + 1 < windows.Count ? windows[windowIndex + 1].Index : Records.Count;
                for (int i = windows[windowIndex].Index + 1; i < end; ++i)
                    if (Records[i] is PngRecord)
                    {
                        end = i;
                        break;
                    }

                var columns = new List<AbstractColumn>();
                foreach (var sheet in book.Sheets)
                    columns.AddRange(sheet.Columns);
                if (columns.Count == 0)
                    continue;

                // The short width layout is introduced by a single byte format record. Most workbooks use
                // the format code 0x60 there, some use 0x20. Where 0x20 is used the long layout is not
                // present, so the two variants are collected separately and the one that adds up to the
                // number of columns wins.
                var widths = new List<double>();
                var widthsWithShortFormat20 = new List<double>();
                for (int i = windows[windowIndex].Index + 1; i + 1 < end; ++i)
                {
                    if (Records[i] is TLVRecord shortPrefix && shortPrefix.Tag == 0x8f &&
                        shortPrefix.Payload.Length == 1 &&
                        (shortPrefix.Payload.Span[0] == 0x60 || shortPrefix.Payload.Span[0] == 0x20) &&
                        Records[i + 1] is TLVRecord shortWidth && shortWidth.Tag == 0x8d &&
                        shortWidth.Payload.Length is 1 or 2 && shortPrefix.NextOffset == shortWidth.Offset &&
                        shortWidth.NextOffset <= Records[end - 1].NextOffset - 6 &&
                        _fileData.AsSpan(shortWidth.NextOffset, 6).SequenceEqual(new byte[] { 0xbf, 0xbf, 0xbf, 0xbf, 0xbf, 0xbf })
                    )
                    {
                        ushort shortEncodedWidth = shortWidth.Payload.Length == 1
                            ? shortWidth.Payload.Span[0]
                            : BinaryPrimitives.ReadUInt16LittleEndian(shortWidth.Payload.Span);
                        if (shortEncodedWidth > 0)
                        {
                            if (shortPrefix.Payload.Span[0] == 0x60)
                            {
                                widths.Add(shortEncodedWidth / 10.0);
                                ++i;
                            }
                            widthsWithShortFormat20.Add(shortEncodedWidth / 10.0);
                        }
                        if (shortPrefix.Payload.Span[0] == 0x60)
                            continue;
                    }

                    if (i + 2 >= end || Records[i] is not TLVRecord prefix ||
                        Records[i + 1] is not TLVRecord marker || marker.Payload.Length != 1 ||
                        Records[i + 2] is not TLVRecord width || width.Tag != 0x8d ||
                        width.Payload.Length is < 1 or > 2 ||
                        prefix.NextOffset != marker.Offset || marker.NextOffset != width.Offset)
                        continue;

                    bool standardLayout = prefix.Tag == 0x89 && prefix.Payload.Span.SequenceEqual(new byte[] { 0x01 }) &&
                        marker.Tag == 0x82;
                    // In empty numeric columns the scanner can combine the count with the preceding descriptor.
                    bool combinedLayout = marker.Tag == 0x8f &&
                        ((prefix.Tag == 0x8f && prefix.Payload.Span.SequenceEqual(new byte[] { 0x01, 0x60 }) &&
                          marker.Payload.Span[0] != 0x60) ||
                         (prefix.Tag == 0x8d && prefix.Payload.Length == 4 && prefix.Payload.Span[0] is >= 1 and <= 9 &&
                          marker.Payload.Span[0] != 0x60 &&
                          (prefix.Payload.Span[1..].SequenceEqual(new byte[] { 0x00, 0x01, 0x60 }) ||
                           prefix.Payload.Span[1..].SequenceEqual(new byte[] { 0x00, 0x00, 0x60 }))) ||
                         (prefix.Tag == 0x83 && prefix.Payload.Span.SequenceEqual(new byte[] { 0x21, 0x61 }) &&
                          marker.Payload.Span.SequenceEqual(new byte[] { 0x20 }))) &&
                        width.NextOffset <= Records[end - 1].NextOffset - 6 &&
                        _fileData.AsSpan(width.NextOffset, 6).SequenceEqual(new byte[] { 0xbf, 0xbf, 0xbf, 0xbf, 0xbf, 0xbf });
                    if (!standardLayout && !combinedLayout)
                        continue;

                    // Observed display widths are stored as little-endian tenths of a column unit.
                    ushort encodedWidth = width.Payload.Length == 1
                        ? width.Payload.Span[0]
                        : BinaryPrimitives.ReadUInt16LittleEndian(width.Payload.Span);
                    if (encodedWidth > 0)
                        widths.Add(encodedWidth / 10.0);
                }

                if (widths.Count != columns.Count)
                {
                    if (widthsWithShortFormat20.Count != columns.Count)
                        continue;
                    widths = widthsWithShortFormat20;
                }
                for (int i = 0; i < columns.Count; ++i)
                    columns[i].Width = widths[i];
            }
        }

        /// <summary>
        /// Groups the located datasets into books and sheets according to their dataset names.
        /// </summary>
        /// <param name="columns">The located datasets in file order.</param>
        /// <returns>The books in order of first occurrence, and the datasets whose names carry no resolvable owner.</returns>
        /// <remarks>The grouping is derived from the dataset names alone; it is not confirmed by the file structure.</remarks>
        private static (IReadOnlyList<Book> Books, IReadOnlyList<AbstractColumn> UnassociatedColumns) GroupWorksheetDatasets(
            IReadOnlyList<AbstractColumn> columns)
        {
            var groups = new Dictionary<string, Dictionary<int, List<AbstractColumn>>>(StringComparer.Ordinal);
            var bookOrder = new List<string>();
            var unassociated = new List<AbstractColumn>();
            foreach (var column in columns)
            {
                if (!TryParseWorksheetDatasetName(column.DatasetName, out var bookName, out var columnName, out int sheetNumber))
                {
                    unassociated.Add(column);
                    continue;
                }

                if (!groups.TryGetValue(bookName, out var sheets))
                {
                    sheets = new Dictionary<int, List<AbstractColumn>>();
                    groups.Add(bookName, sheets);
                    bookOrder.Add(bookName);
                }
                if (!sheets.TryGetValue(sheetNumber, out var sheetColumns))
                {
                    sheetColumns = new List<AbstractColumn>();
                    sheets.Add(sheetNumber, sheetColumns);
                }
                column.Name = columnName;
                sheetColumns.Add(column);
            }

            var books = new List<Book>();
            foreach (var bookName in bookOrder)
            {
                var sheets = new Dictionary<int, Sheet>();
                int sheetCount = 0;
                foreach (var entry in groups[bookName])
                {
                    sheets.Add(entry.Key, new Sheet(entry.Key, null, entry.Value.AsReadOnly()));
                    sheetCount = Math.Max(sheetCount, entry.Key);
                }
                books.Add(new Book(bookName, null, new NumberedSheets(sheetCount, sheets)));
            }
            return (books.AsReadOnly(), unassociated.AsReadOnly());
        }

        /// <summary>
        /// Splits a worksheet dataset name into its book, column and sheet number parts.
        /// </summary>
        /// <param name="datasetName">The dataset name, e.g. <c>Book1_A</c> for sheet 1 or <c>Book1_A@2</c> for sheet 2.</param>
        /// <param name="bookName">The part in front of the last underscore, which is the assumed book name.</param>
        /// <param name="columnName">The part between the last underscore and the <c>@</c> suffix, which is the assumed column name.</param>
        /// <param name="sheetNumber">The one-based sheet number; 1 when the name has no <c>@</c> suffix.</param>
        /// <returns><see langword="true"/> if the name follows the observed naming pattern.</returns>
        private static bool TryParseWorksheetDatasetName(string datasetName, [NotNullWhen(true)] out string? bookName,
            [NotNullWhen(true)] out string? columnName, out int sheetNumber)
        {
            bookName = null;
            columnName = null;
            sheetNumber = 1;
            int separator = datasetName.IndexOf('_');
            if (separator <= 0 || separator != datasetName.LastIndexOf('_') || separator == datasetName.Length - 1 ||
                datasetName.AsSpan(0, separator).Contains('@'))
                return false;

            int suffix = datasetName.IndexOf('@', separator + 1);
            int columnEnd = suffix < 0 ? datasetName.Length : suffix;
            if (columnEnd == separator + 1 ||
                (suffix >= 0 && (!int.TryParse(datasetName.AsSpan(suffix + 1), NumberStyles.None, CultureInfo.InvariantCulture, out sheetNumber) || sheetNumber < 1)))
                return false;

            var candidateBook = datasetName[..separator];
            var candidateColumn = datasetName[(separator + 1)..columnEnd];
            if (string.IsNullOrWhiteSpace(candidateBook) || string.IsNullOrWhiteSpace(candidateColumn))
                return false;
            bookName = candidateBook;
            columnName = candidateColumn;
            return true;
        }

        /// <summary>
        /// Read-only list of the sheets resolved for a book, addressed by position but stored by one-based sheet number.
        /// </summary>
        /// <param name="count">The highest sheet number seen for the book, which fixes the number of positions.</param>
        /// <param name="sheets">The resolved sheets keyed by their one-based sheet number.</param>
        /// <remarks>A position without a resolved sheet yields an empty placeholder sheet, keeping the positions of the higher numbered sheets intact.</remarks>
        private sealed class NumberedSheets(int count, IReadOnlyDictionary<int, Sheet> sheets) : IReadOnlyList<Sheet>
        {
            /// <summary>Gets the number of sheet positions of the book.</summary>
            public int Count => count;

            /// <summary>Gets the sheet at the given zero-based position, or an empty sheet if that position was not resolved.</summary>
            /// <param name="index">The zero-based position within the book.</param>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the sheet positions of the book.</exception>
            public Sheet this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)count)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    return sheets.TryGetValue(index + 1, out var sheet) ? sheet : new Sheet(index + 1, null, []);
                }
            }

            /// <summary>Enumerates the sheet positions of the book in ascending order.</summary>
            /// <returns>The sheets of the book, one per position.</returns>
            public IEnumerator<Sheet> GetEnumerator()
            {
                for (int i = 0; i < count; ++i)
                    yield return this[i];
            }

            /// <inheritdoc/>
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        // ----- Worksheet datasets: locating the dataset headers and decoding their value blocks -----

        /// <summary>
        /// Locates the worksheet datasets by their dataset header and decodes their value blocks where the layout is known.
        /// </summary>
        /// <returns>The located datasets in file order.</returns>
        /// <remarks>
        /// A dataset is recognized by the width record announcing its element width, the dataset name record, the short
        /// descriptor and the 24 byte envelope in front of the value block, and by the two byte block prefix behind the
        /// envelope. Everything accepted here is a candidate: the envelope announces a length hypothesis, it does not
        /// establish a native block boundary.
        /// </remarks>
        private IReadOnlyList<AbstractColumn> FindWorksheetDatasetCandidates()
        {
            var columns = new List<AbstractColumn>();
            for (int i = 0; i + 4 < Records.Count; ++i)
            {
                // A dataset starts with the width record that announces the element width of its value block.
                if (Records[i] is not TLVRecord width || width.Tag != 0x85 ||
                    !(width.Payload.Length == 1 && width.Payload.Span[0] is 1 or 2 or 4 or 8 or 10 or 16 or 50 ||
                      width.Payload.Length == 2 && width.Payload.Span[0] is 0x08 or 0x0a && width.Payload.Span[1] is 0x14 or 0x16 ||
                      width.Payload.Length == 3 && width.Payload.Span[0] is 1 or 2 or 4 &&
                      width.Payload.Span[1] == 0 && width.Payload.Span[2] == 8))
                    continue;

                // TextNumeric width candidates can include an extra byte and change the following name tags.
                bool extendedTextNumericWidth = width.Payload.Length == 2 && width.Payload.Span[0] == 0x0a;
                bool textNumericWidth = extendedTextNumericWidth || width.Payload.Span.SequenceEqual(new byte[] { 0x0a });
                int nameIndex = i + 1;
                bool splitNameRecord = false;
                if (Records[nameIndex] is TLVRecord marker && (marker.Tag == 0x91 ||
                    marker.Tag == 0x90 && width.Payload.Length == 2 || marker.Tag == 0x8f && width.Payload.Length == 3) &&
                    marker.Payload.Length == 1 && marker.Payload.Span[0] is >= 1 and <= 9)
                {
                    splitNameRecord = true;
                    ++nameIndex;
                }
                if (nameIndex + 3 >= Records.Count || Records[nameIndex] is not TLVRecord nameRecord ||
                    !(splitNameRecord ? nameRecord.Tag == 0x82 :
                      nameRecord.Tag == 0x97 || width.Payload.Length == 3 && nameRecord.Tag == 0x95 ||
                      extendedTextNumericWidth && nameRecord.Tag == 0x96) ||
                    Records[nameIndex + 1] is not TLVRecord descriptor ||
                    !IsWorksheetDatasetDescriptor(descriptor))
                    continue;
                int envelopeIndex = nameIndex + 2;
                if (Records[envelopeIndex] is TLVRecord extra && extra.Tag == 0x8d &&
                    extra.Payload.Span.SequenceEqual(new byte[] { 1, 2 }) && width.Payload.Length == 3)
                    ++envelopeIndex;
                if (textNumericWidth && Records[envelopeIndex] is TLVRecord textNumericMarker &&
                    textNumericMarker.Tag == 0x82 && textNumericMarker.Payload.Span.SequenceEqual(new byte[] { 0x02 }))
                    ++envelopeIndex;
                if (envelopeIndex + 1 >= Records.Count || Records[envelopeIndex] is not TLVRecord envelope ||
                    (envelope.Tag != 0x9b && envelope.Tag != 0x9d && !(width.Payload.Length == 3 && envelope.Tag == 0x8b) &&
                     !(textNumericWidth && envelope.Tag == 0x95)) || envelope.Payload.Length != 24 ||
                    !envelope.Payload.Span[..8].SequenceEqual(new byte[] { 0x18, 0, 0, 0, 0, 0, 0, 0 }))
                    continue;

                bool contiguous = true;
                for (int j = i; j < envelopeIndex + 1; ++j)
                    contiguous &= Records[j].NextOffset == Records[j + 1].Offset;
                if (!contiguous || nameRecord.Payload.IsEmpty)
                    continue;

                string name;
                try
                {
                    name = new UTF8Encoding(false, true).GetString(nameRecord.Payload.Span);
                }
                catch (DecoderFallbackException)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                bool hasControlCharacter = false;
                foreach (char character in name)
                    hasControlCharacter |= char.IsControl(character);
                if (hasControlCharacter)
                    continue;

                // The value block follows the envelope directly and starts with 0x0a 0x05, or with 0x0a 0x04 for text/numeric columns.
                int dataRecordIndex = envelopeIndex + 1;
                if (!TryCopyRecordBytes(dataRecordIndex, envelope.NextOffset, 2, out var prefix) ||
                    prefix[0] != 0x0a || (prefix[1] != 0x05 && !(textNumericWidth && prefix[1] == 0x04)))
                    continue;

                int dataOffset = envelope.NextOffset + 1;
                int textNumericHeaderIndex = i - 3;
                if (textNumericHeaderIndex >= 0 && Records[textNumericHeaderIndex] is TLVRecord rowCandidate && rowCandidate.Tag == 0x84)
                    --textNumericHeaderIndex;
                bool textNumericHeader = textNumericHeaderIndex >= 0 &&
                    Records[textNumericHeaderIndex] is TLVRecord mixedHeader && mixedHeader.Tag is 0x81 or 0x86 &&
                    mixedHeader.Payload.Length == 4 &&
                    (mixedHeader.Payload.Span[0] == 0x21 && mixedHeader.Payload.Span[1] is 0x51 or 0x60 or 0x61 ||
                     mixedHeader.Payload.Span[0] == 0x20 && mixedHeader.Payload.Span[1] == 0x61) &&
                    mixedHeader.Payload.Span[2] == 0x03;
                // The descriptor remains identifiable when the scanner swallowed the preceding type header.
                bool textNumeric = textNumericWidth && (textNumericHeader ||
                    descriptor.Payload.Span.StartsWith(new byte[] { 0xca, 0x10 }));
                // Only 8 byte elements and text/numeric columns announce the element count as a little-endian 64 bit value in the envelope.
                ulong? candidateLength = width.Payload.Span[0] == 8 || textNumeric
                    ? BinaryPrimitives.ReadUInt64LittleEndian(envelope.Payload.Span.Slice(8, 8))
                    : null;
                ReadOnlyMemory<byte>? candidateData = null;
                if (candidateLength is > 0 and <= int.MaxValue &&
                    TryCopyRecordBytes(dataRecordIndex, dataOffset, (int)candidateLength.Value, out var data))
                    candidateData = data;

                var descriptorRecords = new List<IRecord>();
                var (columnType, typeRecordIndex) = FindWorksheetColumnType(i);
                var dataType = FindNumericDatasetType(i, width);
                for (int j = typeRecordIndex; j <= envelopeIndex; ++j)
                    descriptorRecords.Add(Records[j]);
                int? dataRowCount = null;
                IReadOnlyList<Variant>? values = null;
                // The decoder to use is selected by the element width, the declared data type and the block layout markers.
                if (textNumeric && candidateData is { } mixedData &&
                    TryDecodeTextNumericDataset(mixedData.Span, dataRecordIndex, dataOffset, out var mixedValues))
                {
                    // The encoded capacity includes trailing blanks; expose only the populated extent as values.
                    dataRowCount = mixedValues.Count;
                    int populatedExtent = mixedValues.Count;
                    while (populatedExtent > 0 && mixedValues[populatedExtent - 1].IsEmpty)
                        --populatedExtent;
                    var populatedValues = new Variant[populatedExtent];
                    for (int row = 0; row < populatedExtent; ++row)
                        populatedValues[row] = mixedValues[row];
                    values = Array.AsReadOnly(populatedValues);
                }
                else if (width.Payload.Span[0] == 8 && candidateData is { } encoded && encoded.Length == 4 &&
                    encoded.Span[0] == 0x05 && encoded.Span[1] > 0 &&
                    encoded.Span[2] == encoded.Span[1] * 2 && encoded.Span[3] == 0x65 &&
                    envelope.Payload.Span.Slice(16, 4).SequenceEqual(encoded.Span) &&
                    envelope.Payload.Span.Slice(20, 4).SequenceEqual(encoded.Span))
                {
                    dataRowCount = encoded.Span[1];
                    var emptyValues = new Variant[dataRowCount.Value];
                    Array.Fill(emptyValues, new Variant(double.NaN));
                    values = Array.AsReadOnly(emptyValues);
                }
                else if (dataType == DeserializedDataType.Double && candidateData is { } numericData &&
                    TryDecodeDoubleValueBlock(numericData.Span, out var decoded))
                {
                    dataRowCount = decoded.Count;
                    values = decoded;
                }
                else if (dataType == DeserializedDataType.Single &&
                    envelope.Payload.Span[8..].SequenceEqual(new byte[] { 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22 }) &&
                    TryDecodeSingleDataset(i, dataRecordIndex, dataOffset, out var singleValues, out var singleData))
                {
                    dataRowCount = singleValues.Count;
                    values = singleValues;
                    candidateData = singleData;
                }
                else if (width.Payload.Length == 1 && width.Payload.Span[0] == 50 &&
                    descriptor.Payload.Span.StartsWith(new byte[] { 0xe8, 0x10 }) &&
                    envelope.Payload.Span[8..].SequenceEqual(new byte[] { 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22 }) &&
                    TryDecodeTextDataset(dataRecordIndex, dataOffset, out var textValues, out var textData))
                {
                    dataRowCount = textValues.Count;
                    values = textValues;
                    candidateData = textData;
                }
                columns.Add(new DatasetColumn(name, width.Payload.Span[0], descriptorRecords.AsReadOnly(),
                    Records[dataRecordIndex], dataOffset, candidateLength, candidateData)
                {
                    DataRowCount = dataRowCount,
                    Values = values,
                    SpreadColumnType = columnType,
                    DeserializedDataType = dataType
                });
                i = envelopeIndex;
            }
            return columns.AsReadOnly();
        }

        /// <summary>
        /// Applies the long name, unit and comment of every column, taken from the long name fields in file order.
        /// </summary>
        /// <param name="columns">The located datasets in file order.</param>
        /// <remarks>
        /// The long name field carries the three texts separated by CRLF. The fields are matched to the datasets purely
        /// by their order of occurrence, so the assignment is skipped entirely when the number of fields does not equal
        /// the number of datasets.
        /// </remarks>
        private void ApplyWorksheetColumnLongnames(IReadOnlyList<AbstractColumn> columns)
        {
            var longnames = new List<string>();
            var units = new List<string>();
            var comments = new List<string>();
            // Local function: reads the long name field starting at the given offset and appends its three parts.
            // Returning false means the field is malformed and the whole pass has to be abandoned.
            bool TryAddLongnameAt(int recordIndex, int offset)
            {
                if (!TryCopyRecordBytes(recordIndex, offset, 2, out var signature) || signature[0] != 0x9a)
                    return true;
                int prefixLength;
                if (signature[1] == 0 && offset >= 3)
                {
                    int prefixIndex = recordIndex;
                    while (prefixIndex > 0 && Records[prefixIndex].Offset > offset - 3)
                        --prefixIndex;
                    if (!TryCopyRecordBytes(prefixIndex, offset - 3, 3, out var context) ||
                        !context.AsSpan().SequenceEqual(new byte[] { 0x84, 0x01, 0x08 }))
                        return true;
                    prefixLength = 2;
                }
                else if (signature[1] > 0)
                {
                    // Behind the tag comes the length of the metadata block, which is empty for the default formats and
                    // carries additional entries such as the format string of a user defined value type specification.
                    // The long name itself follows the 0x0a separator behind that block.
                    if (!TryCopyRecordBytes(recordIndex, offset, 3 + signature[1], out var metadata) ||
                        metadata[2 + signature[1]] != 0x0a)
                        return true;
                    prefixLength = 3 + signature[1];
                }
                else
                    return true;
                if (!TryCopyRecordBytes(recordIndex, offset, prefixLength + 2, out var header))
                    return false;
                if (header[prefixLength] == 0)
                {
                    longnames.Add(string.Empty);
                    units.Add(string.Empty);
                    comments.Add(string.Empty);
                    return true;
                }

                int encodedLength = prefixLength + 3 + header[prefixLength];
                if (!TryCopyRecordBytes(recordIndex, offset, encodedLength, out var encoded) ||
                    !TryReadWorksheetColumnLongname(encoded, 0, prefixLength, out var fieldText))
                    return false;
                int unitSeparator = fieldText.IndexOf("\r\n", StringComparison.Ordinal);
                int commentSeparator = unitSeparator < 0 ? -1 : fieldText.IndexOf("\r\n", unitSeparator + 2, StringComparison.Ordinal);
                longnames.Add(unitSeparator < 0 ? fieldText : fieldText[..unitSeparator]);
                units.Add(unitSeparator < 0 ? string.Empty : fieldText[(unitSeparator + 2)..(commentSeparator < 0 ? fieldText.Length : commentSeparator)]);
                comments.Add(commentSeparator < 0 ? string.Empty : fieldText[(commentSeparator + 2)..]);
                return true;
            }

            for (int recordIndex = 0; recordIndex < Records.Count; ++recordIndex)
            {
                // The long name field is searched for by its tag byte, which can also sit inside a record payload.
                switch (Records[recordIndex])
                {
                    case UnknownRegionRecord region:
                        for (int i = 0; i < region.Payload.Length; ++i)
                            if (region.Payload.Span[i] == 0x9a && !TryAddLongnameAt(recordIndex, region.Offset + i))
                                return;
                        break;
                    case TLVRecord tlv:
                        if (tlv.Tag == 0x9a && !TryAddLongnameAt(recordIndex, tlv.Offset))
                            return;
                        for (int i = 0; i < tlv.Payload.Length; ++i)
                            if (tlv.Payload.Span[i] == 0x9a && !TryAddLongnameAt(recordIndex, tlv.Offset + 2 + i))
                                return;
                        break;
                }
            }

            // The observed field is ordered with the worksheet datasets; retain unresolved ownership if the lists differ.
            if (longnames.Count != columns.Count || units.Count != columns.Count || comments.Count != columns.Count)
                return;
            for (int i = 0; i < columns.Count; ++i)
            {
                columns[i].LongName = longnames[i];
                columns[i].Unit = units[i];
                columns[i].Comment = comments[i];
            }
        }

        /// <summary>
        /// Reads a column long name field located at the given offset of a byte buffer.
        /// </summary>
        /// <param name="data">The buffer containing the field.</param>
        /// <param name="offset">The offset of the field, which starts with the tag byte 0x9a.</param>
        /// <param name="prefixLength">Length of the tag together with its metadata block, i.e. the offset of the outer length byte.</param>
        /// <param name="longname">The decoded text of the field.</param>
        /// <returns><see langword="true"/> if a well formed field was read; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The prefix is either the two byte form <c>9A 00</c> or the form <c>9A &lt;L&gt; &lt;L bytes of metadata&gt; 0A</c>, whose metadata
        /// block is a single zero byte for the built in formats and holds additional entries otherwise.
        /// </remarks>
        private static bool TryReadWorksheetColumnLongname(ReadOnlySpan<byte> data, int offset, int prefixLength, out string longname)
        {
            longname = string.Empty;
            if (offset < 0 || prefixLength < 2 || data[offset] != 0x9a)
                return false;
            if (offset > data.Length - prefixLength - 3)
                return false;

            int outerLength = data[offset + prefixLength];
            if (outerLength < 2 || offset > data.Length - prefixLength - 3 - outerLength)
                return false;
            int stringLength = data[offset + prefixLength + 1];
            if (stringLength < 1 || outerLength != stringLength + 1)
                return false;

            int stringOffset = offset + prefixLength + 2;
            int terminatorOffset = stringOffset + stringLength - 1;
            int trailerOffset = stringOffset + stringLength;
            if (data[terminatorOffset] != 0 || data[trailerOffset] != 0x0a || data[trailerOffset + 1] != 0x00)
                return false;
            try
            {
                longname = new UTF8Encoding(false, true).GetString(data.Slice(stringOffset, stringLength - 1));
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applies the plot role, the declared value type and the designation records to the located columns.
        /// </summary>
        /// <param name="columns">The located datasets in file order.</param>
        /// <remarks>
        /// The designation region is separate from the dataset headers, so designations can only be matched to the
        /// datasets by their order of occurrence. The pass is abandoned as a whole unless the number of accepted
        /// designations equals the number of datasets and every designation carries the expected ordinal and column
        /// name; otherwise a single rejected designation would shift all remaining ones onto the wrong columns.
        /// </remarks>
        private void ApplyWorksheetColumnDesignations(IReadOnlyList<AbstractColumn> columns)
        {
            if (columns.Count == 0)
                return;

            // Collect the datasets of one book and sheet as a group, and remember the column names in file order.
            var worksheetGroups = new List<(int Start, int Count)>();
            var names = new List<string>(columns.Count);
            var namesInWorksheet = new HashSet<string>(StringComparer.Ordinal);
            string? currentBook = null;
            int currentSheet = 0;
            int worksheetStart = 0;
            for (int i = 0; i < columns.Count; ++i)
            {
                if (!TryParseWorksheetDatasetName(columns[i].DatasetName, out var book, out var name, out int sheet))
                    return;
                if (currentBook is not null && (book != currentBook || sheet != currentSheet))
                {
                    worksheetGroups.Add((worksheetStart, i - worksheetStart));
                    worksheetStart = i;
                    namesInWorksheet.Clear();
                }
                currentBook = book;
                currentSheet = sheet;
                if (namesInWorksheet.Count >= 255 || !namesInWorksheet.Add(name))
                    return;
                names.Add(name);
            }
            worksheetGroups.Add((worksheetStart, columns.Count - worksheetStart));

            // A designation consists of a name record and the descriptor record directly behind it.
            var candidates = new List<(int Ordinal, string Name, SpreadColumnType Type, ValueType? ValueType, ValueTypeSpecification? ValueTypeSpecification, bool ApplyDesignation, IReadOnlyList<IRecord> Records)>();
            for (int i = 1; i + 1 < Records.Count; ++i)
            {
                if (Records[i] is not TLVRecord nameRecord ||
                    !((nameRecord.Tag == 0x87 && nameRecord.Payload.Length == 2) ||
                      (nameRecord.Tag == 0x88 && (nameRecord.Payload.Length == 1 || nameRecord.Payload.Length == 2)) ||
                      (nameRecord.Tag == 0x82 && nameRecord.Payload.Length == 2) ||
                      (nameRecord.Tag == 0x83 && nameRecord.Payload.Length == 1)) ||
                    Records[i + 1] is not TLVRecord descriptor ||
                    (descriptor.Tag != 0x8d && descriptor.Tag != 0x8f && descriptor.Tag != 0x8a && descriptor.Tag != 0x88 && descriptor.Tag != 0x8b) ||
                    nameRecord.NextOffset != descriptor.Offset)
                    continue;

                if (descriptor.Tag == 0x8b && (descriptor.Payload.Length != 1 ||
                    descriptor.Payload.Span[0] is not (1 or 2 or 3 or 4 or 0x0a or 0x82 or 0x83 or 0x84) ||
                    i + 2 >= Records.Count || Records[i + 2] is not TLVRecord numericDescriptor ||
                    numericDescriptor.Tag != 0x80 || numericDescriptor.Payload.Length != 2 ||
                    descriptor.NextOffset != numericDescriptor.Offset ||
                    numericDescriptor.Payload.Span[0] is not (1 or 0x21) || numericDescriptor.Payload.Span[1] is not (0x50 or 0x60)))
                    continue;

                // The populated YErr TextNumeric header combines the type and following metadata in one scanner candidate.
                bool extendedTextNumeric = descriptor.Tag == 0x88 &&
                    descriptor.Payload.Span.SequenceEqual(new byte[] { 0x09, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x30, 0x61 });
                bool populatedHeader = nameRecord.Tag is 0x82 or 0x83;
                if (populatedHeader && (Records[i - 1] is not TLVRecord rowMarker || rowMarker.Tag != 0x81 ||
                    rowMarker.Payload.Length != 1 || rowMarker.Payload.Span[0] == 0 || rowMarker.NextOffset != nameRecord.Offset ||
                    !(((descriptor.Tag == 0x88 && descriptor.Payload.Length is 1 or 2 &&
                       // The one byte form carries the value type code alone, the two byte form additionally the
                       // format byte of the value type specification.
                       descriptor.Payload.Span[0] is 0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x09) ||
                       extendedTextNumeric ||
                       (descriptor.Tag == 0x8b && descriptor.Payload.Span.SequenceEqual(new byte[] { 0x01 }))) ||
                      (descriptor.Tag == 0x8f && descriptor.Payload.Span.SequenceEqual(new byte[] { 0x01, 0x50 })) ||
                      (descriptor.Tag == 0x8d &&
                       (descriptor.Payload.Span.SequenceEqual(new byte[] { 0x01, 0x00, 0x01, 0x60 }) ||
                        descriptor.Payload.Span.SequenceEqual(new byte[] { 0x04, 0x00, 0x00, 0x60 }))))))
                    continue;
                // The designation header in front of the name record is 13 bytes for a populated and 10 bytes for an empty column.
                int startIndex = i - 1;
                int startOffset = nameRecord.Offset - (populatedHeader ? 13 : 10);
                while (startIndex > 0 && Records[startIndex].Offset > startOffset)
                    --startIndex;
                if (startOffset < 0 || !TryCopyRecordBytes(startIndex, startOffset, 10, out var prefix))
                    continue;
                bool shortHeader = (prefix[0] == 0x00 || prefix[0] == 0x0a) && prefix[1] == 0x80 && prefix[3] == 0x01 &&
                    prefix[4] == 0x10 && prefix[5] == 0x80 && prefix[6] == 0x03;
                bool longHeader = prefix[0] == 0x80 && prefix[2] == 0x07 && prefix[3] == 0x10 &&
                    prefix[4] == 0x01 && prefix[5] == 0 && prefix[6] == 0;
                if ((!shortHeader && !longHeader) || prefix[8] != 0 ||
                    (prefix[9] != 0x09 && prefix[9] != 0x0b && prefix[9] != 0x89 && prefix[9] != 0x8b))
                    continue;

                var payload = nameRecord.Payload.Span;
                // The first byte of the name record is the role code, the last byte is the single character column name.
                byte code = nameRecord.Tag is 0x87 or 0x82 ? payload[0] : (byte)0;
                byte nameByte = payload[^1];
                if (nameByte < 0x21 || nameByte >= 0x7f)
                    continue;
                SpreadColumnType type = code switch
                {
                    0 when nameRecord.Tag is 0x88 or 0x83 => SpreadColumnType.Y,
                    1 => SpreadColumnType.Ignore,
                    2 => SpreadColumnType.YErr,
                    3 => SpreadColumnType.X,
                    4 => SpreadColumnType.Label,
                    5 => SpreadColumnType.Z,
                    6 => SpreadColumnType.XErr,
                    7 => SpreadColumnType.Group,
                    8 => SpreadColumnType.Subject,
                    _ => SpreadColumnType.Ignore
                };
                // The descriptor tag together with its first payload byte gives the declared value type of the column.
                ValueType? valueType = descriptor.Tag switch
                {
                    0x8d or 0x8f or 0x8a or 0x8b => ValueType.Numeric,
                    0x88 when extendedTextNumeric => ValueType.TextNumeric,
                    0x88 when descriptor.Payload.Length is 1 or 2 => descriptor.Payload.Span[0] switch
                    {
                        1 => ValueType.Text,
                        2 => ValueType.Time,
                        3 => ValueType.Date,
                        4 => ValueType.Month,
                        5 => ValueType.Day,
                        9 => ValueType.TextNumeric,
                        _ => null
                    },
                    0x88 when descriptor.Payload.Length == 9 => descriptor.Payload.Span[0] switch
                    {
                        0x0e => ValueType.Binary,
                        0x0f => ValueType.Color,
                        _ => null
                    },
                    _ => null
                };
                // For the value types below the value type code is the high byte of the value type specification
                // and the optional second payload byte is its format byte. A missing format byte means 0, which is
                // the default specification of that value type. Numeric, text/numeric, binary and color descriptors
                // do not carry the specification here, so it stays unresolved for them.
                ValueTypeSpecification? valueTypeSpecification =
                    descriptor.Tag == 0x88 && descriptor.Payload.Length is 1 or 2 &&
                    valueType is ValueType.Text or ValueType.Time or ValueType.Date or ValueType.Month or ValueType.Day
                    ? (ValueTypeSpecification)((descriptor.Payload.Span[0] << 8) |
                        (descriptor.Payload.Length == 2 ? descriptor.Payload.Span[1] : 0))
                    : null;
                candidates.Add((prefix[7], ((char)nameByte).ToString(), type, valueType, valueTypeSpecification,
                    !(populatedHeader && descriptor.Tag is 0x8b or 0x8f),
                    Array.AsReadOnly(new IRecord[] { nameRecord, descriptor })));
            }

            if (candidates.Count != columns.Count)
                return;
            // The designations correspond to the datasets only if their ordinals and names line up exactly.
            for (int i = 0; i < columns.Count; ++i)
                if (candidates[i].Ordinal != i + 1 || candidates[i].Name != names[i])
                    return;
            for (int i = 0; i < columns.Count; ++i)
            {
                // Scanner gaps can hide a numeric dataset header; use validated worksheet metadata in that case.
                if (candidates[i].ApplyDesignation || columns[i].SpreadColumnType == SpreadColumnType.Ignore)
                    columns[i].SpreadColumnType = candidates[i].Type;
                columns[i].ValueType = candidates[i].ValueType;
                if (candidates[i].ValueTypeSpecification is { } specification)
                    columns[i].ValueTypeSpecification = specification;
                columns[i].TypeDescriptorRecords = candidates[i].Records;
            }
        }

        /// <summary>
        /// Determines the data type of a numeric value block from the type header in front of the dataset.
        /// </summary>
        /// <param name="widthIndex">The index of the width record of the dataset in <see cref="Records"/>.</param>
        /// <param name="width">The width record announcing the element width.</param>
        /// <returns>The data type of the value block, or <see langword="null"/> when the header does not match a known layout.</returns>
        /// <remarks>
        /// The type header is delimited by the two records carrying the bytes <c>f0 3f</c> directly in front of the width
        /// record. Its first byte selects the base type, its second byte the numeric sub type, and the third byte is 3.
        /// Whether the column is signed or unsigned is taken from the length of the width record.
        /// </remarks>
        private DeserializedDataType? FindNumericDatasetType(int widthIndex, TLVRecord width)
        {
            int index = widthIndex - 1;
            if (index < 1 || Records[index] is not TLVRecord last || last.Tag != 0x83 ||
                !last.Payload.Span.SequenceEqual(new byte[] { 0xf0, 0x3f }) ||
                Records[index - 1] is not TLVRecord previous || previous.Tag is not (0x86 or 0x8e) ||
                !previous.Payload.Span.SequenceEqual(new byte[] { 0xf0, 0x3f }))
                return null;
            index -= 2;
            if (index >= 0 && Records[index] is TLVRecord row && row.Tag == 0x84 && row.Payload.Length == 1)
                --index;
            if (index < 0 || Records[index] is not TLVRecord header ||
                !((header.Tag is 0x81 or 0x86 && header.Payload.Length == 4 && header.Payload.Span[2] == 3) ||
                  (header.Tag is 0x82 or 0x87 && header.Payload.Length == 3 && header.Payload.Span[1] == 3)))
                return null;
            for (int j = index; j < widthIndex; ++j)
                if (Records[j].NextOffset != Records[j + 1].Offset)
                    return null;

            var data = header.Payload.Span;
            if (width.Payload.Span[0] == 8 &&
                (header.Payload.Length == 3 && data[0] is 0x50 or 0x60 ||
                 header.Payload.Length == 4 && data[0] is 1 or 0x10 && data[1] is >= 0x40 and <= 0x6f))
                return DeserializedDataType.Double;
            if (header.Payload.Length != 4 || data[1] is < 0x50 or > 0x6f)
                return null;

            bool unsigned = width.Payload.Length == 3;
            // The pair of (base type, element width, signedness) selects the data type of the value block.
            return (data[0], width.Payload.Span[0], unsigned) switch
            {
                (1, 8, false) => DeserializedDataType.Double,
                (3, 4, false) when data[1] is 0x50 or 0x60 => DeserializedDataType.Single,
                (1, 16, false) => DeserializedDataType.Complex,
                (1, 4, false) => DeserializedDataType.Int32,
                (1, 4, true) => DeserializedDataType.UInt32,
                (3, 2, false) when data[1] is 0x58 or 0x68 => DeserializedDataType.Int16,
                (3, 2, true) when data[1] is 0x58 or 0x68 => DeserializedDataType.UInt16,
                (0x21, 1, false) => DeserializedDataType.SByte,
                (0x21, 1, true) => DeserializedDataType.Byte,
                _ => null
            };
        }

        /// <summary>
        /// Determines the plot role of a column from the type record in front of its width record.
        /// </summary>
        /// <param name="widthIndex">The index of the width record of the dataset in <see cref="Records"/>.</param>
        /// <returns>The role of the column and the index of the first record that belongs to the dataset header.</returns>
        /// <remarks>
        /// This is the same type header that <see cref="FindNumericDatasetType"/> inspects; here only its second byte is
        /// of interest, which tells an X column (<c>0x50</c>) from a Y column (<c>0x60</c>). When no such header can be
        /// proven, the result is <see cref="SpreadColumnType.Ignore"/> together with <paramref name="widthIndex"/>.
        /// </remarks>
        private (SpreadColumnType Type, int StartIndex) FindWorksheetColumnType(int widthIndex)
        {
            int index = widthIndex - 1;
            if (index < 1 || Records[index] is not TLVRecord last || last.Tag != 0x83 ||
                !last.Payload.Span.SequenceEqual(new byte[] { 0xf0, 0x3f }) ||
                Records[index - 1] is not TLVRecord previous || (previous.Tag != 0x86 && previous.Tag != 0x8e) ||
                !previous.Payload.Span.SequenceEqual(new byte[] { 0xf0, 0x3f }))
                return (SpreadColumnType.Ignore, widthIndex);

            index -= 2;
            if (index >= 0 && Records[index] is TLVRecord rowCandidate && rowCandidate.Tag == 0x84 && rowCandidate.Payload.Length == 1)
                --index;
            if (index < 0 || Records[index] is not TLVRecord typeRecord ||
                (typeRecord.Tag != 0x81 && typeRecord.Tag != 0x86) || typeRecord.Payload.Length != 4 ||
                (typeRecord.Payload.Span[0] != 0x01 && typeRecord.Payload.Span[0] != 0x03) || typeRecord.Payload.Span[2] != 0x03)
                return (SpreadColumnType.Ignore, widthIndex);
            for (int j = index; j < widthIndex; ++j)
                if (Records[j].NextOffset != Records[j + 1].Offset)
                    return (SpreadColumnType.Ignore, widthIndex);
            var columnType = typeRecord.Payload.Span[1] switch
            {
                0x50 => SpreadColumnType.X,
                0x60 => SpreadColumnType.Y,
                _ => SpreadColumnType.Ignore
            };
            return (columnType, index);
        }

        // ----- Value block decoders: turning the encoded bytes of a column into cell values -----

        /// <summary>
        /// Reads a 7 bit per byte little-endian unsigned integer, as used for row counts and run controls.
        /// </summary>
        /// <param name="data">The buffer to read from.</param>
        /// <param name="position">On input the offset of the value, on output the offset directly behind it.</param>
        /// <param name="value">The decoded value.</param>
        /// <returns><see langword="true"/> if a complete value was read; otherwise, <see langword="false"/>.</returns>
        private static bool TryReadUnsignedVariableInteger(ReadOnlySpan<byte> data, ref int position, out uint value)
        {
            value = 0;
            for (int shift = 0; shift <= 28; shift += 7)
            {
                if (position >= data.Length)
                    return false;
                byte current = data[position++];
                if (shift == 28 && (current & 0xf0) != 0)
                    return false;
                value |= (uint)(current & 0x7f) << shift;
                if ((current & 0x80) == 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Decodes the value block of a text column, whose elements are length prefixed UTF-8 strings.
        /// </summary>
        /// <param name="startIndex">The index of the record in which the value block starts.</param>
        /// <param name="dataOffset">The file offset of the value block.</param>
        /// <param name="values">The decoded cell values.</param>
        /// <param name="encodedData">The bytes the value block consists of.</param>
        /// <returns><see langword="true"/> if the block was decoded and terminated as expected.</returns>
        /// <remarks>The row count is a single byte below 0x80; the block ends with <c>02 01 00 0A</c> or with a run of zero bytes.</remarks>
        private bool TryDecodeTextDataset(int startIndex, int dataOffset,
            [NotNullWhen(true)] out IReadOnlyList<Variant>? values, [NotNullWhen(true)] out byte[]? encodedData)
        {
            values = null;
            encodedData = null;
            int position = dataOffset;
            int index = startIndex;
            // Local function: reads the given number of bytes of the block, crossing record boundaries as needed.
            bool ReadBytes(int count, [NotNullWhen(true)] out byte[]? bytes)
            {
                bytes = null;
                if (count < 0 || (long)position + count - dataOffset > 16384)
                    return false;
                while (index < Records.Count && Records[index].NextOffset <= position)
                    ++index;
                if (index >= Records.Count || !TryCopyRecordBytes(index, position, count, out bytes))
                    return false;
                position += count;
                return true;
            }

            if (!ReadBytes(2, out var header) || header[0] != 0x05 || header[1] == 0 || header[1] >= 0x80)
                return false;
            var result = new Variant[header[1]];
            var utf8 = new UTF8Encoding(false, true);
            for (int row = 0; row < result.Length; ++row)
            {
                if (!ReadBytes(1, out var length) || length[0] >= 0x80 || !ReadBytes(length[0], out var text))
                    return false;
                try
                {
                    result[row] = new Variant(utf8.GetString(text));
                }
                catch (DecoderFallbackException)
                {
                    return false;
                }
            }

            int blockLength = position - dataOffset;
            if (!ReadBytes(4, out var trailer))
                return false;
            // The block ends with 02 01 00 0A, or with a longer run of zero bytes closed by 0A 00 in front of the
            // window description of the sheet. That window description is a record of its own, so its bytes are
            // verified against the project bytes instead of being read as a part of the value block.
            if (!trailer.AsSpan().SequenceEqual(new byte[] { 0x02, 0x01, 0x00, 0x0a }))
            {
                ReadOnlySpan<byte> blockEnd = [0, 0, 0, 0, 0, 0x0a, 0];
                if (!trailer.AsSpan().SequenceEqual(new byte[] { 0, 0, 0, 0 }) ||
                    position + blockEnd.Length + 1 > _fileData.Length ||
                    !_fileData.AsSpan(position, blockEnd.Length).SequenceEqual(blockEnd) ||
                    _fileData[position + blockEnd.Length] is < 0x80 or > 0x9f)
                    return false;
            }
            if (!TryCopyRecordBytes(startIndex, dataOffset, blockLength, out encodedData))
                return false;
            values = Array.AsReadOnly(result);
            return true;
        }

        /// <summary>
        /// Decodes the value block of a 4 byte (single precision) numeric column.
        /// </summary>
        /// <param name="widthIndex">The index of the width record of the dataset, used to locate the row count marker.</param>
        /// <param name="startIndex">The index of the record in which the value block starts.</param>
        /// <param name="dataOffset">The file offset of the value block.</param>
        /// <param name="values">The decoded cell values; rows behind the populated extent become <see cref="double.NaN"/>.</param>
        /// <param name="encodedData">The bytes the value block consists of.</param>
        /// <returns><see langword="true"/> if the block was decoded and terminated as expected.</returns>
        /// <remarks>
        /// A run is either a single repeated 4 byte value or a literal run of 4 byte values. The encoded capacity of the
        /// block can be larger than the populated extent of the column, which is taken from the row count marker in front
        /// of the dataset; only the populated rows have to be non zero.
        /// </remarks>
        private bool TryDecodeSingleDataset(int widthIndex, int startIndex, int dataOffset,
            [NotNullWhen(true)] out IReadOnlyList<Variant>? values, [NotNullWhen(true)] out byte[]? encodedData)
        {
            values = null;
            encodedData = null;
            int rowMarkerIndex = widthIndex - 3;
            if (rowMarkerIndex < 0)
                return false;
            int populatedExtent = 0;
            if (Records[rowMarkerIndex] is TLVRecord rowMarker && rowMarker.Tag == 0x84 && rowMarker.Payload.Length == 1)
            {
                populatedExtent = rowMarker.Payload.Span[0];
                if (populatedExtent == 0)
                    return false;
            }
            else if (Records[rowMarkerIndex] is not TLVRecord header || header.Tag is not (0x81 or 0x86) || header.Payload.Length != 4)
                return false;

            int position = dataOffset;
            int index = startIndex;
            // Local function: reads the given number of bytes of the block, crossing record boundaries as needed.
            bool ReadBytes(int count, [NotNullWhen(true)] out byte[]? bytes)
            {
                bytes = null;
                if (count < 0 || (long)position + count - dataOffset > 4_000_128)
                    return false;
                while (index < Records.Count && Records[index].NextOffset <= position)
                    ++index;
                if (index >= Records.Count || !TryCopyRecordBytes(index, position, count, out bytes))
                    return false;
                position += count;
                return true;
            }
            // Local function: reads a 7 bit per byte little-endian unsigned integer from the block.
            bool ReadInteger(out uint value)
            {
                value = 0;
                for (int shift = 0; shift <= 28; shift += 7)
                {
                    if (!ReadBytes(1, out var next) || (shift == 28 && (next[0] & 0xf0) != 0))
                        return false;
                    value |= (uint)(next[0] & 0x7f) << shift;
                    if ((next[0] & 0x80) == 0)
                        return true;
                }
                return false;
            }

            if (!ReadBytes(1, out var marker) || marker[0] != 0x05 || !ReadInteger(out uint rowCount) ||
                rowCount == 0 || rowCount > 1_000_000 || populatedExtent > rowCount)
                return false;
            var result = new Variant[(int)rowCount];
            int row = 0;
            while (row < result.Length)
            {
                // An even run control repeats one value, an odd one introduces a literal run of the following values.
                if (!ReadInteger(out uint control))
                    return false;
                uint count = (control & 1) == 0 ? control / 2 : control / 2 + 1;
                if (count == 0 || count > result.Length - row)
                    return false;
                bool literal = (control & 1) != 0;
                if (!ReadBytes(literal ? checked((int)count * 4) : 4, out var raw))
                    return false;
                for (int j = 0; j < count; ++j)
                {
                    uint bits = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(literal ? j * 4 : 0, 4));
                    if (row >= populatedExtent)
                    {
                        if (bits != 0)
                            return false;
                        result[row] = new Variant(double.NaN);
                    }
                    else
                    {
                        float number = BitConverter.Int32BitsToSingle(unchecked((int)bits));
                        if (!float.IsFinite(number))
                            return false;
                        result[row] = new Variant((double)number);
                    }
                    ++row;
                }
            }
            int blockLength = position - dataOffset;
            if (!ReadBytes(4, out var trailer) || !trailer.AsSpan().SequenceEqual(new byte[] { 0x02, 0x01, 0x00, 0x0a }) ||
                !TryCopyRecordBytes(startIndex, dataOffset, blockLength, out encodedData))
                return false;
            values = Array.AsReadOnly(result);
            return true;
        }

        /// <summary>
        /// Decodes the value block of a text/numeric column, which mixes numbers with references to a shared string pool.
        /// </summary>
        /// <param name="data">The bytes of the value block.</param>
        /// <param name="startIndex">The index of the record in which the value block starts; the string pool follows the block.</param>
        /// <param name="dataOffset">The file offset of the value block.</param>
        /// <param name="values">The decoded cell values; blank rows are left as the default <see cref="Variant"/>.</param>
        /// <returns><see langword="true"/> if the whole block was consumed without leftovers.</returns>
        /// <remarks>
        /// A run is introduced by <c>ff ff</c>, a run control and a kind byte. Numbers use the same encoding as numeric
        /// columns, strings are referenced by their index in the pool, and blanks are announced by their doubled row count.
        /// A block that ends in the middle of the encoded capacity is closed by <c>ce fa</c>.
        /// </remarks>
        private bool TryDecodeTextNumericDataset(ReadOnlySpan<byte> data, int startIndex, int dataOffset,
            [NotNullWhen(true)] out IReadOnlyList<Variant>? values)
        {
            values = null;
            int position = 0;
            if (data.IsEmpty || data[position] is not (0x04 or 0x05))
                return false;
            bool hasStrings = data[position++] == 0x04;
            if (!TryReadUnsignedVariableInteger(data, ref position, out uint rowCount) || rowCount == 0 || rowCount > 1_000_000)
                return false;
            var result = new Variant[(int)rowCount];
            var strings = new Dictionary<int, string>();
            if (hasStrings)
            {
                // The string pool follows the value block. Its header is named _USER_STRINGS_PSET and announces the pool length.
                int poolOffset = dataOffset + data.Length;
                while (startIndex < Records.Count && Records[startIndex].NextOffset <= poolOffset)
                    ++startIndex;
                if (startIndex >= Records.Count || !TryCopyRecordBytes(startIndex, poolOffset, 55, out var poolHeader) ||
                    !poolHeader.AsSpan(1, 22).SequenceEqual(new byte[] { 0x01, 0x23, 0x5f, 0x4d, 0x53, 0x45, 0x52, 0x5f, 0x53, 0x54, 0x52, 0x49, 0x4e, 0x47, 0x53, 0x5f, 0x50, 0x53, 0x45, 0x54, 0x05, 0x01 }) ||
                    poolHeader[0] < 59 || poolHeader[0] >= 0x80 || poolHeader[23] != poolHeader[0] - 25 || poolHeader[24] != 0)
                    return false;
                int stringOffset;
                int stringCount;
                if (poolHeader.AsSpan(29, 10).SequenceEqual(new byte[] { 0xf0, 0x13, 0x77, 0x11, 0x11, 0x11, 0x02, 0, 0x01, 0 }) &&
                    BinaryPrimitives.ReadUInt32LittleEndian(poolHeader.AsSpan(25, 4)) == poolHeader[0] - 31 &&
                    poolHeader.AsSpan(39, 16).SequenceEqual(new byte[] { 4, 0, 0, 0, 5, 0, 0, 0, 6, 0, 0, 0, 3, 0, 0, 0 }))
                {
                    // The uncompressed three-string pool stores byte offsets relative to its first string.
                    stringOffset = 55;
                    stringCount = 3;
                }
                else if (poolHeader.AsSpan(29, 10).SequenceEqual(new byte[] { 0xf0, 0x16, 0x77, 0x11, 0x11, 0x11, 0x02, 0, 0x01, 0 }) &&
                    BinaryPrimitives.ReadUInt32LittleEndian(poolHeader.AsSpan(25, 4)) == poolHeader[0] - 31 &&
                    poolHeader.AsSpan(39, 16).SequenceEqual(new byte[] { 4, 0, 0, 0, 2, 0, 0, 0, 9, 0, 0, 0, 3, 0, 0, 0 }))
                {
                    stringOffset = 55;
                    stringCount = 3;
                }
                else if (poolHeader.AsSpan(29, 10).SequenceEqual(new byte[] { 0xf1, 0x04, 0x77, 0x11, 0x11, 0x11, 0x02, 0, 0x01, 0 }) &&
                    BinaryPrimitives.ReadUInt32LittleEndian(poolHeader.AsSpan(25, 4)) == poolHeader[0] - 30 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(poolHeader.AsSpan(39, 4)) == 4 &&
                    poolHeader.AsSpan(47, 7).SequenceEqual(new byte[] { 0x1d, 0, 0, 0x0c, 0, 0xf0, 0x12 }))
                {
                    stringOffset = 54;
                    stringCount = 4;
                }
                else
                    return false;
                if (!TryCopyRecordBytes(startIndex, poolOffset, poolHeader[0], out var pool) ||
                    !pool.AsSpan(pool.Length - 4).SequenceEqual(new byte[4]))
                    return false;
                var utf8 = new UTF8Encoding(false, true);
                // The pool stores its strings consecutively, each terminated by a NUL byte, and is addressed by a one-based index.
                int offset = stringOffset;
                int stringEnd = pool.Length - 4;
                while (offset < stringEnd)
                {
                    int terminator = Array.IndexOf(pool, (byte)0, offset, stringEnd - offset);
                    if (terminator < 0 || terminator == offset)
                        return false;
                    try
                    {
                        strings.Add(offset - stringOffset + 1, utf8.GetString(pool, offset, terminator - offset));
                    }
                    catch (DecoderFallbackException)
                    {
                        return false;
                    }
                    offset = terminator + 1;
                }
                if (strings.Count != stringCount)
                    return false;
            }
            int row = 0;
            while (row < result.Length)
            {
                // A run of rows starts with ff ff, a single number is introduced by its compact tag, and 01 00 00 is a blank row.
                if (data.Length - position < 3)
                    return false;
                if (data[position] == 0xff && data[position + 1] == 0xff)
                {
                    position += 2;
                    if (!TryReadUnsignedVariableInteger(data, ref position, out uint count) || count == 0 || count > result.Length - row || position >= data.Length)
                        return false;
                    byte type = data[position++];
                    if (type == 0)
                    {
                        if (!TryDecodeDoubleRows(data, ref position, (int)count, out var numericRows, true))
                            return false;
                        numericRows.CopyTo(result, row);
                    }
                    else if (type == 1 && hasStrings && position < data.Length && data[position] == 2 * strings.Count - 1)
                    {
                        ++position;
                        if (count < strings.Count || data.Length - position < strings.Count)
                            return false;
                        for (int j = 0; j < strings.Count; ++j)
                        {
                            if (!strings.TryGetValue(data[position++], out var text))
                                return false;
                            result[row + j] = new Variant(text);
                        }
                        int blankRows = (int)count - strings.Count;
                        if (blankRows > 0 && (!TryReadUnsignedVariableInteger(data, ref position, out uint blankCount) ||
                            blankCount != 2 * blankRows || position >= data.Length || data[position++] != 0))
                            return false;
                        if (row + count == result.Length)
                        {
                            if (!data[position..].SequenceEqual(new byte[] { 0xce, 0xfa }))
                                return false;
                            position += 2;
                        }
                    }
                    else if (type == 1 && TryReadUnsignedVariableInteger(data, ref position, out uint blankCount) &&
                        blankCount == 2 * count && position < data.Length && data[position++] == 0)
                    {
                        Array.Fill(result, default, row, (int)count);
                        if (row + count == result.Length)
                        {
                            if (!data[position..].SequenceEqual(new byte[] { 0xce, 0xfa }))
                                return false;
                            position += 2;
                        }
                    }
                    else
                        return false;
                    row += (int)count;
                }
                else if (data[position] == 0 && data[position + 1] == 0)
                {
                    position += 2;
                    if (!TryReadCompactDouble(data, ref position, out var number))
                        return false;
                    result[row++] = new Variant(number);
                }
                else if (data[position] == 1 && data[position + 1] == 0 && data[position + 2] == 0)
                {
                    result[row++] = default;
                    position += 3;
                }
                else
                    return false;
            }
            if (position != data.Length)
                return false;
            values = Array.AsReadOnly(result);
            return true;
        }

        /// <summary>
        /// Decodes a complete value block of an 8 byte numeric column, including its row count header.
        /// </summary>
        /// <param name="data">The bytes of the value block.</param>
        /// <param name="values">The decoded cell values.</param>
        /// <returns><see langword="true"/> if the block was decoded and consumed; otherwise, <see langword="false"/>.</returns>
        /// <remarks>The block starts with the block kind byte <c>0x05</c> and a row count; a trailing <c>0x02</c> byte is accepted.</remarks>
        private static bool TryDecodeDoubleValueBlock(ReadOnlySpan<byte> data, [NotNullWhen(true)] out IReadOnlyList<Variant>? values)
        {
            values = null;
            int position = 0;
            if (data.IsEmpty || data[position++] != 0x05 ||
                !TryReadUnsignedVariableInteger(data, ref position, out uint rowCount) || rowCount == 0 || rowCount > 1_000_000)
                return false;
            if (!TryDecodeDoubleRows(data, ref position, (int)rowCount, out var result) ||
                (position != data.Length && !(position + 1 == data.Length && data[position] == 0x02)))
                return false;
            values = Array.AsReadOnly(result);
            return true;
        }

        /// <summary>
        /// Reads a double value that is stored with a reduced number of high order bytes.
        /// </summary>
        /// <param name="data">The buffer to read from.</param>
        /// <param name="position">On input the offset of the tag byte, on output the offset directly behind the value.</param>
        /// <param name="value">The decoded value.</param>
        /// <returns><see langword="true"/> if a well formed value was read; otherwise, <see langword="false"/>.</returns>
        private static bool TryReadCompactDouble(ReadOnlySpan<byte> data, ref int position, out double value)
        {
            value = default;
            if (position >= data.Length)
                return false;
            int encodedLength = data[position] - 0x08;
            if (encodedLength < 9 || encodedLength > 72 || encodedLength % 9 != 0)
                return false;
            // The compact tag stores the number of high-order bytes in increments of nine.
            int length = encodedLength / 9;
            if (data.Length - position - 1 < length)
                return false;
            ++position;
            ulong bits = 0;
            for (int i = 0; i < length; ++i)
                bits |= (ulong)data[position++] << (8 * (8 - length + i));
            value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
            return true;
        }

        /// <summary>
        /// Decodes the rows of a numeric value block, which are stored as repeated values or as delta encoded runs.
        /// </summary>
        /// <param name="data">The buffer to read from.</param>
        /// <param name="position">On input the offset of the first run, on output the offset directly behind the last decoded row.</param>
        /// <param name="rowCount">The number of rows to decode.</param>
        /// <param name="values">The decoded rows.</param>
        /// <param name="unresolvedBlanks"><see langword="true"/> to turn the blank markers into empty values instead of storing them as numbers.</param>
        /// <returns><see langword="true"/> if the requested number of rows could be decoded; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// An even run control repeats one value, which may be the compact form or the marker <c>0x65</c> for "the rest is
        /// empty". An odd run control introduces a literal run marked by <c>0x0c</c>. In a literal run every value is
        /// stored as the xor of a prediction and a small difference; the prediction is either the last value seen for the
        /// current hash (fcm table) or the previous value plus the last difference seen for the current delta hash (dfcm table).
        /// </remarks>
        private static bool TryDecodeDoubleRows(ReadOnlySpan<byte> data, ref int position, int rowCount, [NotNullWhen(true)] out Variant[]? values, bool unresolvedBlanks = false)
        {
            values = null;
            var result = new Variant[rowCount];
            int row = 0;
            while (row < result.Length)
            {
                if (!TryReadUnsignedVariableInteger(data, ref position, out uint control))
                    return false;
                uint count = (control & 1) == 0 ? control / 2 : control / 2 + 1;
                if (count == 0 || count > result.Length - row || position >= data.Length)
                    return false;
                if ((control & 1) == 0)
                {
                    double repeated;
                    if (data[position] == 0x65)
                    {
                        repeated = double.NaN;
                        ++position;
                    }
                    else if (!TryReadCompactDouble(data, ref position, out repeated))
                        return false;
                    for (int j = 0; j < count; ++j)
                        result[row++] = unresolvedBlanks && double.IsNaN(repeated) ? default : new Variant(repeated);
                    continue;
                }
                if (data[position++] != 0x0c)
                    return false;
                // Literal run: both prediction tables are 4096 entries wide and are indexed by a hash of the previous value or difference.
                ulong previous = 0;
                var fcm = new ulong[4096];
                var dfcm = new ulong[4096];
                int fcmHash = 0;
                int dfcmHash = 0;
                byte pair = 0;
                for (int j = 0; j < count; ++j)
                {
                    if ((j & 1) == 0)
                    {
                        if (position >= data.Length)
                            return false;
                        pair = data[position++];
                    }
                    // Two values share one byte: the low nibble first. The low three bits of the code give the number of
                    // difference bytes, the fourth bit selects the prediction table.
                    int code = (j & 1) == 0 ? pair & 0x0f : pair >> 4;
                    int size = code & 7;
                    if (size >= 4)
                        ++size;
                    if (data.Length - position < size)
                        return false;
                    ulong difference = 0;
                    for (int k = 0; k < size; ++k)
                        difference |= (ulong)data[position++] << (8 * k);
                    ulong predicted = (code & 8) == 0 ? fcm[fcmHash] : unchecked(previous + dfcm[dfcmHash]);
                    ulong bits = predicted ^ difference;
                    fcm[fcmHash] = bits;
                    fcmHash = (int)(((uint)fcmHash << 6 ^ bits >> 48) & 0xfff);
                    ulong delta = unchecked(bits - previous);
                    dfcm[dfcmHash] = delta;
                    dfcmHash = (int)(((uint)dfcmHash << 2 ^ delta >> 40) & 0xfff);
                    previous = bits;
                    double value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
                    // -1.23456789E-300 is the sentinel Origin writes for a blank cell.
                    result[row++] = unresolvedBlanks && value == -1.23456789E-300 ? default : new Variant(value);
                }
                if ((count & 1) != 0 && (pair & 0xf0) != 0)
                    return false;
            }
            values = result;
            return true;
        }

        // ----- Record level helpers: reading bytes across the candidate records -----

        /// <summary>
        /// Copies a range of file bytes out of the parsed records, reassembling the record headers the scanner dropped.
        /// </summary>
        /// <param name="startIndex">The index of the first record that may contribute bytes.</param>
        /// <param name="offset">The file offset of the first byte to copy.</param>
        /// <param name="count">The number of bytes to copy.</param>
        /// <param name="bytes">The copied bytes.</param>
        /// <returns><see langword="true"/> if the whole range is covered by consecutive copyable records.</returns>
        /// <remarks>
        /// The tag and length byte of a <see cref="TLVRecord"/> are part of the copied data, so a TLV record contributes
        /// two more bytes than its payload. Image table and PNG records are never copied, because their content does not
        /// belong to the object stream.
        /// </remarks>
        private bool TryCopyRecordBytes(int startIndex, int offset, int count, [NotNullWhen(true)] out byte[]? bytes)
        {
            bytes = null;
            if (count < 0 || startIndex >= Records.Count || offset < Records[startIndex].Offset)
                return false;

            long end = (long)offset + count;
            int index = startIndex;
            int position = offset;
            while (position < end)
            {
                if (index >= Records.Count || Records[index].Offset > position || Records[index].NextOffset <= position ||
                    Records[index] is ImageTable or PngRecord ||
                    (Records[index] is not TLVRecord && Records[index] is not UnknownRegionRecord) ||
                    (Records[index] is TLVRecord preview && preview.Payload.Span.StartsWith("PrvwOPJU"u8)))
                    return false;
                position = (int)Math.Min(end, Records[index].NextOffset);
                ++index;
            }

            bytes = new byte[count];
            position = offset;
            index = startIndex;
            while (position < end)
            {
                var record = Records[index++];
                int localOffset = position - record.Offset;
                int length = (int)Math.Min(end - position, record.NextOffset - position);
                for (int j = 0; j < length; ++j)
                {
                    int source = localOffset + j;
                    bytes[position - offset + j] = record switch
                    {
                        TLVRecord tlv when source == 0 => tlv.Tag,
                        TLVRecord tlv when source == 1 => (byte)tlv.Payload.Length,
                        TLVRecord tlv => tlv.Payload.Span[source - 2],
                        UnknownRegionRecord region => region.Payload.Span[source],
                        _ => throw new InvalidDataException("The candidate data crosses an unsupported record.")
                    };
                }
                position += length;
            }
            return true;
        }


        /// <summary>Reads the project header and partitions the body into candidate records, indexed PNG images, and unknown regions.</summary>
        /// <param name="originFile">The stream containing the project bytes.</param>
        /// <returns>The body records in file order.</returns>
        /// <remarks>Record recognition and resynchronization are heuristic; unknown regions are scanner gaps, not necessarily native format blocks.</remarks>
        /// <exception cref="InvalidDataException">The header line or root header is invalid or incomplete.</exception>
        [MemberNotNull(nameof(FormatVersion))]
        [MemberNotNull(nameof(ObjectCount))]
        [MemberNotNull(nameof(BodyOffset))]
        [MemberNotNull(nameof(RootHeader))]
        private IReadOnlyList<IRecord> ParseIntoRecords(Stream originFile)
        {
            ReadOnlySpan<byte> prvwOPJUmarker = [0x50, 0x72, 0x76, 0x77, 0x4f, 0x50, 0x4a, 0x55]; // the text prvwOPJU is followed 

            // Read records data into memory.
            var data = new byte[originFile.Length];
            originFile.ReadExactly(data, 0, data.Length);
            _fileData = data;

            // Find the first newline that limits the header and parse the header line.
            int newline = Array.IndexOf(data, (byte)'\n');
            if (newline < 0 || newline > 4096)
                throw new InvalidDataException("The OPJU header line is missing or too long.");
            var parts = Encoding.ASCII.GetString(data, 0, newline).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0] != "CPYUA" ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var objectCount))
                throw new InvalidDataException("Invalid OPJU header line.");
            FormatVersion = parts[1];
            ObjectCount = objectCount;

            // The body starts immediately after the newline.
            BodyOffset = newline + 1;
            if (data.Length - BodyOffset < 7)
                throw new InvalidDataException("The OPJU root header is truncated.");
            RootHeader = new ReadOnlyMemory<byte>(data, (int)BodyOffset, 7);
            var start = BodyOffset + 7;
            var position = start;

            // Scan for candidate records with a tag in 0x80..0x9f, a length byte, and a payload.
            while (position < data.Length)
            {
                // Find the first record starting after the current position. Records can be
                // inserted ahead of the scanner when an image index is encountered.
                int low = 0;
                int high = _records.Count;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (_records[middle].Offset <= position)
                        low = middle + 1;
                    else
                        high = middle;
                }

                // Skip a record containing this position; otherwise stop scanning at the
                // next record so that TLVs and unknown regions cannot overlap it.
                if (low > 0 && position < _records[low - 1].NextOffset)
                {
                    position = _records[low - 1].NextOffset;
                    continue;
                }
                var scanEnd = low < _records.Count ? _records[low].Offset : data.Length;
                if (TryReadTLVRecord(data, position, MaxTLVLength, out var record) && record.NextOffset <= scanEnd) // to be proven: is 0x60 really the maximum length of a plain record? The engineering spec says 0x7f, but that would be a problem for resynchronization. Longer records are handled as window descriptions.
                {
                    // test if the new record is the preview image table record
                    if (record.Payload.Length == 0x0A &&
                      prvwOPJUmarker.SequenceEqual(record.Payload.Span.Slice(0, prvwOPJUmarker.Length)))
                    {
                        // the length of the record is 0x0A, but it is 6 bytes more, so make a new TLVRecord with the correct length
                        AddRecord(new TLVRecord(record.Offset, record.Tag, new ReadOnlyMemory<byte>(data, record.Offset + 2, 0x10)));

                        // get the index into the preview image table. A project without any preview image (e.g. a project
                        // that only holds note windows) stores a null pointer here, and there is neither an image table
                        // nor any PNG data in the file.
                        var previewImageTableOffset = (int)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan().Slice(position + 2 + prvwOPJUmarker.Length, 8));
                        if (previewImageTableOffset != 0)
                            FindIndexedPngs(data, previewImageTableOffset, BodyOffset);
                        position = 6 + record.NextOffset; // length was off by 2 bytes, so we need to add 6 instead of 8
                        if (TryReadLengthPrefixedBlock(data, position, 0x60, out var rec))
                        {
                            AddRecord(rec);
                            position = rec.NextOffset;
                        }
                    }
                    else if (record.Tag == 0x8f &&
                        TryFindEmbeddedWorksheetDatasetStart(data, position, record.NextOffset, out int embeddedDatasetOffset))
                    {
                        // A candidate data record can swallow the following worksheet width; split only on a validated dataset header.
                        AddRecord(new UnknownRegionRecord(position, embeddedDatasetOffset - position,
                            new ReadOnlyMemory<byte>(data, position, embeddedDatasetOffset - position)));
                        position = embeddedDatasetOffset;
                    }
                    else
                    {
                        AddRecord(record);
                        position += 2 + record.Payload.Length;
                    }
                    continue;
                }

                // A window description declares a length above the limit of the plain candidate scan, so it has to
                // be recognized separately.
                if (TryReadWindowRecord(data, position, scanEnd, out var windowRecord))
                {
                    AddRecord(windowRecord);
                    position = windowRecord.NextOffset;
                    continue;
                }

                // Preserve unrecognized bytes until four consecutive candidates permit resynchronization,
                // the start of a window description, or the next indexed image boundary is reached.
                int gapStart = position++;
                bool windowStartsHere = false;
                while (position < scanEnd)
                {
                    if (HasFourConsecutiveTLVRecords(data, position, scanEnd))
                        break;
                    if (TryReadWindowRecord(data, position, scanEnd, out windowRecord))
                    {
                        windowStartsHere = true;
                        break;
                    }
                    ++position;
                }
                AddRecord(new UnknownRegionRecord(gapStart, position - gapStart, new ReadOnlyMemory<byte>(data, gapStart, position - gapStart)));
                if (windowStartsHere)
                {
                    AddRecord(windowRecord);
                    position = windowRecord.NextOffset;
                }
            }
            return _records.AsReadOnly();
        }

        /// <summary>
        /// Searches a candidate record for the start of a worksheet dataset that the scanner swallowed.
        /// </summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="candidateOffset">The offset of the candidate record that may contain the dataset start.</param>
        /// <param name="candidateEnd">The exclusive end of that candidate record.</param>
        /// <param name="datasetOffset">The offset of the width record of the dataset, or -1 when none was found.</param>
        /// <returns><see langword="true"/> if a validated dataset header starts inside the candidate record.</returns>
        /// <remarks>Splitting a record is only allowed on a complete dataset header, so that the remaining bytes stay an unknown region.</remarks>
        private static bool TryFindEmbeddedWorksheetDatasetStart(byte[] data, int candidateOffset, int candidateEnd, out int datasetOffset)
        {
            datasetOffset = -1;
            for (int offset = candidateOffset + 2; offset <= candidateEnd - 3; ++offset)
            {
                if (!TryReadTLVRecord(data, offset, MaxTLVLength, out var width) || width.Tag != 0x85 ||
                    width.Payload.Length != 1 || width.Payload.Span[0] is not (1 or 2 or 4 or 8 or 10 or 16 or 50) ||
                    width.NextOffset != candidateEnd || !HasWorksheetDatasetHeader(data, width.NextOffset))
                    continue;

                datasetOffset = offset;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks whether the bytes at the given offset form the beginning of a worksheet dataset.
        /// </summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="offset">The offset to check, which is expected to hold the width record of a dataset.</param>
        /// <returns><see langword="true"/> if width, name marker, dataset name, descriptor and envelope all match.</returns>
        /// <remarks>
        /// This is the scanner side of the dataset recognition performed by <see cref="FindWorksheetDatasetCandidates"/>.
        /// It reads the records directly from the file bytes, because the scanner has not produced records for this area yet.
        /// </remarks>
        private static bool HasWorksheetDatasetHeader(byte[] data, int offset)
        {
            if (!TryReadTLVRecord(data, offset, 0x60, out var nameMarker) || nameMarker.Tag != 0x91 ||
                nameMarker.Payload.Length != 1 || nameMarker.Payload.Span[0] is < 1 or > 9 ||
                !TryReadTLVRecord(data, nameMarker.NextOffset, 0x60, out var nameRecord) || nameRecord.Tag != 0x82 ||
                nameRecord.Payload.IsEmpty)
                return false;

            string datasetName;
            try
            {
                datasetName = new UTF8Encoding(false, true).GetString(nameRecord.Payload.Span);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            if (!TryParseWorksheetDatasetName(datasetName, out _, out _, out _))
                return false;

            if (!TryReadTLVRecord(data, nameRecord.NextOffset, 0x60, out var descriptor) ||
                !IsWorksheetDatasetDescriptor(descriptor) ||
                !TryReadTLVRecord(data, descriptor.NextOffset, 0x60, out var envelope) ||
                envelope.Tag is not (0x9b or 0x9d) || envelope.Payload.Length != 24 ||
                !envelope.Payload.Span[..8].SequenceEqual(new byte[] { 0x18, 0, 0, 0, 0, 0, 0, 0 }) ||
                envelope.NextOffset > data.Length - 2 || data[envelope.NextOffset] != 0x0a ||
                data[envelope.NextOffset + 1] is not (0x04 or 0x05))
                return false;

            return true;
        }

        /// <summary>
        /// Tells whether a record is the short descriptor that sits between a dataset name and the
        /// envelope of its value block.
        /// </summary>
        /// <remarks>
        /// A tag only names a value class in this format, not a field role, and the tag used for this
        /// descriptor varies: 0x87, 0x8d, 0x8e, 0x8f, 0x90 and 0x93 were observed, sometimes even for
        /// the columns of one and the same workbook. The four byte form therefore is recognized by its
        /// shape (two little-endian 16 bit values whose high bytes are 0x10 and 0x03 or 0x04) instead
        /// of by its tag. The two byte form is restricted to the tag and prefixes seen so far.
        /// </remarks>
        /// <param name="descriptor">The candidate descriptor record.</param>
        /// <returns><see langword="true"/> if the record has the shape of a worksheet dataset descriptor.</returns>
        private static bool IsWorksheetDatasetDescriptor(TLVRecord descriptor)
        {
            var payload = descriptor.Payload.Span;
            return (descriptor.Tag == 0x8f &&
                    (payload.StartsWith(new byte[] { 0xc8, 0x10 }) ||
                     payload.StartsWith(new byte[] { 0xca, 0x10 }) ||
                     payload.StartsWith(new byte[] { 0xe8, 0x10 }))) ||
                   (descriptor.Tag == 0x8d && payload.StartsWith(new byte[] { 0xca, 0x10 })) ||
                   (descriptor.Tag is 0x8e or 0x90 && payload.Length == 4 &&
                    (payload.StartsWith(new byte[] { 0xc8, 0x10 }) ||
                     payload.StartsWith(new byte[] { 0xca, 0x10 }))) ||
                   (descriptor.Tag is 0x87 or 0x93 && payload.Length == 4 &&
                    (payload[0] is 0xc8 or 0xca or 0xe8) && payload[1] == 0x10 &&
                    payload[3] is 0x03 or 0x04);
        }

        /// <summary>Inserts a record in ascending offset order.</summary>
        /// <param name="record">The record to insert.</param>
        /// <exception cref="ArgumentException">A record with the same offset already exists.</exception>
        void AddRecord(IRecord record)
        {
            // Appending is the usual case while scanning forward.
            if (_records.Count == 0 || _records[^1].Offset < record.Offset)
            {
                _records.Add(record);
                return;
            }

            int low = 0;
            int high = _records.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                int offset = _records[middle].Offset;
                if (offset == record.Offset)
                    throw new ArgumentException($"A record at offset {record.Offset} already exists.", nameof(record));
                if (offset < record.Offset)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            _records.Insert(low, record);
        }


        /// <summary>
        /// Reads a block introduced by a little-endian 32 bit length and delimited by 0x0a bytes.
        /// </summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="offset">The offset of the length prefix.</param>
        /// <param name="maximumLength">The maximum accepted block length; not evaluated by this check.</param>
        /// <param name="record">The block that was read.</param>
        /// <returns><see langword="true"/> if the length prefix and the delimiters are valid.</returns>
        /// <remarks>Such a block is not interpreted as objects; it is kept as an <see cref="UnknownRegionRecord"/>.</remarks>
        bool TryReadLengthPrefixedBlock(byte[] data, int offset, int maximumLength, [NotNullWhen(true)] out IRecord record)
        {
            var (isPrefix, length) = IsBlockLengthPrefix(data, offset, maximumLength);
            if (!isPrefix)
            {
                record = null;
                return false;
            }

            if (length == 0)
                record = new UnknownRegionRecord(offset, 5, new ReadOnlyMemory<byte>(data, offset, 5));
            else
                record = new UnknownRegionRecord(offset, 4 + length + 2, new ReadOnlyMemory<byte>(data, offset, 4 + length + 2));
            return true;
        }

        /// <summary>
        /// Checks whether the four bytes at the given offset are the length prefix of a block delimited by 0x0a bytes.
        /// </summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="offset">The offset of the candidate length prefix.</param>
        /// <param name="maximumLength">The maximum accepted block length; not evaluated by this check.</param>
        /// <returns>A pair telling whether the prefix is valid and, if so, which block length it announces.</returns>
        (bool isPrefix, int length) IsBlockLengthPrefix(byte[] data, int offset, int maximumLength)
        {
            if (offset >= data.Length - 5)
                return (false, 0);
            if (data[offset + 4] != 0x0a)
                return (false, 0);
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));

            if (length > 0)
            {

                if (offset + 4 + length + 1 >= data.Length || data[offset + 4 + length + 1] != 0x0a)
                    return (false, 0);
            }
            return (true, length);
        }


        /// <summary>Locates the embedded preview images using the observed image index and adds them as records.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="imageTableOffset">The offset where the image table starts.</param>
        /// <param name="bodyStart">The offset of the start of the body, which bounds the image offsets.</param>
        /// <remarks>
        /// The index layout is inferred from observed files. Every entry has to frame a complete PNG image and the entries
        /// must not overlap. The scanner treats the located images as boundaries that TLV scanning cannot cross.
        /// </remarks>
        /// <exception cref="InvalidDataException">The index is truncated, an entry does not frame a PNG image, or entries overlap.</exception>
        private void FindIndexedPngs(byte[] data, int imageTableOffset, int bodyStart)
        {
            var images = new List<(int Offset, int Length)>();
            var bytes = data.AsSpan();

            // The index has a count, reserved bytes, and a byte length followed by
            // count pairs of 64-bit little-endian absolute offsets and lengths.
            ulong count = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(imageTableOffset, 8));
            uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(imageTableOffset + 8, 4));
            uint tableLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(imageTableOffset + 12, 4));
            if (reserved != 0 || count == 0 || count > (ulong)(data.Length - imageTableOffset - 16) / 16 ||
                count * 16 != tableLength)
                throw new InvalidDataException("The OPJU image table is invalid or truncated.");

            // add the image table as a new record
            AddRecord(new ImageTable(imageTableOffset, (int)(16 + tableLength), new ReadOnlyMemory<byte>(data, imageTableOffset, (int)(16 + tableLength))));


            // Reject the entire index if any range is invalid or does not frame a PNG.
            ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
            ReadOnlySpan<byte> pngEnd = [0, 0, 0, 0, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82];
            for (int i = 0; i < (int)count; ++i)
            {
                int entry = imageTableOffset + 16 + 16 * i;
                ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(entry, 8));
                ulong length = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(entry + 8, 8));
                if (offset < (ulong)bodyStart || offset > (ulong)data.Length || length < 20 ||
                    length > (ulong)data.Length - offset ||
                    (offset < (ulong)(imageTableOffset + 16 + tableLength) && offset + length > (ulong)imageTableOffset) ||
                    !bytes.Slice((int)offset, signature.Length).SequenceEqual(signature) ||
                    !bytes.Slice((int)(offset + length) - pngEnd.Length, pngEnd.Length).SequenceEqual(pngEnd))
                {
                    throw new InvalidDataException($"The OPJU image table entry {i} is invalid or does not frame a PNG image.");
                }
                else
                {
                    images.Add(((int)offset, (int)length));
                }
            }

            // Index entries need not be in file order; overlapping ranges are ambiguous.
            images.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            for (int i = 1; i < images.Count; ++i)
                if (images[i - 1].Offset + images[i - 1].Length > images[i].Offset)
                    throw new InvalidDataException("The OPJU image table contains overlapping entries.");

            // now add the images also as new records
            foreach (var image in images)
            {
                AddRecord(new PngRecord(image.Offset, new ReadOnlyMemory<byte>(data, image.Offset, image.Length)));
            }
        }

        /// <summary>
        /// Reads a worksheet sheet name from the records around the given index.
        /// </summary>
        /// <param name="index">The index of the record carrying the column count of the sheet.</param>
        /// <param name="candidate">The recovered sheet name together with its file offset.</param>
        /// <param name="number">The one-based sheet number taken from the ordinal marker in front of the name.</param>
        /// <param name="columns">The number of columns of the sheet.</param>
        /// <returns><see langword="true"/> if the records form a sheet name entry; otherwise, <see langword="false"/>.</returns>
        /// <remarks>The signature does not establish a sheet's type or its parent book.</remarks>
        private bool TryReadWorksheetSheetName(int index, [NotNullWhen(true)] out SheetNameCandidate? candidate,
            out int number, out int columns)
        {
            candidate = null;
            number = columns = 0;
            if (index < 2 || index + 1 >= Records.Count ||
                Records[index - 1] is not TLVRecord numeric || numeric.Tag != 0x88 || numeric.Payload.Length != 8 ||
                Records[index - 2] is not TLVRecord ordinalMarker || ordinalMarker.Payload.Length != 1)
                return false;

            // A sheet with columns announces its column count in a 0x89 record followed by a 0x80 record holding the
            // marker 0xf7. A sheet without columns merges both records into a single 0x8d record holding that marker.
            int nameIndex;
            int nameOffset;
            if (Records[index] is TLVRecord columnCount && columnCount.Tag == 0x89 && columnCount.Payload.Length == 1 &&
                Records[index + 1] is TLVRecord marker && marker.Tag == 0x80 &&
                marker.Payload.Span.SequenceEqual(new byte[] { 0xf7 }))
            {
                columns = columnCount.Payload.Span[0];
                nameIndex = index + 2;
                nameOffset = marker.NextOffset;
            }
            else if (Records[index] is TLVRecord emptySheet && emptySheet.Tag == 0x8d &&
                emptySheet.Payload.Span.SequenceEqual(new byte[] { 0xf7 }))
            {
                nameIndex = index + 1;
                nameOffset = emptySheet.NextOffset;
            }
            else
                return false;
            if (ordinalMarker.Tag == 0x84 && ordinalMarker.Payload.Span[0] == 7)
                number = 1;
            else if (ordinalMarker.Tag == 0x80 && ordinalMarker.Payload.Span[0] == 7 && index >= 3 &&
                Records[index - 3] is TLVRecord ordinal && ordinal.Tag == 0x80 && ordinal.Payload.Length == 1 &&
                ordinal.Payload.Span[0] > 0 && ordinal.NextOffset == ordinalMarker.Offset)
                number = ordinal.Payload.Span[0] + 1;
            else
                return false;
            for (int i = index - 2; i <= index + 1; ++i)
                if (Records[i].NextOffset != Records[i + 1].Offset)
                    return false;

            // The name block behind the marker starts with c0 ff followed by a 0x8e record and a record holding the
            // name. Two layouts are observed: the 0x8e record either carries two additional bytes and introduces a
            // 0xa4 record (c0 ff 8e 04 ff 01 00 04 a4 <len> <name> 0a 00 10 01), or it is short and introduces a
            // 0xa6 record (c0 ff 8e 02 ff 01 a6 <len> <name> 0a 00 10 01). Therefore only the framing is matched and
            // both records are parsed.
            int offset = nameOffset;
            if (!TryCopyRecordBytes(nameIndex, offset, 4, out var header) ||
                !header.AsSpan(0, 3).SequenceEqual(new byte[] { 0xc0, 0xff, 0x8e }) ||
                header[3] < 2 || header[3] > 8)
                return false;

            // Index of the tag byte of the name record, relative to the start of the name block.
            int nameTag = 4 + header[3];
            if (!TryCopyRecordBytes(nameIndex, offset, nameTag + 2, out var framing) ||
                !framing.AsSpan(4, 2).SequenceEqual(new byte[] { 0xff, 0x01 }) ||
                (framing[nameTag] != 0xa4 && framing[nameTag] != 0xa6) ||
                framing[nameTag + 1] == 0)
                return false;

            int nameStart = nameTag + 2;
            int nameLength = framing[nameTag + 1];
            if (!TryCopyRecordBytes(nameIndex, offset, nameStart + nameLength + 4, out var encoded) ||
                !encoded.AsSpan(nameStart + nameLength, 4).SequenceEqual(new byte[] { 0x0a, 0, 0x10, 1 }))
                return false;
            string name;
            try
            {
                name = new UTF8Encoding(false, true).GetString(encoded, nameStart, nameLength);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(name))
                return false;
            foreach (char character in name)
                if (char.IsControl(character))
                    return false;
            candidate = new SheetNameCandidate(offset + nameStart, name);
            return true;
        }

        /// <summary>Finds possible sheet names in adjacent parsed records matching an observed signature.</summary>
        /// <returns>Printable sheet-name candidates with their file offsets.</returns>
        /// <remarks>Two different sheet signatures are scanned for: the worksheet signature handled by
        /// <see cref="TryReadWorksheetSheetName"/> and the matrix sheet signature that follows a <c>0x94</c> marker.</remarks>
        private IReadOnlyList<SheetNameCandidate> FindSheetNameCandidates()
        {
            var candidates = new List<SheetNameCandidate>();
            for (int i = 0; i + 1 < Records.Count; ++i)
            {
                if (TryReadWorksheetSheetName(i, out var worksheetName, out _, out _))
                    candidates.Add(worksheetName);
                if (Records[i] is not TLVRecord marker || marker.Tag != 0x94 ||
                    !marker.Payload.Span.SequenceEqual(new byte[] { 0xff, 0xff, 0x01, 0x00 }) ||
                    Records[i + 1] is not UnknownRegionRecord region || marker.NextOffset != region.Offset)
                    continue;
                // The unknown region must begin with a nonempty length-prefixed name and the observed suffix.
                var payload = region.Payload.Span;
                if (payload.Length < 5)
                    continue;
                int length = payload[0];
                if (length == 0 || length > payload.Length - 5)
                    continue;
                int suffix = 1 + length;
                if (payload[suffix] != 0x0a || payload[suffix + 1] != 0 || (payload[suffix + 2] != 0x10 && payload[suffix + 2] != 0x40) || payload[suffix + 3] != 1)
                    continue;
                var name = payload.Slice(1, length);
                bool printable = true;
                foreach (var b in name)
                    printable &= b >= 0x20 && b < 0x7f;
                if (printable)
                    candidates.Add(new SheetNameCandidate(region.Offset + 1, Encoding.ASCII.GetString(name)));
            }
            return candidates.AsReadOnly();
        }

        // ----- Matrix and window candidates: locating matrix sheets and book windows -----

        /// <summary>Gets possible matrix dataset names followed by the observed matrix type marker.</summary>
        /// <remarks>These are heuristic matches, not decoded matrix objects.</remarks>
        public IReadOnlyList<TLVRecord> MatrixDatasetCandidates { get; private set; }

        /// <summary>Finds candidate matrix dataset records and infers available dimensions from parsed records.</summary>
        /// <returns>The candidate matrix dataset name records.</returns>
        /// <remarks>Matching dataset, window, and sheet signatures is experimental and does not establish a complete matrix object.</remarks>
        IReadOnlyList<TLVRecord> FindMatrixDatasetCandidates()
        {
            // Match printable dataset names followed by an observed matrix type marker.
            var matrixCandidates = new List<TLVRecord>();
            var datasetIndices = new List<int>();
            for (int idxRecord = 1; idxRecord < Records.Count; ++idxRecord)
            {
                if (Records[idxRecord - 1] is TLVRecord candidate && Records[idxRecord] is TLVRecord type &&
                   (candidate.Tag == 0x95 || candidate.Tag == 0x97) && candidate.Text is not null &&
                   type.Payload.Length >= 2 && type.Payload.Span[0] == 0xca && (type.Payload.Span[1] == 0x50 || type.Payload.Span[1] == 0x51))
                {
                    matrixCandidates.Add(candidate);
                    datasetIndices.Add(idxRecord - 1);
                }
            }

            // Locate named window candidates so sheet records can be bounded by the next window.
            var windows = FindWindowCandidates();

            // Associate a dataset with a uniquely named window, then look for one sheet column count.
            var dimensions = new List<MatrixCandidate>();
            for (int i = 0; i < matrixCandidates.Count; ++i)
            {
                var name = matrixCandidates[i].Text!;
                int? count = FindMatrixElementCount(datasetIndices[i]);
                int? columns = null;
                int windowIndex = -1;
                bool ambiguousWindow = false;
                for (int j = 0; j < windows.Count; ++j)
                {
                    if (windows[j].Name == name)
                    {
                        if (windowIndex >= 0)
                            ambiguousWindow = true;
                        windowIndex = j;
                    }
                }
                if (!ambiguousWindow && windowIndex >= 0)
                {
                    int end = windowIndex + 1 < windows.Count ? windows[windowIndex + 1].Index : Records.Count;
                    for (int j = windows[windowIndex].Index + 1; j + 2 < end; ++j)
                    {
                        if (Records[j] is TLVRecord column && column.Tag == 0x89 && column.Payload.Length == 1 &&
                            Records[j + 1] is TLVRecord sheetMarker && sheetMarker.Tag == 0x94 &&
                            Records[j + 2] is UnknownRegionRecord sheetRegion)
                        {
                            bool knownSheet = false;
                            foreach (var sheet in SheetNameCandidates)
                                knownSheet |= sheet.Offset == sheetRegion.Offset + 1;
                            if (!knownSheet)
                                continue;
                            if (columns is not null)
                            {
                                columns = null;
                                break;
                            }
                            columns = column.Payload.Span[0];
                        }
                    }
                }
                // Infer rows only for a positive column count that divides the candidate element count.
                if (columns == 0)
                    columns = null;
                int? rows = count is not null && columns is not null && count.Value % columns.Value == 0 ? count.Value / columns.Value : null;
                dimensions.Add(new MatrixCandidate(name, rows, columns));
            }
            _matrixCandidates = dimensions.AsReadOnly();
            return matrixCandidates.AsReadOnly();
        }

        /// <summary>Looks backwards for the observed matrix element-count descriptor preceding a dataset name.</summary>
        /// <param name="datasetIndex">The index of the dataset name in <see cref="Records"/>.</param>
        /// <returns>The positive candidate element count, or <see langword="null"/> if it cannot be decoded.</returns>
        private int? FindMatrixElementCount(int datasetIndex)
        {
            for (int i = datasetIndex - 1; i >= 2 && Records[i] is TLVRecord; --i)
            {
                if (Records[i] is TLVRecord countRecord && countRecord.Tag == 0x86 &&
                    Records[i - 1] is TLVRecord second && second.Tag == 0x80 && second.Payload.Span.SequenceEqual(new byte[] { 0xa0, 0x0f, 0x01 }) &&
                    Records[i - 2] is TLVRecord first && first.Tag == 0x80 && first.Payload.Span.SequenceEqual(new byte[] { 0xfa }))
                {
                    var payload = countRecord.Payload.Span;
                    if (payload.Length < 4 || payload.Length > 7 || payload[2] != 0x03)
                        return null;
                    // Decode the variable-length little-endian count after the descriptor's first three bytes.
                    uint count = 0;
                    for (int j = 3; j < payload.Length; ++j)
                        count |= (uint)payload[j] << (8 * (j - 3));
                    return count > 0 && count <= int.MaxValue ? (int)count : null;
                }
            }
            return null;
        }

        /// <summary>
        /// Collects the window candidates of the project, which introduce the byte area belonging to one book window.
        /// </summary>
        /// <returns>The index of the record carrying the window name and the window name itself, in file order.</returns>
        /// <remarks>
        /// A window candidate is recognized by the geometry record and the five byte marker that follow its name. The
        /// candidates bound the searches for sheet names, column widths and book metadata; their order is also used to
        /// number the files inside the project tree.
        /// </remarks>
        private List<(int Index, string Name)> FindWindowCandidates()
        {
            var windows = new List<(int Index, string Name)>();
            for (int i = 0; i < Records.Count; ++i)
            {
                // A window description names its window by itself; its child records belong to that record.
                if (Records[i] is WindowRecord window)
                {
                    windows.Add((i, window.Name));
                    continue;
                }

                if (i + 2 >= Records.Count)
                    continue;

                if (Records[i + 1] is TLVRecord geometry &&
                    Records[i + 2] is TLVRecord marker && marker.Payload.Length == 5 &&
                    (geometry.Payload.Length == 12 && marker.Tag == 0x84 ||
                     geometry.Tag == 0x91 && geometry.Payload.Length == 15 && marker.Tag == 0x81) &&
                    TryGetWindowName(Records[i], out var name) &&
                    Records[i].NextOffset == geometry.Offset && geometry.NextOffset == marker.Offset)
                {
                    windows.Add((i, name));
                    continue;
                }

                if (i + 1 < Records.Count && TryGetWindowCandidateWithInlineGeometry(Records[i], Records[i + 1], out name))
                    windows.Add((i, name));
            }
            return windows;
        }

        /// <summary>
        /// Reads a window candidate whose geometry and marker the scanner folded into the record carrying its name.
        /// </summary>
        /// <param name="nameRecord">The candidate record that contains the window name.</param>
        /// <param name="followingRecord">The record behind it, which may hold the marker of the window.</param>
        /// <param name="name">The recovered ASCII window name.</param>
        /// <returns><see langword="true"/> if exactly one window name with a valid geometry and marker was found.</returns>
        private static bool TryGetWindowCandidateWithInlineGeometry(IRecord nameRecord, IRecord followingRecord,
            [NotNullWhen(true)] out string? name)
        {
            name = null;
            ReadOnlySpan<byte> payload = nameRecord switch
            {
                TLVRecord tlv => tlv.Payload.Span,
                UnknownRegionRecord region => region.Payload.Span,
                _ => ReadOnlySpan<byte>.Empty
            };
            string? candidateName = null;
            for (int i = Math.Max(0, payload.Length - 258); i + 7 < payload.Length; ++i)
            {
                if (payload[i] != 0x80 || payload[i + 2] < 3 || payload[i + 3] != 0 || payload[i + 4] != 0)
                    continue;

                int nameLength = payload[i + 2] - 2;
                int geometryOffset = i + 5 + nameLength;
                if (geometryOffset > payload.Length - 2)
                    continue;
                byte geometryTag = payload[geometryOffset];
                int geometryLength = payload[geometryOffset + 1];
                int markerOffset = geometryOffset + 2 + geometryLength;
                bool markerIsValid = false;
                if (markerOffset == payload.Length && followingRecord is TLVRecord marker &&
                    nameRecord.NextOffset == marker.Offset && marker.Payload.Length == 5)
                    markerIsValid = geometryLength == 12 && marker.Tag == 0x84 ||
                        geometryTag == 0x91 && geometryLength == 15 && marker.Tag == 0x81;
                else if (markerOffset <= payload.Length - 2)
                {
                    byte markerTag = payload[markerOffset];
                    int markerLength = payload[markerOffset + 1];
                    markerIsValid = markerOffset + 2 + markerLength <= payload.Length && markerLength == 5 &&
                        (geometryLength == 12 && markerTag == 0x84 ||
                         geometryTag == 0x91 && geometryLength == 15 && markerTag == 0x81);
                }
                if (!markerIsValid)
                    continue;

                var nameBytes = payload.Slice(i + 5, nameLength);
                bool printable = true;
                foreach (byte value in nameBytes)
                    printable &= value >= 0x20 && value < 0x7f;
                if (!printable)
                    continue;

                if (candidateName is not null)
                    return false;
                candidateName = Encoding.ASCII.GetString(nameBytes);
            }

            if (candidateName is null)
                return false;
            name = candidateName;
            return true;
        }

        /// <summary>Tries to find a printable window name at the end of a parsed record payload.</summary>
        /// <param name="record">The candidate TLV or unknown-region record.</param>
        /// <param name="name">The recovered ASCII name when the observed framing matches.</param>
        /// <returns><see langword="true"/> if a candidate name was found; otherwise, <see langword="false"/>.</returns>
        private static bool TryGetWindowName(IRecord record, [NotNullWhen(true)] out string? name)
        {
            name = null;
            ReadOnlySpan<byte> payload = record switch
            {
                TLVRecord tlv => tlv.Payload.Span,
                UnknownRegionRecord region => region.Payload.Span,
                _ => ReadOnlySpan<byte>.Empty
            };
            // Search only the possible length-prefixed suffix of this record, not the surrounding file bytes.
            for (int i = Math.Max(0, payload.Length - 258); i + 5 < payload.Length; ++i)
            {
                int length = payload.Length - i - 5;
                if (payload[i] != 0x80 || length > 253 || payload[i + 2] != length + 2 || payload[i + 3] != 0 || payload[i + 4] != 0)
                    continue;
                var bytes = payload[(i + 5)..];
                bool printable = true;
                foreach (var b in bytes)
                    printable &= b >= 0x20 && b < 0x7f;
                if (printable)
                {
                    name = Encoding.ASCII.GetString(bytes);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Try to interpret the data at the given offset as a TLV record. Returns false if the data does not look like a valid record.
        /// </summary>
        /// <param name="data">The data to interpret.</param>
        /// <param name="offset">The offset at which to start interpreting.</param>
        /// <param name="maximumLength">The maximum length of the record.</param>
        /// <param name="record">The interpreted record, if successful.</param>
        /// <returns>True if the data was successfully interpreted as a TLV record, false otherwise.</returns>
        private static bool TryReadTLVRecord(byte[] data, int offset, int maximumLength, [NotNullWhen(true)] out TLVRecord? record)
        {
            record = null;
            if (offset >= data.Length - 1 || data[offset] < 0x80 || data[offset] > 0x9f)
                return false;
            int length = data[offset + 1];
            if (length > maximumLength || length > data.Length - offset - 2)
                return false;
            record = new TLVRecord(offset, data[offset], new ReadOnlyMemory<byte>(data, (int)(offset + 2), length));
            return true;
        }

        /// <summary>
        /// Tries to read a window description record at the given offset.
        /// </summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="offset">The offset of the tag to interpret.</param>
        /// <param name="endOffset">The exclusive end of the area available for scanning.</param>
        /// <param name="record">The window description, if the observed framing matches.</param>
        /// <returns><see langword="true"/> if a window description was recognized.</returns>
        /// <remarks>
        /// Only lengths that the plain candidate scan rejects (above its limit) can be window descriptions, so this
        /// recognition cannot change any record that is already parsed today. The payload must consist of a name
        /// field <c>&lt;length&gt; 00 00 &lt;name&gt;</c>, a run of at least four child records, and a short trailing block.
        /// </remarks>
        private static bool TryReadWindowRecord(byte[] data, int offset, int endOffset, [NotNullWhen(true)] out WindowRecord? record)
        {
            record = null;
            if (offset >= data.Length - 1 || data[offset] < 0x80 || data[offset] > 0x9f)
                return false;
            int length = data[offset + 1];
            if (length <= MaxTLVLength || length > 0x7f || offset + 2 + length > Math.Min(endOffset, data.Length))
                return false;

            // The name field is <field length> 00 00 <name>, where the field length counts its own two zero bytes.
            var payload = new ReadOnlyMemory<byte>(data, offset + 2, length);
            var span = payload.Span;
            int nameLength = span[0] - 2;
            if (span[0] < 3 || span[1] != 0 || span[2] != 0 || nameLength > length - 3)
                return false;
            var nameBytes = span.Slice(3, nameLength);
            foreach (var b in nameBytes)
                if (b < 0x20 || b >= 0x7f)
                    return false;

            // The child records follow the name field. Their run ends at the first byte that is not a candidate tag,
            // which is the beginning of the trailing block.
            int payloadEnd = offset + 2 + length;
            var children = new List<TLVRecord>();
            int position = 3 + nameLength;
            while (TryReadTLVRecord(data, offset + 2 + position, MaxTLVLength, out var child) && child.NextOffset <= payloadEnd)
            {
                children.Add(child);
                position = child.NextOffset - (offset + 2);
            }
            if (children.Count < 4 || length - position is < 1 or > 32)
                return false;

            record = new WindowRecord(offset, data[offset], payload, Encoding.ASCII.GetString(nameBytes), children.AsReadOnly());
            return true;
        }

        /// <summary>
        /// Checks if there is a run of at least four consecutive TLV records starting at the given offset. This is used to resynchronize after an unknown region.
        /// </summary>
        /// <param name="data">The data to check.</param>
        /// <param name="offset">The offset at which to start checking.</param>
        /// <param name="endOffset">The exclusive end of the area available for scanning.</param>
        /// <returns>True if there is a valid run of TLV records, false otherwise.</returns>
        private static bool HasFourConsecutiveTLVRecords(byte[] data, int offset, int endOffset)
        {
            for (int i = 0; i < 4; ++i)
            {
                if (!TryReadTLVRecord(data, offset, MaxTLVLength, out var record) || record.NextOffset > endOffset)
                    return false;
                offset = record.NextOffset;
            }
            return true;
        }
    }
}
