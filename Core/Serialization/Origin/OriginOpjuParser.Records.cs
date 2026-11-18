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
    }
}
