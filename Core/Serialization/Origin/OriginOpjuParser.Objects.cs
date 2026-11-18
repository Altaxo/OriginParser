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

    }
}
