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
    public partial class OriginOpjuParser
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
            /// Dumps the content to the specified writer for debugging and for the AI.
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
        /// The encoded value block of a dataset, kept together as one record.
        /// </summary>
        /// <remarks>
        /// A value block is a binary sub blob inside the record stream and not a run of value records. Where the
        /// envelope in front of the block states its byte count, the scanner merges the block into this record, so
        /// that the block has a boundary of its own and no coincidental candidate record is reported inside it.
        /// Only blocks whose envelope carries a usable length are merged: the envelopes of text and single precision
        /// columns hold a filler instead of a length at that position, so their blocks stay unknown regions.
        /// </remarks>
        /// <param name="Offset">The byte offset of the first byte of the block.</param>
        /// <param name="Payload">The encoded bytes of the block.</param>
        [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay,nq}")]
        public sealed record ValueBlockRecord(int Offset, ReadOnlyMemory<byte> Payload) : IRecord
        {
            /// <summary>Gets a one-line view of the length and first payload bytes for the debugger.</summary>
            private string DebuggerDisplay => $"VAL Offs={Offset:X}, L={Payload.Length:X}, C={BitConverter.ToString(Payload.Span[..Math.Min(Payload.Length, 8)].ToArray()).Replace('-', ' ')}";

            /// <summary>The number of encoded bytes of the block.</summary>
            public int Length => Payload.Length;

            /// <inheritdoc/>
            public int NextOffset => Offset + Payload.Length;

            /// <summary>
            /// Dumps the content to the specified writer.
            /// </summary>
            /// <param name="writer">The writer.</param>
            public void Dump(TextWriter writer)
            {
                writer.WriteLine($"{Offset:X8}: VALUEBLOCK Length={Payload.Length} {BitConverter.ToString(Payload.Span[..Math.Min(16, Payload.Length)].ToArray()).Replace('-', ' ')}");

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
        /// A parent record: a record whose content is a sub-header followed by a run of child records.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A parent record is framed differently from a plain candidate record: its length byte counts one byte more
        /// than its content, so its content ends at <c>Offset + 1 + DeclaredLength</c> and not at
        /// <c>Offset + 2 + DeclaredLength</c>. Reading such a record with the plain rule therefore shifts the byte
        /// behind the record into the record and hides the start of the following record.
        /// </para>
        /// <para>
        /// The content of a parent record starts with the byte count of a short sub-header, which is followed by the
        /// child records. The child records themselves are ordinary candidate records and are kept in the flat record
        /// list of the scanner; the <see cref="Children"/> of this record are the very same record objects. Only the
        /// header of the parent is a record of its own, so that the record list stays an exact partition of the body.
        /// </para>
        /// <para>
        /// The window description is the named form of a parent record and is read by
        /// <see cref="TryReadWindowRecord"/> instead: its length byte counts its content exactly.
        /// </para>
        /// </remarks>
        /// <param name="Offset">The byte offset of the tag from the beginning of the file.</param>
        /// <param name="Tag">The tag of the record.</param>
        /// <param name="SubHeader">The bytes between the length byte and the first child record; the first byte is the byte count of the sub-header.</param>
        /// <param name="ContentEnd">The offset directly behind the content of the parent record.</param>
        /// <param name="Children">The child records and the byte runs between them, in file order.</param>
        [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay,nq}")]
        public sealed record ParentRecord(int Offset, byte Tag, ReadOnlyMemory<byte> SubHeader, int ContentEnd, IReadOnlyList<IRecord> Children) : IRecord
        {
            /// <summary>Gets a one-line view of the declared length and the child count for the debugger.</summary>
            private string DebuggerDisplay => $"PAR Offs={Offset:X}, Tag={Tag:X2}, L={DeclaredLength:X2}, End={ContentEnd:X}, Children={Children.Count}";

            /// <summary>The length the record declares in its length byte, which is one byte more than the length of its content.</summary>
            public int DeclaredLength => ContentEnd - Offset - 1;

            /// <inheritdoc/>
            public int NextOffset => Offset + 2 + SubHeader.Length;

            /// <summary>
            /// Dumps the content to the specified writer.
            /// </summary>
            /// <param name="writer">The writer.</param>
            /// <remarks>The child records are not dumped here because they are records of the flat record list in their own right.</remarks>
            public void Dump(TextWriter writer)
            {
                writer.WriteLine($"{Offset:X8}: PARENT Tag={Tag:X2} DeclaredLength={DeclaredLength:X2} Content={Offset + 2:X8}..{ContentEnd:X8} SubHeader={BitConverter.ToString(SubHeader.Span[..Math.Min(8, SubHeader.Length)].ToArray()).Replace('-', ' ')} Children={Children.Count}");
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

        // ----- The scanner: partitioning the project bytes into records -----
        //
        // The scanner runs in four steps:
        //   1. the header line and the root header are read,
        //   2. the preview images and their index are registered before any byte scan runs, so that a gap can
        //      never end inside an image and image bytes can never be mistaken for candidate records,
        //   3. the body is scanned for parent records with their child records, candidate value records, window descriptions and gaps,
        //   4. the value blocks whose length their envelope states are merged into single records.
        //
        // A boundary is proven only where the format states a length: in a value block envelope, in a window
        // description and in an image table entry. Everywhere else the boundaries come from resynchronization and
        // are scanner artifacts, which is why the interpretation passes validate their own expectations instead
        // of trusting the boundaries.

        /// <summary>
        /// The maximum payload length a plain candidate record may declare. Longer records are only accepted as
        /// window descriptions (see <see cref="TryReadWindowRecord"/>), because a longer length byte would make the
        /// resynchronization after an unknown region unreliable.
        /// </summary>
        private const int MaxTLVLength = 0x60;

        /// <summary>
        /// The maximum payload length a parent record may declare.
        /// </summary>
        /// <remarks>
        /// Every parent of the fixture corpus declares at most <c>0x7f</c> bytes. The only two candidates with a
        /// longer length byte are false positives: they carry a tag other than <c>0x80</c>, and their run of child
        /// records is held together by a coincidental record that starts inside float data, e.g. the bytes
        /// <c>00 00 80 3f</c>, which is the float 1.0, read as the record <c>80 3f</c> with 63 bytes of payload.
        /// </remarks>
        private const int MaxParentLength = 0x7f;

        /// <summary>The ASCII marker that introduces the preview image table pointer.</summary>
        private static ReadOnlySpan<byte> PreviewMarker => "PrvwOPJU"u8;

        /// <summary>
        /// The preview marker record declares 0x0a bytes but occupies 0x10: the eight bytes of the marker text
        /// followed by the 64 bit image table pointer, which the length byte does not count. This is one of the two
        /// known framing errors of the writer; the other is the description of a notes window, whose length byte is
        /// one byte too short.
        /// </summary>
        private const int PreviewMarkerDeclaredLength = 0x0a;
        private const int PreviewMarkerActualLength = 0x10;

        /// <summary>The eight payload bytes every value block envelope starts with.</summary>
        private static ReadOnlySpan<byte> ValueBlockEnvelopePrefix => [0x18, 0, 0, 0, 0, 0, 0, 0];

        /// <summary>The records built up by <see cref="ParseIntoRecords"/> in ascending offset order.</summary>
        private List<IRecord> _records = [];

        /// <summary>Reads the project header and partitions the body into records, indexed PNG images and unknown regions.</summary>
        /// <param name="originFile">The stream containing the project bytes.</param>
        /// <returns>The body records in file order.</returns>
        /// <remarks>
        /// Record recognition and resynchronization are heuristic; unknown regions are scanner gaps, not necessarily
        /// native format blocks. The value block is the exception: its length is stated by its envelope, so it becomes
        /// a record with a proven boundary.
        /// </remarks>
        private IReadOnlyList<IRecord> ParseIntoRecords(Stream originFile)
        {
            // Read records data into memory.
            var data = new byte[originFile.Length];
            originFile.ReadExactly(data, 0, data.Length);
            _fileData = data;

            ReadProjectHeader(data);

            // Registering the images before the scan gives the scan hard boundaries to stop at: neither a gap nor a
            // candidate record can reach into an image any more.
            RegisterEmbeddedImages(data, BodyOffset);

            ScanRecordStream(data, BodyOffset + 7);

            MergeValueBlocks(data);

            return _records.AsReadOnly();
        }

        /// <summary>Reads the header line and the root header that follows it.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <exception cref="InvalidDataException">The header line or root header is invalid or incomplete.</exception>
        [MemberNotNull(nameof(FormatVersion))]
        [MemberNotNull(nameof(ObjectCount))]
        [MemberNotNull(nameof(BodyOffset))]
        [MemberNotNull(nameof(RootHeader))]
        private void ReadProjectHeader(byte[] data)
        {
            // The file starts with a header line terminated by 0x0a, which is followed by the 7 byte root header.
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
        }

        /// <summary>Locates the preview image index and registers the image table and every image it lists.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="bodyStart">The offset of the start of the body, which bounds the image offsets.</param>
        /// <remarks>
        /// The marker text is the payload of a length prefixed record whose tag differs between project generations
        /// (0x81 in the newer, 0x84 in the older fixtures) while its length byte does not, so the marker is located
        /// by its text. Throughout the corpus the text occurs exactly once per project.
        /// </remarks>
        /// <exception cref="InvalidDataException">The image index is invalid; see <see cref="RegisterIndexedPngs"/>.</exception>
        private void RegisterEmbeddedImages(byte[] data, int bodyStart)
        {
            if (!TryLocatePreviewMarker(data, out int markerOffset))
                return;

            // A project that holds nothing but notes windows has neither a preview image nor an image table. Its
            // pointer is zero, which is a missing table rather than a truncated one.
            int pointerOffset = markerOffset + 2 + PreviewMarker.Length;
            if (pointerOffset + 8 > data.Length)
                return;
            long tableOffset = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(pointerOffset, 8));
            if (tableOffset != 0)
                RegisterIndexedPngs(data, (int)tableOffset, bodyStart);
        }

        /// <summary>Finds the record that carries the preview marker text.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="markerOffset">The offset of the tag byte of the marker record.</param>
        /// <returns><see langword="true"/> if a properly framed marker was found.</returns>
        private static bool TryLocatePreviewMarker(byte[] data, out int markerOffset)
        {
            markerOffset = -1;
            for (int offset = 2; offset + PreviewMarker.Length + 8 <= data.Length; ++offset)
            {
                if (!data.AsSpan(offset, PreviewMarker.Length).SequenceEqual(PreviewMarker))
                    continue;

                int tagOffset = offset - 2;
                if (data[tagOffset] < 0x80 || data[tagOffset] > 0x9f || data[tagOffset + 1] != PreviewMarkerDeclaredLength)
                    continue;

                markerOffset = tagOffset;
                return true;
            }
            return false;
        }

        /// <summary>Scans the body for parent records, candidate value records, window descriptions and gaps.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="start">The offset the scan starts at, which is the offset behind the root header.</param>
        private void ScanRecordStream(byte[] data, int start)
        {
            // The scan walks forward through the body. A byte run that cannot be interpreted as a record is collected
            // as an unknown region until the scan can resynchronize on a plausible record start.
            int position = start;
            while (position < data.Length)
            {
                // Skip a record that the scan reached from inside, e.g. an indexed PNG image.
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
                if (low > 0 && position < _records[low - 1].NextOffset)
                {
                    position = _records[low - 1].NextOffset;
                    continue;
                }

                // A candidate record must not cross a record that is already known, because that record is a boundary.
                var scanEnd = low < _records.Count ? _records[low].Offset : data.Length;

                // A parent record has to be recognized before the plain candidate scan: its length byte counts one byte
                // more than its content, so the plain scan would swallow the first byte of the record behind it.
                if (TryReadParentRecord(data, position, scanEnd, out var parentRecord))
                {
                    AddRecord(parentRecord);
                    foreach (var child in parentRecord.Children)
                        AddRecord(child);
                    position = parentRecord.ContentEnd;
                    continue;
                }

                if (TryReadTLVRecord(data, position, MaxTLVLength, out var record) && record.NextOffset <= scanEnd) // to be proven: is 0x60 really the maximum length of a plain record? The engineering spec says 0x7f, but that would be a problem for resynchronization. Longer records are handled as window descriptions.
                {
                    if (record.Payload.Length == PreviewMarkerDeclaredLength && record.Payload.Span.StartsWith(PreviewMarker))
                    {
                        // The record carries the 64 bit image table pointer behind the marker text, which its length
                        // byte does not count. Emit it with the length it actually occupies.
                        AddRecord(new TLVRecord(record.Offset, record.Tag,
                            new ReadOnlyMemory<byte>(data, record.Offset + 2, PreviewMarkerActualLength)));
                        position = record.Offset + 2 + PreviewMarkerActualLength;
                        if (TryReadLengthPrefixedBlock(data, position, MaxTLVLength, out var rec))
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
                    else if (TryReadValueBlockBehindEnvelope(data, low - 1, record, scanEnd, out int blockStart, out int blockEnd))
                    {
                        // The envelope announces the byte count of the value block behind it. Consuming the block here
                        // keeps the scan from resynchronizing on coincidental candidate records inside the block, which
                        // would carry the scan over the record that follows the block.
                        AddRecord(record);
                        AddRecord(new UnknownRegionRecord(record.NextOffset, 1, new ReadOnlyMemory<byte>(data, record.NextOffset, 1)));
                        AddRecord(new ValueBlockRecord(blockStart, new ReadOnlyMemory<byte>(data, blockStart, blockEnd - blockStart)));
                        position = blockEnd;
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
                // the start of a parent or window description, or the next indexed image boundary is reached.
                int gapStart = position++;
                bool windowStartsHere = false;
                bool parentStartsHere = false;
                while (position < scanEnd)
                {
                    if (TryReadParentRecord(data, position, scanEnd, out parentRecord))
                    {
                        parentStartsHere = true;
                        break;
                    }
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
                if (parentStartsHere)
                {
                    AddRecord(parentRecord);
                    foreach (var child in parentRecord.Children)
                        AddRecord(child);
                    position = parentRecord.ContentEnd;
                }
                else if (windowStartsHere)
                {
                    AddRecord(windowRecord);
                    position = windowRecord.NextOffset;
                }
            }
        }

        /// <summary>Merges every value block whose length its envelope states into a single <see cref="ValueBlockRecord"/>.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <remarks>
        /// The envelope of a value block is a 24 byte record whose payload holds the byte count of the encoded block
        /// that follows it. Where that count is usable, the candidate records and gaps the scanner reported inside
        /// the block are replaced by one block record, which gives the block a boundary of its own. The single byte
        /// that separates the envelope from the block, and the bytes behind the block, stay unknown regions.
        /// </remarks>
        private void MergeValueBlocks(byte[] data)
        {
            var result = new List<IRecord>(_records.Count);
            for (int index = 0; index < _records.Count; ++index)
            {
                if (!TryGetValueBlockSpan(index, data, out int blockStart, out int blockEnd, out int lastIndex))
                {
                    result.Add(_records[index]);
                    continue;
                }

                result.Add(_records[index]);
                if (_records[index + 1].Offset < blockStart)
                    result.Add(NewRegion(data, _records[index + 1].Offset, blockStart));
                result.Add(new ValueBlockRecord(blockStart, new ReadOnlyMemory<byte>(data, blockStart, blockEnd - blockStart)));
                if (_records[lastIndex].NextOffset > blockEnd)
                    result.Add(NewRegion(data, blockEnd, _records[lastIndex].NextOffset));
                index = lastIndex;
            }
            _records = result;
        }

        /// <summary>Checks whether the record at the given index is the envelope of a value block of known length.</summary>
        /// <param name="envelopeIndex">The index of the candidate envelope record.</param>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="blockStart">The offset of the first byte of the encoded block.</param>
        /// <param name="blockEnd">The offset directly behind the encoded block.</param>
        /// <param name="lastIndex">The index of the last record the scanner reported inside the block.</param>
        /// <returns><see langword="true"/> if the length the envelope states is supported by the records around it.</returns>
        /// <remarks>
        /// The length field is unusable for the element types that do not announce their element width in front of
        /// the envelope; the envelopes of text and single precision columns hold a filler instead of a length there
        /// and are rejected by the range check of the length. The check that no record inside the block starts
        /// another dataset keeps a wrong length from swallowing the records of the following dataset.
        /// </remarks>
        private bool TryGetValueBlockSpan(int envelopeIndex, byte[] data, out int blockStart, out int blockEnd, out int lastIndex)
        {
            blockStart = blockEnd = lastIndex = 0;
            if (_records[envelopeIndex] is not TLVRecord envelope ||
                envelopeIndex == 0 || envelopeIndex + 1 >= _records.Count ||
                _records[envelopeIndex - 1] is not TLVRecord descriptor || !IsWorksheetDatasetDescriptor(descriptor) ||
                descriptor.NextOffset != envelope.Offset || _records[envelopeIndex + 1].Offset != envelope.NextOffset ||
                !TryGetValueBlockSpan(data, envelope, data.Length, out blockStart, out blockEnd))
                return false;

            // The block has to be covered by candidate records and gaps only. An image, an image table or a window
            // description inside the announced range means that the announced length cannot be trusted.
            lastIndex = envelopeIndex + 1;
            int position = _records[lastIndex].Offset;
            while (true)
            {
                if (lastIndex >= _records.Count)
                    return false;
                var record = _records[lastIndex];
                if (record is ImageTable or PngRecord || record is not (TLVRecord or UnknownRegionRecord or ParentRecord) ||
                    record.Offset > position || record.NextOffset <= position)
                    return false;
                if (record is TLVRecord inner && HasWorksheetDatasetHeader(data, inner.Offset))
                    return false;

                position = record.NextOffset;
                if (position >= blockEnd)
                    return true;
                ++lastIndex;
            }
        }

        /// <summary>Checks whether the value block behind the given envelope announces a usable byte count.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="envelope">The candidate envelope record.</param>
        /// <param name="endOffset">The exclusive end of the area the block is allowed to occupy.</param>
        /// <param name="blockStart">The offset of the first byte of the encoded block.</param>
        /// <param name="blockEnd">The offset directly behind the encoded block.</param>
        /// <returns><see langword="true"/> if the byte count the envelope states is supported by the bytes around it.</returns>
        /// <remarks>
        /// The envelope is closed by a 0x0a byte, the block starts with its kind byte and the announced length has to
        /// end on a byte that starts the following structure. A wrong length must not swallow the records of the
        /// following dataset, so the announced range is rejected as soon as it holds a worksheet dataset header.
        /// </remarks>
        private static bool TryGetValueBlockSpan(byte[] data, TLVRecord envelope, int endOffset, out int blockStart, out int blockEnd)
        {
            blockStart = blockEnd = 0;
            if ((envelope.Tag != 0x9b && envelope.Tag != 0x9d) || envelope.Payload.Length != 24 ||
                !envelope.Payload.Span[..8].SequenceEqual(ValueBlockEnvelopePrefix))
                return false;

            ulong length = BinaryPrimitives.ReadUInt64LittleEndian(envelope.Payload.Span.Slice(8, 8));
            if (length is < 4 or > 1_000_000)
                return false;

            // The envelope payload is closed by a 0x0a byte; the block itself starts with its kind byte.
            blockStart = envelope.NextOffset + 1;
            if (blockStart >= data.Length || data[envelope.NextOffset] != 0x0a || data[blockStart] is not (0x04 or 0x05))
                return false;

            blockEnd = blockStart + (int)length;
            if (blockEnd >= data.Length || blockEnd > endOffset || data[blockEnd] is not (0x00 or 0x02))
                return false;

            for (int offset = blockStart; offset <= blockEnd - 3; ++offset)
                if (HasWorksheetDatasetHeader(data, offset))
                    return false;

            return true;
        }

        /// <summary>Checks whether the candidate record the scan just read is the envelope of a value block the scan can consume.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="previousIndex">The index of the record the scan read before the envelope, or -1.</param>
        /// <param name="envelope">The candidate envelope record.</param>
        /// <param name="endOffset">The exclusive end of the area the block is allowed to occupy.</param>
        /// <param name="blockStart">The offset of the first byte of the encoded block.</param>
        /// <param name="blockEnd">The offset directly behind the encoded block.</param>
        /// <returns><see langword="true"/> if the envelope belongs to a dataset and its block can be consumed.</returns>
        /// <remarks>
        /// An envelope belongs to the dataset whose short descriptor sits directly in front of it. The record list is
        /// kept in offset order, so the record in front of the envelope is the record at <paramref name="previousIndex"/>,
        /// which the scan determines while it looks for the next known boundary.
        /// </remarks>
        private bool TryReadValueBlockBehindEnvelope(byte[] data, int previousIndex, TLVRecord envelope, int endOffset, out int blockStart, out int blockEnd)
        {
            blockStart = blockEnd = 0;
            if (previousIndex < 0 || _records[previousIndex] is not TLVRecord descriptor || !IsWorksheetDatasetDescriptor(descriptor) ||
                descriptor.NextOffset != envelope.Offset)
                return false;

            return TryGetValueBlockSpan(data, envelope, endOffset, out blockStart, out blockEnd);
        }

        /// <summary>Creates an unknown region covering the half-open byte range.</summary>
        private static UnknownRegionRecord NewRegion(byte[] data, int start, int endExclusive)
            => new(start, endExclusive - start, new ReadOnlyMemory<byte>(data, start, endExclusive - start));

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
            if (!TryReadTLVRecord(data, offset, MaxTLVLength, out var nameMarker) || nameMarker.Tag != 0x91 ||
                nameMarker.Payload.Length != 1 || nameMarker.Payload.Span[0] is < 1 or > 9 ||
                !TryReadTLVRecord(data, nameMarker.NextOffset, MaxTLVLength, out var nameRecord) || nameRecord.Tag != 0x82 ||
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

            if (!TryReadTLVRecord(data, nameRecord.NextOffset, MaxTLVLength, out var descriptor) ||
                !IsWorksheetDatasetDescriptor(descriptor) ||
                !TryReadTLVRecord(data, descriptor.NextOffset, MaxTLVLength, out var envelope) ||
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

        /// <summary>Registers the embedded preview images using the observed image index.</summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="imageTableOffset">The offset where the image table starts.</param>
        /// <param name="bodyStart">The offset of the start of the body, which bounds the image offsets.</param>
        /// <remarks>
        /// The index layout is inferred from observed files. Every entry has to frame a complete PNG image and the entries
        /// must not overlap. The scanner treats the located images as boundaries that TLV scanning cannot cross.
        /// </remarks>
        /// <exception cref="InvalidDataException">The index is truncated, an entry does not frame a PNG image, or entries overlap.</exception>
        private void RegisterIndexedPngs(byte[] data, int imageTableOffset, int bodyStart)
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
            if (!HasWindowNameField(span, out int nameLength))
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
        /// Checks whether the payload starts with the name field <c>&lt;length&gt; 00 00 &lt;name&gt;</c> of a window description.
        /// </summary>
        /// <param name="payload">The payload of the candidate record.</param>
        /// <param name="nameLength">The length of the name, which excludes the two zero bytes of the field.</param>
        /// <returns><see langword="true"/> if the payload has the shape of a name field.</returns>
        private static bool HasWindowNameField(ReadOnlySpan<byte> payload, out int nameLength)
        {
            nameLength = 0;
            if (payload.Length < 4 || payload[0] < 3 || payload[1] != 0 || payload[2] != 0)
                return false;

            int length = payload[0] - 2;
            if (length > payload.Length - 3)
                return false;
            for (int i = 0; i < length; ++i)
                if (payload[3 + i] < 0x20 || payload[3 + i] >= 0x7f)
                    return false;

            nameLength = length;
            return true;
        }

        /// <summary>
        /// Tries to read a parent record at the given offset.
        /// </summary>
        /// <param name="data">The complete project bytes.</param>
        /// <param name="offset">The offset of the tag to interpret.</param>
        /// <param name="endOffset">The exclusive end of the area available for scanning.</param>
        /// <param name="record">The parent record with its child records, if the observed framing matches.</param>
        /// <returns><see langword="true"/> if a parent record was recognized.</returns>
        /// <remarks>
        /// <para>
        /// The framing of a parent record differs from the framing of a plain candidate record in two ways, both of
        /// which have been observed consistently: its length byte counts one byte more than its content, and its
        /// content is a short sub-header followed by child records. The sub-header is introduced by its own byte
        /// count, e.g. <c>03 00 00 01</c> for a three byte sub-header or <c>01 10</c> for a one byte sub-header.
        /// </para>
        /// <para>
        /// Because that framing alone is only half of the evidence, the run of child records is the decisive part: it
        /// has to start immediately behind the sub-header, every child has to stay inside the content, and the run has
        /// to close the content with a child record. Byte runs between two children, which carry e.g. the doubles of a
        /// column description, are permitted and are kept as gaps. The named form, whose content starts with a name
        /// field, is left to <see cref="TryReadWindowRecord"/>, because a window description counts its content exactly.
        /// </para>
        /// </remarks>
        private static bool TryReadParentRecord(byte[] data, int offset, int endOffset, [NotNullWhen(true)] out ParentRecord? record)
        {
            record = null;
            if (offset >= data.Length - 1 || data[offset] < 0x80 || data[offset] > 0x9f)
                return false;

            int declaredLength = data[offset + 1];
            // The content has to hold the sub-header length byte, at least one sub-header byte and one child record.
            if (declaredLength < 4 || declaredLength > MaxParentLength)
                return false;

            // The content of a parent record ends one byte before the end of a plain candidate record with the same length byte.
            int contentEnd = offset + 1 + declaredLength;
            if (contentEnd > Math.Min(endOffset, data.Length) || contentEnd >= data.Length)
                return false;

            var payload = new ReadOnlySpan<byte>(data, offset + 2, declaredLength);
            if (payload.StartsWith(PreviewMarker) || HasWindowNameField(payload, out _))
                return false;

            int subHeaderLength = payload[0];
            if (subHeaderLength < 1)
                return false;

            int firstChild = offset + 2 + 1 + subHeaderLength;
            if (firstChild >= contentEnd)
                return false;

            // The child records start immediately behind the sub-header.
            if (!TryReadTLVRecord(data, firstChild, MaxTLVLength, out var firstCandidate) || firstCandidate.NextOffset > contentEnd)
                return false;

            // Walk the child records. A byte run that is not a child record is kept as a gap, but it has to be
            // followed by a child record; a child record that would cross the end of the content does not belong here.
            var children = new List<IRecord>();
            int position = firstChild;
            while (position < contentEnd)
            {
                if (TryReadTLVRecord(data, position, MaxTLVLength, out var child) && child.NextOffset <= contentEnd)
                {
                    children.Add(child);
                    position = child.NextOffset;
                    continue;
                }

                int gapStart = position;
                do
                {
                    ++position;
                    if (position >= contentEnd)
                        return false;
                }
                while (!TryReadTLVRecord(data, position, MaxTLVLength, out var nextCandidate) || nextCandidate.NextOffset > contentEnd);

                children.Add(new UnknownRegionRecord(gapStart, position - gapStart, new ReadOnlyMemory<byte>(data, gapStart, position - gapStart)));
            }

            record = new ParentRecord(offset, data[offset], new ReadOnlyMemory<byte>(data, offset + 2, firstChild - (offset + 2)), contentEnd, children.AsReadOnly());
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
