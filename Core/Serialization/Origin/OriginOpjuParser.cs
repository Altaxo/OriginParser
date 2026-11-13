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
    /// </remarks>
    public class OriginOpjuParser
    {
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
        /// Represents the preview image table record, which contains the offsets and lengths of the embedded PNG images in the OPJU file.
        /// </summary>
        [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay,nq}")]
        public record ImageTable(int Offset, int Length, ReadOnlyMemory<byte> Payload) : UnknownRegionRecord(Offset, Length, Payload)
        {
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

        public record Book(string? Name, string? Label, IReadOnlyList<Sheet> Sheets)
        {
            public string FolderName { get; set; } = string.Empty;
            public string LongName { get; set; } = string.Empty;
        }

        public record Sheet(int Number, int? RowCount, IReadOnlyList<AbstractColumn> Columns)
        {
            public string Name { get; set; } = string.Empty;
        }

        public abstract class AbstractColumn
        {
            protected AbstractColumn(string datasetName, IReadOnlyList<IRecord> descriptorRecords)
            {
                DatasetName = datasetName;
                DescriptorRecords = descriptorRecords;
            }

            public string DatasetName { get; }
            public string? Name { get; set; }
            public IReadOnlyList<IRecord> DescriptorRecords { get; }
            // Null means the values have not been decoded, not that the column is empty.
            public IReadOnlyList<Variant>? Values { get; init; }

            public SpreadColumnType SpreadColumnType { get; set; } = SpreadColumnType.Ignore;

            public ValueType? ValueType { get; set; }

            public DeserializedDataType? DeserializedDataType { get; set; }
            public string Longname { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public string Comment { get; set; } = string.Empty;
            public double Width { get; set; }
            internal IReadOnlyList<IRecord>? TypeDescriptorRecords { get; set; }

        }

        public sealed class DatasetColumn : AbstractColumn
        {
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

            internal byte ElementWidth { get; }
            internal IRecord DataStartRecord { get; }
            internal int DataOffset { get; }
            internal ulong? CandidateDataLength { get; }
            // The length and bytes are hypotheses; neither establishes a native block boundary.
            internal ReadOnlyMemory<byte>? CandidateData { get; }
            internal int? DataRowCount { get; init; }

        }


        private sealed record MatrixCandidate(string Name, int? NumberOfRows, int? NumberOfColumns);

        private IReadOnlyList<MatrixCandidate> _matrixCandidates = [];

        /// <summary>Gets the format version from the file header.</summary>
        public string FormatVersion { get; private set; }

        /// <summary>Gets the object count reported by the file header.</summary>
        public int ObjectCount { get; private set; }

        /// <summary>Gets the offset immediately following the header line.</summary>
        public int BodyOffset { get; private set; }

        /// <summary>Gets the seven-byte root header.</summary>
        public ReadOnlyMemory<byte> RootHeader { get; private set; }

        private List<IRecord> _records = new List<IRecord>();
        private byte[] _fileData = [];

        /// <summary>Gets candidate records recovered by scanning and resynchronizing the body.</summary>
        public IReadOnlyList<IRecord> Records { get; }



        /// <summary>Gets possible worksheet or matrix sheet names from an observed sheet signature.</summary>
        /// <remarks>The sheet type and parent book cannot be established from these matches alone.</remarks>
        public IReadOnlyList<SheetNameCandidate> SheetNameCandidates { get; }

        public IReadOnlyList<Book> Books { get; }

        internal IReadOnlyList<AbstractColumn> UnassociatedColumns { get; }

        /// <summary>Reads an OPJU project from the beginning of a stream without closing it.</summary>
        /// <param name="originFile">A readable, seekable stream containing the project.</param>
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
            (Books, UnassociatedColumns) = GroupWorksheetDatasets(worksheetColumns);
            ApplyWorksheetSheetNames();
            ApplyWorksheetColumnWidths();
            ApplyWorksheetBookMetadata();
        }

        private void ApplyWorksheetBookMetadata()
        {
            var windows = FindWindowCandidates();
            ReadOnlySpan<byte> longNameMarker = [0x80, 0x01, 0xfc, 0xc0, 0xff, 0x02, 0x00, 0x39, 0x8c, 0x01, 0x48, 0x8b, 0x04, 0xff, 0x7f, 0x00, 0x00];
            for (int w = 0; w < windows.Count; ++w)
            {
                Book? book = null;
                int bookCount = 0;
                int windowCount = 0;
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

                int start = Records[windows[w].Index].NextOffset;
                int end = w + 1 < windows.Count ? Records[windows[w + 1].Index].Offset : _fileData.Length;
                for (int i = windows[w].Index + 1; i < Records.Count && Records[i].Offset < end; ++i)
                    if (Records[i] is PngRecord)
                    {
                        end = Records[i].Offset;
                        break;
                    }
                string? longName = null;
                bool ambiguous = false;
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
                        var name = new UTF8Encoding(false, true).GetString(nameBytes);
                        ambiguous |= longName is not null;
                        longName = name;
                    }
                    catch (DecoderFallbackException)
                    {
                        // Invalid candidates do not establish metadata.
                    }
                }
                if (!ambiguous && longName is not null)
                    book.LongName = longName;
            }

            ApplyWorksheetFolders(windows);
        }

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
            if (fileCount > windowCount || (fileCount > 0 && (position >= end || _fileData[position++] != 0)))
                return false;
            string path = root ? string.Empty : parent.Length == 0 ? name : parent + "\\" + name;
            if (!root && (name.Length == 0 || name.Contains('\\')))
                return false;
            for (int i = 0; i < fileCount; ++i)
            {
                int ordinal;
                if (end - position >= 5 && _fileData.AsSpan(position, 5).SequenceEqual(new byte[] { 0x80, 0x01, 0x85, 0, 0 }))
                {
                    ordinal = 0;
                    position += 5;
                }
                else if (end - position >= 7 && _fileData.AsSpan(position, 4).SequenceEqual(new byte[] { 0x80, 0x04, 0x81, 0x01 }) &&
                    _fileData[position + 5] == 0x80 && _fileData[position + 6] == 0)
                {
                    ordinal = _fileData[position + 4];
                    position += 7;
                }
                else
                    return false;
                if (ordinal >= windowCount || !assignments.TryAdd(ordinal, path))
                    return false;
            }
            if (position >= end || (_fileData[position] & 1) != 0)
                return false;
            int folderCount = _fileData[position++] / 2;
            if (folderCount > end - position)
                return false;
            for (int i = 0; i < folderCount; ++i)
                if (!TryReadFolder(ref position, end, path, false, depth + 1, assignments, windowCount))
                    return false;
            return true;
        }

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

                var widths = new List<double>();
                for (int i = windows[windowIndex].Index + 1; i + 2 < end; ++i)
                {
                    if (Records[i] is not TLVRecord count || count.Tag != 0x89 ||
                        count.Payload.Length != 1 || count.Payload.Span[0] != 1 ||
                        Records[i + 1] is not TLVRecord marker || marker.Tag != 0x82 || marker.Payload.Length != 1 ||
                        Records[i + 2] is not TLVRecord width || width.Tag != 0x8d ||
                        width.Payload.Length is < 1 or > 2 ||
                        count.NextOffset != marker.Offset || marker.NextOffset != width.Offset)
                        continue;

                    // Observed display widths are stored as little-endian tenths of a column unit.
                    ushort encodedWidth = width.Payload.Length == 1
                        ? width.Payload.Span[0]
                        : BinaryPrimitives.ReadUInt16LittleEndian(width.Payload.Span);
                    if (encodedWidth > 0)
                        widths.Add(encodedWidth / 10.0);
                }

                if (widths.Count != columns.Count)
                    continue;
                for (int i = 0; i < columns.Count; ++i)
                    columns[i].Width = widths[i];
            }
        }

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

        private sealed class NumberedSheets(int count, IReadOnlyDictionary<int, Sheet> sheets) : IReadOnlyList<Sheet>
        {
            public int Count => count;

            public Sheet this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)count)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    return sheets.TryGetValue(index + 1, out var sheet) ? sheet : new Sheet(index + 1, null, []);
                }
            }

            public IEnumerator<Sheet> GetEnumerator()
            {
                for (int i = 0; i < count; ++i)
                    yield return this[i];
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private IReadOnlyList<AbstractColumn> FindWorksheetDatasetCandidates()
        {
            var columns = new List<AbstractColumn>();
            for (int i = 0; i + 4 < Records.Count; ++i)
            {
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
                    !((descriptor.Tag == 0x8f &&
                       (descriptor.Payload.Span.StartsWith(new byte[] { 0xc8, 0x10 }) ||
                        descriptor.Payload.Span.StartsWith(new byte[] { 0xca, 0x10 }) ||
                        descriptor.Payload.Span.StartsWith(new byte[] { 0xe8, 0x10 }))) ||
                      (descriptor.Tag == 0x8d && descriptor.Payload.Span.StartsWith(new byte[] { 0xca, 0x10 })) ||
                      (descriptor.Tag is 0x8e or 0x90 && descriptor.Payload.Length == 4 &&
                       (descriptor.Payload.Span.StartsWith(new byte[] { 0xc8, 0x10 }) ||
                        descriptor.Payload.Span.StartsWith(new byte[] { 0xca, 0x10 })))))
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

                int dataRecordIndex = envelopeIndex + 1;
                if (!TryCopyRecordBytes(dataRecordIndex, envelope.NextOffset, 2, out var prefix) ||
                    prefix[0] != 0x0a || (prefix[1] != 0x05 && !(textNumericWidth && prefix[1] == 0x04)))
                    continue;

                int dataOffset = envelope.NextOffset + 1;
                int textNumericHeaderIndex = i - 3;
                if (textNumericHeaderIndex >= 0 && Records[textNumericHeaderIndex] is TLVRecord rowCandidate && rowCandidate.Tag == 0x84)
                    --textNumericHeaderIndex;
                bool textNumeric = textNumericWidth && textNumericHeaderIndex >= 0 &&
                    Records[textNumericHeaderIndex] is TLVRecord mixedHeader && mixedHeader.Tag is 0x81 or 0x86 &&
                    mixedHeader.Payload.Length == 4 &&
                    (mixedHeader.Payload.Span[0] == 0x21 && mixedHeader.Payload.Span[1] is 0x51 or 0x60 or 0x61 ||
                     mixedHeader.Payload.Span[0] == 0x20 && mixedHeader.Payload.Span[1] == 0x61) &&
                    mixedHeader.Payload.Span[2] == 0x03;
                ulong? candidateLength = width.Payload.Length == 1 && width.Payload.Span[0] == 8 || textNumeric
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

        private void ApplyWorksheetColumnLongnames(IReadOnlyList<AbstractColumn> columns)
        {
            var longnames = new List<string>();
            var units = new List<string>();
            var comments = new List<string>();
            bool TryAddLongnameAt(int recordIndex, int offset)
            {
                if (!TryCopyRecordBytes(recordIndex, offset, 4, out var signature))
                    return true;
                int prefixLength;
                if (signature.AsSpan().SequenceEqual(new byte[] { 0x9a, 0x01, 0x00, 0x0a }))
                    prefixLength = 4;
                else if (signature[0] == 0x9a && signature[1] == 0 && offset >= 3)
                {
                    int prefixIndex = recordIndex;
                    while (prefixIndex > 0 && Records[prefixIndex].Offset > offset - 3)
                        --prefixIndex;
                    if (!TryCopyRecordBytes(prefixIndex, offset - 3, 3, out var context) ||
                        !context.AsSpan().SequenceEqual(new byte[] { 0x84, 0x01, 0x08 }))
                        return true;
                    prefixLength = 2;
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
                    !TryReadWorksheetColumnLongname(encoded, 0, out var fieldText, out _))
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
                columns[i].Longname = longnames[i];
                columns[i].Unit = units[i];
                columns[i].Comment = comments[i];
            }
        }

        private static bool TryReadWorksheetColumnLongname(ReadOnlySpan<byte> data, int offset, out string longname, out int encodedLength)
        {
            longname = string.Empty;
            encodedLength = 0;
            if (offset < 0 || offset > data.Length - 4 || data[offset] != 0x9a)
                return false;
            int prefixLength;
            if (data[offset + 1] == 0)
                prefixLength = 2;
            else if (data[offset + 1] == 0x01 && data[offset + 2] == 0x00 && data[offset + 3] == 0x0a)
                prefixLength = 4;
            else
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

            encodedLength = prefixLength + 3 + outerLength;
            return true;
        }

        private void ApplyWorksheetColumnDesignations(IReadOnlyList<AbstractColumn> columns)
        {
            if (columns.Count == 0 || columns.Count > 255)
                return;
            string? book = null;
            var names = new List<string>();
            foreach (var column in columns)
            {
                if (!TryParseWorksheetDatasetName(column.DatasetName, out var currentBook, out var name, out int sheet) ||
                    sheet != 1 || (book is not null && book != currentBook) || names.Contains(name))
                    return;
                book = currentBook;
                names.Add(name);
            }

            var candidates = new List<(int Ordinal, string Name, SpreadColumnType Type, ValueType? ValueType, bool ApplyDesignation, IReadOnlyList<IRecord> Records)>();
            for (int i = 1; i + 1 < Records.Count; ++i)
            {
                if (Records[i] is not TLVRecord nameRecord ||
                    !((nameRecord.Tag == 0x87 && nameRecord.Payload.Length == 2) ||
                      (nameRecord.Tag == 0x88 && nameRecord.Payload.Length == 1) ||
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
                    !(((descriptor.Tag == 0x88 && descriptor.Payload.Length == 1 && descriptor.Payload.Span[0] is 0x01 or 0x09) ||
                       extendedTextNumeric ||
                       (descriptor.Tag == 0x8b && descriptor.Payload.Span.SequenceEqual(new byte[] { 0x01 }))) ||
                      (descriptor.Tag == 0x8f && descriptor.Payload.Span.SequenceEqual(new byte[] { 0x01, 0x50 })) ||
                      (descriptor.Tag == 0x8d && descriptor.Payload.Span.SequenceEqual(new byte[] { 0x01, 0x00, 0x01, 0x60 })))))
                    continue;
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
                ValueType? valueType = descriptor.Tag switch
                {
                    0x8d or 0x8f or 0x8a or 0x8b => ValueType.Numeric,
                    0x88 when extendedTextNumeric => ValueType.TextNumeric,
                    0x88 when descriptor.Payload.Length == 1 => descriptor.Payload.Span[0] switch
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
                candidates.Add((prefix[7], ((char)nameByte).ToString(), type, valueType, !(populatedHeader && descriptor.Tag is 0x8b or 0x8f),
                    Array.AsReadOnly(new IRecord[] { nameRecord, descriptor })));
            }

            if (candidates.Count != columns.Count)
                return;
            for (int i = 0; i < columns.Count; ++i)
                if (candidates[i].Ordinal != i + 1 || candidates[i].Name != names[i])
                    return;
            for (int i = 0; i < columns.Count; ++i)
            {
                // Scanner gaps can hide a numeric dataset header; use validated worksheet metadata in that case.
                if (candidates[i].ApplyDesignation || columns[i].SpreadColumnType == SpreadColumnType.Ignore)
                    columns[i].SpreadColumnType = candidates[i].Type;
                columns[i].ValueType = candidates[i].ValueType;
                columns[i].TypeDescriptorRecords = candidates[i].Records;
            }
        }

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
            if (index < 0 || Records[index] is not TLVRecord header || header.Tag is not (0x81 or 0x86) ||
                header.Payload.Length != 4 || header.Payload.Span[2] != 3)
                return null;
            for (int j = index; j < widthIndex; ++j)
                if (Records[j].NextOffset != Records[j + 1].Offset)
                    return null;

            var data = header.Payload.Span;
            bool unsigned = width.Payload.Length == 3;
            return (data[0], data[1], width.Payload.Span[0], unsigned) switch
            {
                (1, 0x50 or 0x60, 8, false) => DeserializedDataType.Double,
                (3, 0x50 or 0x60, 4, false) => DeserializedDataType.Single,
                (1, 0x52 or 0x62, 16, false) => DeserializedDataType.Complex,
                (1, 0x58 or 0x68, 4, false) => DeserializedDataType.Int32,
                (1, 0x58 or 0x68, 4, true) => DeserializedDataType.UInt32,
                (3, 0x58 or 0x68, 2, false) => DeserializedDataType.Int16,
                (3, 0x58 or 0x68, 2, true) => DeserializedDataType.UInt16,
                (0x21, 0x58 or 0x68, 1, false) => DeserializedDataType.SByte,
                (0x21, 0x58 or 0x68, 1, true) => DeserializedDataType.Byte,
                _ => null
            };
        }

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

        private bool TryDecodeTextDataset(int startIndex, int dataOffset,
            [NotNullWhen(true)] out IReadOnlyList<Variant>? values, [NotNullWhen(true)] out byte[]? encodedData)
        {
            values = null;
            encodedData = null;
            int position = dataOffset;
            int index = startIndex;
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
            if (!trailer.AsSpan().SequenceEqual(new byte[] { 0x02, 0x01, 0x00, 0x0a }))
            {
                if (!trailer.AsSpan().SequenceEqual(new byte[] { 0, 0, 0, 0 }) ||
                    !ReadBytes(9, out var rest) || !rest.AsSpan().SequenceEqual(new byte[] { 0, 0, 0, 0, 0, 0x0a, 0, 0x80, 0x77 }))
                    return false;
            }
            if (!TryCopyRecordBytes(startIndex, dataOffset, blockLength, out encodedData))
                return false;
            values = Array.AsReadOnly(result);
            return true;
        }

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

        private static bool TryReadCompactDouble(ReadOnlySpan<byte> data, ref int position, out double value)
        {
            value = default;
            if (position >= data.Length)
                return false;
            int length = data[position] switch { 0x11 => 1, 0x1a => 2, 0x50 => 8, _ => 0 };
            if (length == 0 || data.Length - position - 1 < length)
                return false;
            ++position;
            ulong bits = 0;
            for (int i = 0; i < length; ++i)
                bits |= (ulong)data[position++] << (8 * (8 - length + i));
            value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
            return true;
        }

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
                    result[row++] = unresolvedBlanks && value == -1.23456789E-300 ? default : new Variant(value);
                }
                if ((count & 1) != 0 && (pair & 0xf0) != 0)
                    return false;
            }
            values = result;
            return true;
        }

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
                if (TryReadTLVRecord(data, position, 0x60, out var record) && record.NextOffset <= scanEnd) // to be proven: is 0x60 really the maximum length of a record? The engineering spec says 0x7f, but that would be a problem for resynchronization.
                {
                    // test if the new record is the preview image table record
                    if (record.Payload.Length == 0x0A &&
                      prvwOPJUmarker.SequenceEqual(record.Payload.Span.Slice(0, prvwOPJUmarker.Length)))
                    {
                        // the length of the record is 0x0A, but it is 6 bytes more, so make a new TLVRecord with the correct length
                        AddRecord(new TLVRecord(record.Offset, record.Tag, new ReadOnlyMemory<byte>(data, record.Offset + 2, 0x10)));

                        // get the index into the preview image table
                        var previewImageTableOffset = (int)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan().Slice(position + 2 + prvwOPJUmarker.Length, 8));
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

                // Preserve unrecognized bytes until four consecutive candidates permit resynchronization,
                // or until the next indexed image boundary is reached.
                int gapStart = position++;
                while (position < scanEnd && !HasFourConsecutiveTLVRecords(data, position, scanEnd))
                    ++position;
                AddRecord(new UnknownRegionRecord(gapStart, position - gapStart, new ReadOnlyMemory<byte>(data, gapStart, position - gapStart)));
            }
            return _records.AsReadOnly();
        }

        private static bool TryFindEmbeddedWorksheetDatasetStart(byte[] data, int candidateOffset, int candidateEnd, out int datasetOffset)
        {
            datasetOffset = -1;
            for (int offset = candidateOffset + 2; offset <= candidateEnd - 3; ++offset)
            {
                if (!TryReadTLVRecord(data, offset, 0x60, out var width) || width.Tag != 0x85 ||
                    width.Payload.Length != 1 || width.Payload.Span[0] is not (1 or 2 or 4 or 8 or 10 or 16 or 50) ||
                    width.NextOffset != candidateEnd || !HasWorksheetDatasetHeader(data, width.NextOffset))
                    continue;

                datasetOffset = offset;
                return true;
            }
            return false;
        }

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
                !((descriptor.Tag == 0x8f &&
                   (descriptor.Payload.Span.StartsWith(new byte[] { 0xc8, 0x10 }) ||
                    descriptor.Payload.Span.StartsWith(new byte[] { 0xca, 0x10 }) ||
                    descriptor.Payload.Span.StartsWith(new byte[] { 0xe8, 0x10 }))) ||
                  (descriptor.Tag == 0x8d && descriptor.Payload.Span.StartsWith(new byte[] { 0xca, 0x10 })) ||
                  (descriptor.Tag is 0x8e or 0x90 && descriptor.Payload.Length == 4 &&
                   (descriptor.Payload.Span.StartsWith(new byte[] { 0xc8, 0x10 }) ||
                    descriptor.Payload.Span.StartsWith(new byte[] { 0xca, 0x10 })))) ||
                !TryReadTLVRecord(data, descriptor.NextOffset, 0x60, out var envelope) ||
                envelope.Tag is not (0x9b or 0x9d) || envelope.Payload.Length != 24 ||
                !envelope.Payload.Span[..8].SequenceEqual(new byte[] { 0x18, 0, 0, 0, 0, 0, 0, 0 }) ||
                envelope.NextOffset > data.Length - 2 || data[envelope.NextOffset] != 0x0a ||
                data[envelope.NextOffset + 1] is not (0x04 or 0x05))
                return false;

            return true;
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


        /// <summary>Locates PNG images using the observed embedded-preview index.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="imageTableOffset">The index where the image table starts.</param>
        /// <param name="bodyStart">The index of the start of the body</param>
        /// <returns>Validated image offsets and lengths in file order, or an empty list if the index is absent or invalid.</returns>
        /// <remarks>The index layout is inferred from observed files. An invalid entry causes the entire index to be ignored.</remarks>
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

        /// <summary>Finds possible sheet names in adjacent parsed records matching an observed signature.</summary>
        /// <returns>Printable sheet-name candidates with their file offsets.</returns>
        /// <remarks>The signature does not establish a sheet's type or parent book.</remarks>
        private bool TryReadWorksheetSheetName(int index, [NotNullWhen(true)] out SheetNameCandidate? candidate,
            out int number, out int columns)
        {
            candidate = null;
            number = columns = 0;
            if (index < 2 || index + 2 >= Records.Count ||
                Records[index] is not TLVRecord columnCount || columnCount.Tag != 0x89 || columnCount.Payload.Length != 1 ||
                Records[index - 1] is not TLVRecord numeric || numeric.Tag != 0x88 || numeric.Payload.Length != 8 ||
                Records[index - 2] is not TLVRecord ordinalMarker || ordinalMarker.Payload.Length != 1 ||
                Records[index + 1] is not TLVRecord marker || marker.Tag != 0x80 ||
                !marker.Payload.Span.SequenceEqual(new byte[] { 0xf7 }))
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

            int offset = marker.NextOffset;
            if (!TryCopyRecordBytes(index + 2, offset, 10, out var header) ||
                !header.AsSpan(0, 9).SequenceEqual(new byte[] { 0xc0, 0xff, 0x8e, 0x04, 0xff, 0x01, 0, 0x04, 0xa4 }) ||
                header[9] == 0 || !TryCopyRecordBytes(index + 2, offset, 14 + header[9], out var encoded) ||
                !encoded.AsSpan(10 + header[9], 4).SequenceEqual(new byte[] { 0x0a, 0, 0x10, 1 }))
                return false;
            string name;
            try
            {
                name = new UTF8Encoding(false, true).GetString(encoded, 10, header[9]);
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
            columns = columnCount.Payload.Span[0];
            candidate = new SheetNameCandidate(offset + 10, name);
            return true;
        }

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

        private List<(int Index, string Name)> FindWindowCandidates()
        {
            var windows = new List<(int Index, string Name)>();
            for (int i = 0; i + 2 < Records.Count; ++i)
            {
                if (Records[i + 1] is TLVRecord geometry &&
                    Records[i + 2] is TLVRecord marker && marker.Payload.Length == 5 &&
                    (geometry.Payload.Length == 12 && marker.Tag == 0x84 ||
                     geometry.Tag == 0x91 && geometry.Payload.Length == 15 && marker.Tag == 0x81) &&
                    TryGetWindowName(Records[i], out var name) &&
                    Records[i].NextOffset == geometry.Offset && geometry.NextOffset == marker.Offset)
                    windows.Add((i, name));
            }
            return windows;
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
                if (!TryReadTLVRecord(data, offset, 0x60, out var record) || record.NextOffset > endOffset)
                    return false;
                offset = record.NextOffset;
            }
            return true;
        }
    }
}
