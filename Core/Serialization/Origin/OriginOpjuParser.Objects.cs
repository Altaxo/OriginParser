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
using System.Collections.Generic;

namespace Altaxo.Serialization.Origin
{
    public partial class OriginOpjuParser
    {
        /// <summary>Represents a top-level window with common metadata and appearance settings.</summary>
        public class Window
        {
            /// <summary>Name of the window.</summary>
            public string Name;

            /// <summary>
            /// The long name associated with the window (can contain special chars).
            /// </summary>
            public string LongName;

            /// <summary>
            /// The comment associated with the window.
            /// </summary>
            public string Comment;

            /// <summary>Project tree path of the folder the window is stored in.</summary>
            public string FolderName;

            /// <summary>Which title is displayed for the window.</summary>
            public WindowTitle Title;

            /// <summary>Rectangle that defines the window frame.</summary>
            public Rect FrameRect;

            /// <summary>Creation timestamp of the window.</summary>
            public DateTimeOffset CreationDate;

            /// <summary>Last modification timestamp of the window.</summary>
            public DateTimeOffset ModificationDate;
        }


        /// <summary>A worksheet book (workbook) assembled from the dataset names of its columns.</summary>
        /// <param name="Name">The book name, which is the part before the last underscore of a dataset name.</param>
        /// <param name="Label">The book label. Not recovered yet, therefore always <see langword="null"/>.</param>
        /// <param name="Sheets">The sheets of the book, addressable by their one-based sheet number.</param>
        public class Book : Window
        {
            public IReadOnlyList<Sheet> Sheets { get; set; } = Array.Empty<Sheet>();
        }

        /// <summary>A simple note window containing text.</summary>
        public class Note : Window
        {
            /// <summary>Text content of the note.</summary>
            public string Text;


        }

        public class Graph : Window
        {
        }

        /// <summary>Represents a matrix window containing one or more sheets.</summary>
        public class Matrix : Window
        {
            /// <summary>
            /// Defines the supported header display modes for a matrix.
            /// </summary>
            public enum HeaderViewType
            {
                /// <summary>Show column and row headers.</summary>
                ColumnRow,

                /// <summary>Show X and Y headers.</summary>
                XY
            };

            /// <summary>Index of the active sheet in the matrix.</summary>
            public int ActiveSheet;

            /// <summary>Header view type used by the matrix.</summary>
            public HeaderViewType Header;

            /// <summary>Sheets that belong to this matrix.</summary>
            public List<MatrixSheet> Sheets = [];

            /// <summary>Initializes a new instance of <see cref="Matrix"/>.</summary>
            public Matrix()
            {
                ActiveSheet = 0;
                Header = HeaderViewType.ColumnRow;
            }
        }


        /// <summary>A worksheet sheet, identified by the sheet number encoded in the dataset names.</summary>
        /// <param name="Number">The one-based sheet number within the book.</param>
        /// <param name="RowCount">The number of rows. Not recovered yet, therefore always <see langword="null"/>.</param>
        /// <param name="Columns">The columns of the sheet in file order.</param>
        public record Sheet(int Number, int? RowCount, IReadOnlyList<SpreadColumn> Columns)
        {
            /// <summary>Gets or sets the sheet name; empty when it was not recovered.</summary>
            public string Name { get; set; } = string.Empty;
        }




        /// <summary>A matrix dataset together with the dimensions inferred from the surrounding records.</summary>
        /// <param name="Name">The dataset name of the matrix.</param>
        /// <param name="NumberOfRows">The inferred row count, or <see langword="null"/> when it could not be inferred.</param>
        /// <param name="NumberOfColumns">The inferred column count, or <see langword="null"/> when it could not be inferred.</param>
        private sealed record MatrixCandidate(string Name, int? NumberOfRows, int? NumberOfColumns);


        /// <summary>A notes window located by the record carrying its name and by the text block behind it.</summary>
        /// <param name="Offset">The file offset of the record that carries the window name.</param>
        /// <param name="Name">The name of the notes window.</param>
        /// <param name="LongName">The long name of the notes window.</param>
        /// <param name="Comment">The comment behind the long name, or <see langword="null"/> when the text holds none.</param>
        /// <param name="Text">The content of the note behind the comment.</param>
        private sealed record NoteWindowCandidate(int Offset, string Name, string LongName, string? Comment, string Text);


        /// <summary>
        /// Represents a single column of a worksheet: its recovered metadata, the raw encoded data of its value
        /// block, and the cell values decoded from that block.
        /// </summary>
        /// <remarks>
        /// Every member is unresolved right after construction and is filled in only by the parser pass that proves it.
        /// A member that cannot be proven stays <see langword="null"/> when its type offers no value meaning
        /// "unknown", which is the case for <see cref="ValueType"/>, <see cref="DeserializedDataType"/>, the row count
        /// and the decoded values. Members whose type does have a neutral value keep that value instead:
        /// <see cref="SpreadColumnType.Ignore"/> for an unknown plot role, <see cref="ValueTypeSpecification.Numeric_Decimal"/>
        /// for an unknown format, the empty string for an unknown text and 0 for an unknown number or display width.
        /// </remarks>
        public class SpreadColumn : Altaxo.Serialization.Origin.SpreadColumn
        {
            /// <summary>Initializes a new instance of <see cref="SpreadColumn"/> with every member unresolved.</summary>
            /// <param name="name">Name of the column; empty while the column has no resolved dataset name.</param>
            /// <param name="index">Index of the column within its sheet.</param>
            public SpreadColumn(string name = "", int index = 0)
            {
                Name = name;
                Index = index;
                // The display settings below are the defaults Origin applies to a column whose format records are missing.
                SignificantDigits = 6;
                DecimalPlaces = 6;
                NumericDisplayType = NumericDisplayType.DefaultDecimalDigits;
            }

            /// <summary>Initializes a new instance from a dataset located in the project stream, keeping its raw data.</summary>
            /// <param name="datasetName">The dataset name as stored in the file, e.g. <c>Book1_A</c> for sheet 1 or <c>Book1_A@2</c> for sheet 2.</param>
            /// <param name="elementWidth">The element width byte of the width record that introduced the dataset.</param>
            /// <param name="descriptorRecords">The records that make up the dataset header.</param>
            /// <param name="dataStartRecord">The record in which the value block starts.</param>
            /// <param name="dataOffset">The byte offset of the value block within the file, i.e. just behind the envelope's two byte block prefix.</param>
            /// <param name="candidateDataLength">The element count announced by the envelope, when the envelope carries one.</param>
            /// <param name="candidateData">The bytes the scanner could provide for the value block.</param>
            public SpreadColumn(string datasetName, byte elementWidth, IReadOnlyList<IRecord> descriptorRecords,
                IRecord dataStartRecord, int dataOffset, ulong? candidateDataLength, ReadOnlyMemory<byte>? candidateData)
                : this()
            {
                DatasetName = datasetName;
                ElementWidth = elementWidth;
                DescriptorRecords = descriptorRecords;
                DataStartRecord = dataStartRecord;
                DataOffset = dataOffset;
                CandidateDataLength = candidateDataLength;
                CandidateData = candidateData;
            }


            // The members below keep the raw, partly unproven view of the dataset so that a later pass, or a later
            // investigation, can re-examine the bytes the value block was decoded from.
            /// <summary>Gets the element width byte: 1, 2, 4, 8, 16, 50 or 10 (text/numeric) select the value block layout.</summary>
            internal byte ElementWidth { get; init; }
            /// <summary>Gets the records that form the dataset header, from the first recognized header record up to the value block envelope.</summary>
            internal IReadOnlyList<IRecord> DescriptorRecords { get; init; }
            /// <summary>Gets the record in which the value block starts.</summary>
            internal IRecord? DataStartRecord { get; init; }
            /// <summary>Gets the file offset of the value block, i.e. the offset just behind the envelope's two byte block prefix.</summary>
            internal int DataOffset { get; init; }
            /// <summary>Gets the element count announced by the envelope, or <see langword="null"/> when the envelope does not carry one.</summary>
            internal ulong? CandidateDataLength { get; init; }
            // The length and bytes are hypotheses; neither establishes a native block boundary.
            /// <summary>Gets the bytes the scanner made available for the value block, or <see langword="null"/> when they were not available.</summary>
            internal ReadOnlyMemory<byte>? CandidateData { get; init; }
            /// <summary>Gets or sets the records that carry the column designation and value type; <see langword="null"/> when not recovered.</summary>
            internal IReadOnlyList<IRecord>? TypeDescriptorRecords { get; set; }
        }

    }
}
