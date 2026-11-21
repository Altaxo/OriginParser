# Findings

Running log of properties that have been figured out while implementing the Origin `.opju`
reader. Each entry describes the observed byte patterns, where they occur and how the parser
recognizes them.

---

## Worksheet dataset descriptor (dataset header)

**Where:** immediately after the record that carries the dataset (column) name, and immediately
before the 24 byte envelope record `9B 18` / `9D 18` that introduces the value block.

**Shape:** the descriptor is a `TLV` record whose payload is either two or four bytes:

```
8F 04 C8 10 03 03      9B 18 18 00 00 00 00 00 00 00 44 00 ...
^  ^  --  --  --  --   ^
|  |   |   |   |  +-- kind/flags of the value block (03 or 04)
|  |   |   |   +----- always 0x03 / 0x04
|  |   |   +--------- always 0x10 (high byte of the second 16 bit value)
|  |   +------------ 0xC8 / 0xCA / 0xE8 (low byte of the first 16 bit value)
|  +---------------- payload length 2 or 4
+------------------- tag: see below
```

**Important:** the tag names a *value class*, not a field role (see the exbound/opj spec, §2.5:
"A tag names a value's type/width class, not its field role"). Roles are positional. The tag used
for the descriptor therefore varies — `0x87`, `0x8d`, `0x8e`, `0x8f`, `0x90` and `0x93` were seen,
sometimes with different tags for different columns of one and the same workbook. Time columns use
`87 04 E8 10 03 04`, date columns use `93 04 E8 10 03 04`.

Corpus statistics (all `.opju` under `CoreTest/Serialization/Origin/TestFiles`, recursive):
309 envelope sites, every one of them preceded by a four byte descriptor. Two byte forms in use:
`8F 02 CA 10` (162), `8D 02 C8 10` (14), `8F 02 C8 10` (13), `8F 02 E8 10` (7), `90 02 E8 10` (4),
`8D 02 CA 10` (3).

**Parser:** `IsWorksheetDatasetDescriptor(TLVRecord)` recognizes the four byte form by its *shape*
(payload length 4, `payload[1] == 0x10`, `payload[3] in {0x03, 0x04}`, `payload[0] in
{0xC8, 0xCA, 0xE8}`) for the tags that were not accepted before, and keeps the tag restricted
rules for the previously known forms. `FindWorksheetDatasetCandidates` and
`HasWorksheetDatasetHeader` both call this helper.

> Note: changes to descriptor recognition must stay **purely additive**. Replacing the rules with a
> single tag-agnostic shape rule made 203 two byte records with tags `0x8d`/`0x8e`/`0x8f`/`0x90`
> drop out, which silently removed the sheet name and all column metadata of the text content tests.

---

## Column value type in the worksheet column designation region

**Where:** the designation region is separate from the dataset header. A column designation starts
with a header block of 10 or 13 bytes and is followed by the name record and a descriptor record.

Two header layouts occur:

```
empty column:      80 2D 07 10 01 00 00 01 00 | 88 01 41 | 88 02 02 0A | 82 02 01 60 | 8F 01 60 | 8D 01 B1 | BF BF BF BF BF BF
populated column:  ... 0A 80 2F 01 10 80 03 01 00 09 | 81 01 01 | 83 01 41 | 88 02 02 0A | 82 02 01 60 | 8F 01 20 | 8D 01 B1 | BF x6
				   \-------- 13 byte header --------/   row marker  name      descriptor
```

* If the name record has tag `0x88` (length 1) the header is the 10 byte *long* form.
* If the name record has tag `0x82` or `0x83` (populated column) the header is the 13 byte *short*
  form, introduced by a `81 01 <row>` marker record directly in front of the name record.

**Descriptor `88 02 XX YY`:** the first byte is the value type code, the second byte is the format
byte of the value type specification:

| code | value type |
| ---- | ---------- |
| 1 | Text |
| 2 | Time |
| 3 | Date |
| 4 | Month |
| 5 | Day |
| 9 | TextNumeric |

The one byte form `88 01 XX` carries the same code without the format byte, which then counts as the
default `00`. Both forms are mapped by the same switch in `ApplyWorksheetColumnDesignations`. The nine
byte form `88 09 ...` carries binary (`0x0E`) and color (`0x0F`) types.

> Note: for a populated column the scan accepts a descriptor of one or two bytes whose code is `01`
> to `05` or `09`. The one byte form is observed there for `01`, `09`, `02`
> (`ValueTypeSpecificationForTime\OneBookOneSheetOneTimeColumn_HH_MM`) and `03`
> (`ValueTypeSpecificationForDate\OneBookOneSheetOneDateColumn_DDMMYYYY`); for `04` and `05` it is only
> observed for empty columns, so accepting it there is inferred from the other codes.

> Note: `ApplyWorksheetColumnDesignations` bails out (`return`) unless the number of accepted
> candidates equals the number of datasets and each candidate has the expected ordinal and name. A
> single rejected candidate therefore removes name, long name, unit, comment, width **and** value
> type of *all* columns.

---

## Column value type specification

**Where:** the same designation descriptor record as the value type code. For Text, Time, Date, Month
and Day the value type code is the high byte of the 16 bit value type specification and the second
payload byte is its low byte, so the specification is `(payload[0] << 8) | payload[1]`. A one byte
descriptor omits the format byte, which then counts as `00` and is the default specification of that
value type — `88 01 02` is `0x0200` (`Time_HH_MM`), `88 01 01` is `0x0100` (`Text_Default`).

The `ValueTypeSpecificationForTime` fixtures vary only that one byte and cover the whole observed
time range:

| payload | specification | displayed as |
| ------- | ------------- | ------------ |
| `02` (`02 00`) | `0x0200` | `HH:mm` |
| `02 01` | `0x0201` | `HH` |
| `02 02` | `0x0202` | `HH:mm:ss` |
| `02 03` | `0x0203` | `HH:mm:ss,##` |
| `02 04` | `0x0204` | `hh pm` |
| `02 05` | `0x0205` | `hh:mm pm` |
| `02 06` | `0x0206` | `mm:ss` |
| `02 07` | `0x0207` | `mm:ss,##` |
| `02 08` | `0x0208` | `HHmm` |
| `02 09` | `0x0209` | `HHmmss` |
| `02 0A` | `0x020A` | `HH:mm:ss,###` |
| `02 0B` | `0x020B` | `DDD` |
| `02 0C` | `0x020C` | `DDD:HH` |
| `02 0D` | `0x020D` | `DDD:HH:mm` |
| `02 0E` | `0x020E` | `DDD:HH:mm:ss` |
| `02 0F` | `0x020F` | `DDD:HH:mm:ss,##` |
| `02 10` | `0x0210` | `DDD:HH:mm:ss,######` |
| `02 11` | `0x0211` | user defined (`Custom1`) |

Date columns use the same scheme with the code `03`. The `ValueTypeSpecificationForDate` fixtures vary
the low byte over `01`, `06` to `0F` (their names are `_LongDate`, `_Day_LETTER`, `_YYYY`, `_YY`,
`_DDMMYYYY_HH_MM`, `_DDMMYYYY_HH_MM_SS`, `_YYMMDD` and the `_YYMMDD` time variants) and over the
default, which `_DDMMYYYY` serializes as the one byte `88 01 03`. Counting the two byte form over all
`.dump` files under `CoreTest/Serialization/Origin/TestFiles` gives 40 sites, 23 with the time code and
17 with the date code; `02 00` only occurs in the one byte form.

Numeric, TextNumeric, Binary and Color designations use the `8D`, `8F`, `8A` and `8B` descriptors or
the nine byte `88` form, none of which carries a format byte that identifies a format specification in
the corpus. For those columns the specification stays unresolved and keeps its default
`Numeric_Decimal`.

**Parser:** `ApplyWorksheetColumnDesignations` derives the specification in the same place as the
value type and assigns it to `AbstractColumn.ValueTypeSpecification`, but only for Text, Time, Date,
Month and Day, so that an unproven descriptor cannot overwrite it. The `ValueTypeSpecification` enum
gained the members `0x020B` to `0x0211` observed in the time fixtures.

---

## User defined format string in the column long name field

**Where:** the combined long name, unit and comment field of a worksheet column starts with the tag
byte `0x9A`. Behind the tag either the text follows directly or a metadata block does:

```
9A 00                           <outer> <inner> <UTF-8 text + NUL> 0A 00
9A <L> <L bytes of metadata> 0A <outer> <inner> <UTF-8 text + NUL> 0A 00
```

`L` is the length of the metadata block. For the built in formats that block is the single byte `00`,
which gives the previously known prefix `9A 01 00 0A`; the two byte form `9A 00` was only ever seen
with the three bytes `84 01 08` directly in front of the field. A longer block appears in
`ValueTypeSpecificationForTime\OneBookOneSheetOneTimeColumn_Custom1.opju`, whose column uses the user
defined time specification `0x0211` and whose block is 34 bytes:

```
9A 22 01 0B "SYSTEM" 03 00 58 01 98 02 03 0E "hh:mm:ss;### T" 00 00 00 00 0A 17 16 "TimeColumn..."
   \------------------------- metadata block ------------------/  ^ text as before
```

The user defined format string therefore lives in this metadata block and not in the designation
descriptor, which only carries `02 11`. The framing of the sub records inside the block is not decoded
yet, so the parser recovers the long name, unit and comment but not the format string.

**Parser:** `ApplyWorksheetColumnLongnames` determines the prefix length while scanning for the tag
byte and passes it to `TryReadWorksheetColumnLongname`, which reads the text behind the block. As
before, the field is applied only when one well formed field was recovered per dataset.

---

## Column width: short layout format code `0x20`

**Where:** after the designation records. Display widths are stored little-endian in tenths of a
column unit in a `0x8d` record. Three layouts occur:

1. **short:** `8F 01 <fmt>` + `8D 01|02 <width>` + six `BF` filler bytes
2. **standard:** `89 01 01` + `82 xx` + `8D 01|02 <width>`
3. **combined:** seen in empty numeric columns, where the scanner merges the count with the
   preceding descriptor (see the `combinedLayout` condition).

The format byte of the short layout is `0x60` in most workbooks but `0x20` in others (corpus:
`8F 01 20 8D 1` at 99 sites, `8F 01 60 8D 1` at 26 sites). Some workbooks contain *both* patterns,
where the `0x20` occurrences belong to the long/standard layout instead — accepting `0x20`
unconditionally double-counts those columns, `widths.Count != columns.Count` and no width is applied
at all.

**Parser:** `ApplyWorksheetColumnWidths` collects two lists in one pass — a strict list (`0x60`
only, exactly the previous behaviour, consuming the record pair) and a relaxed list that additionally
accepts `0x20` without consuming the pair. The strict list is used when its length matches the
number of columns, otherwise the relaxed list is used if it matches; if neither matches, no widths
are applied.

---

## Time column values

Time values are stored as day fractions, like all numeric column values, in the block introduced by
the `9B 18` / `9D 18` envelope. The envelope payload holds the block length as a little-endian 64 bit
value at payload offset 8; the block itself starts with `0A 05`, followed by a varint row count and a
varint run control.

The run control encodes the number of values (`control / 2` for an even control,
`control / 2 + 1` for an odd one). An odd control introduces a delta (FCM) run marked by `0x0C`,
decoded by `TryDecodeDoubleRows`. `0x65` as a run control means "the rest is empty / NaN".

Displayed time format `HH:MM:SS.mmm` is *not* limited to one day: `99:00:00.123` is 99 hours, i.e.
`(99 * 3600 + 0.123) / 86400 = 4.125001423611111` as a day fraction. `23:59:59.999` is
`(86400 - 0.001) / 86400`, `25:00:00.000` is `1 + 1/24`.

## Date column values and precision

The `DateContent` fixtures use the same lossless double encoding as numeric and time columns.
Their date scale is `2440587 + (date - new DateTime(1970, 1, 1)).TotalDays`, with integer values
at midnight (half a day below conventional astronomical Julian dates).

In `OneBookOneSheetOneDateColumn_Row0.opju`, the first FCM prediction is zero and the eight
literal bytes `CE A0 C8 7B 58 C7 42 41` directly represent the IEEE 754 double
`2461360.967060185`, matching the documented `16.11.2026 23:12:34`. The test expectation
`2461360.96706019` is rounded, differing by about 0.443 milliseconds.

Similarly, `OneBookOneSheetOneDateColumn_Row0To8.opju` starts with literal bytes
`46 0D 7F 53 F1 9F 42 41`, representing `2441186.6523148147` for
`23.08.1971 15:39:20`. The test expectation `2441186.6523148` differs by about
1.287 milliseconds. These differences are already present in the expected decimal literals,
not introduced by decompression. For precise regression checks, derive expected values from
the documented calendar dates rather than copying rounded decimal day counts.

---

## Parent records (unnamed records that contain child records)

**Where:** one in front of every worksheet dataset and one in front of every column format description.
In `SysVariations/ColumnComment/OneBookOneSheetFiveColumns_A_B_C_D_E.opju` the description of the first
column dataset starts at `0xE5` with `80 33`, i.e. tag `0x80` and length byte `0x33` (51), and the five
column descriptions of that file start at `0xE5`, `0x15A`, `0x1DB`, `0x24F` and `0x2D1`; the five column
format descriptions start at `0xAAC`, `0xB0E`, `0xB6D`, `0xBCE` and `0xC31`.

**Shape:** `<tag> <length> <sub-header> <child records>`. The first byte of the content is the byte count of
the sub-header, so `03 00 00 01` is the three byte sub-header `00 00 01`, `01 10` is the one byte sub-header
`10`, and the record at `0x58C` starts with `0A 00 00 23 C9 02 3D 00 2D 03 A1`, i.e. a ten byte sub-header.
Behind the sub-header the child records follow directly; byte runs between two children, which carry e.g. the
doubles of a column description, are kept as gaps.

```
80 33 | 03 00 00 01 | 80 01 FA | 80 03 A0 0F 01 | ... | 8F 04 C8 10 48 03 | 9B 18 ...
^  ^    ^     ^      ----------------------------------^                  ^
|  |    |     +-- sub-header bytes                   content end 0x119     the record
|  |    +-------- sub-header byte count (three)                            behind the
|  +------------- length byte, one byte more than the content              parent
+---------------- tag 0x80..0x9F
```

**Important — the length byte counts one byte too many:** the content of an unnamed parent record ends at
`offset + 1 + length`, not at `offset + 2 + length` as it does for a plain candidate record. For `80 33` at
`0xE5` the content is 50 bytes wide and ends at `0x119`, and the byte at `0x119` is already the tag of the
value block envelope `9B 18`. The named form, the window description, is the exception: its length byte
counts its content exactly, so `80 77` at `0x362` ends at `0x3DB`.

**Decisive evidence:** a byte pair in the `0x80`..`0x9F` range with a plausible length is only a candidate,
so the child run decides. A record is accepted as a parent when the run closes the content exactly: the first
child starts immediately behind the sub-header, every child stays inside the content, and the last child ends
exactly at the content end. A candidate whose content would end in the middle of a byte run is rejected and
stays an unknown region.

**Corpus:** the rule holds for every file of the fixture corpus. The parser recognizes 1404 parent records in
the 212 fixture files, all of them with tag `0x80`, every one closed by a child record, and the record list of
every file stays an exact partition of the body. The counts behave as expected: a file of the `NumberOfColumns`
folder with *n* columns holds *2n + 2* parents, i.e. two parents per column, and the file above holds twelve of
them for its five columns. Two of those twelve, at `0x58C` and `0x974`, were not listed in the hand written
notes but fit the same framing; their sub-headers start with `0A 00 00 23 …` and `03 00 00 06` instead of the
`03 00 00 01` of a dataset description or the `01 10` of a format description. Graph object descriptions use the sub-header `01 11`, e.g. the twelve
records from `0xA56` of `SysVariations/NumberOfWindows/OneGraph.opju`; before parent records were recognized
those were read as a single plain `TLV` record whose payload held the whole graph object. A few
parent-shaped candidates are still rejected because their content does not close with a child record, e.g.
`80 2C 03 00 00 23 …` at `0x87D` and `80 39 0A 00 00 23 …` at `0x8D4` of
`SysVariations/ColumnWidth/OneBookOneSheetTwoColumns.opju`; their content is not yet understood.

**Length limit:** no parent of the corpus declares more than `0x7F` bytes, and the two candidates that do are
false positives. The record `81 80 01 30 …` at `0x7CB` of `SysVariations/NumberOfWindows/OneGraph.opju` carries
a tag that no parent carries anywhere else, and the long child record that keeps its child run together starts
inside float data: the bytes `00 00 80 3F`, the float 1.0, read as the record `80 3F` with 63 payload bytes.
That is why `MaxParentLength` stays at `0x7F`.

**Parser:** `TryReadParentRecord` accepts a tag `0x80`..`0x9F` with a length of `4`..`0x7F` whose payload is
`<sub-header length> <sub-header>` followed by a child run that closes the content. The named form is left to
`TryReadWindowRecord` (see the next section), because a window description counts its content exactly.
`ScanRecordStream` tries the parent rule *before* the plain candidate rule, and the start of a parent is also
a resynchronization point for gap loops, so a parent is never swallowed by a plain record that merely starts
inside its content. `ParentRecord` occupies only the header and the sub-header while `ContentEnd` marks the
true end: the children stay entries of the flat `Records` list, which keeps that list an exact partition of
the body. Passes that reconstruct bytes across a parent header read the tag, the length byte and the
sub-header bytes through `TryCopyRecordBytes`.

---

## Window description records (records that contain child records)

**Where:** one record per top-level window. In `SysVariations/NumberOfWindows/OneEmptyWorksheet.opju`
the worksheet window `Biro` starts at `0xEC` with `80 76`, i.e. tag `0x80` and length `0x76` (118).

A window description is the *named* form of the parent record of the previous section: its content starts with
a name field instead of a sub-header length byte, and its length byte counts the content exactly, so the record
ends at `offset + 2 + length` and not at `offset + 1 + length`.

**Shape:** the payload is a name field, a run of child `TLV` records and a short trailing area:

```
80 76 | 06 00 00 42 69 72 6F | 92 0C ... 81 08 ... | C1 FF 05 3C 03 00 00 11 80 01 FC ...
^  ^    ^  --  --  --- name ---  -- 8 child records --  ^ trailing area, starts with a
|  |    |  |     |                                        non-TLV byte
|  |    |  |     +-- the name bytes (4 = field length - 2)
|  |    |  +-------- two zero bytes
|  |    +---------- field length, counting its own two zero bytes
|  +--------------- payload length 0x76, above the limit of the plain candidate scan
+------------------ tag 0x80..0x9F
```

The child run ends at the first byte that is not a candidate tag. Every window of the
`NumberOfWindows` fixture folder — worksheet, matrix and graph alike — has eight child records and a
declared length of `114 + length of the window name` (`80 76` for `Biro`, `80 77` for `Quark`, `80 78`
for `Annika`), so the length byte is always above `0x60`.

**Important:** the plain candidate scan rejects lengths above `MaxTLVLength` (`0x60`), so these
records used to be swallowed into an unknown region and the window name was invisible. Because only
lengths that the plain scan rejects can be window descriptions, recognizing them cannot change any
record that was parsed before.

**Parser:** `TryReadWindowRecord` accepts a tag `0x80`..`0x9F` with length `0x61`..`0x7F` whose
payload is `<field length> 00 00 <printable ASCII name>`, followed by at least four child records and
1..32 trailing bytes. `ParseIntoRecords` calls it when the plain `TLV` attempt fails, and the start of
a window record is a resynchronization point for gap loops. The parent rule of the previous section is tried
before it, but cannot take a window description away, because that rule rejects a payload that starts with a
name field. The child records are *nested*: they are available through `WindowRecord.Children`, not as
top-level entries of `Records`, so passes that scan `Records` cannot see them.

**Window types:** a window candidate is a matrix when its name matches exactly one matrix dataset
candidate, a graph when its description holds the application marker in the graph form (see the graph
section below), and a worksheet in every other case. A notes window is not a window candidate at all,
because its description carries no name field (see the notes section below). `ApplyMatrixAndGraphWindows`
and `ApplyNoteWindows` model the windows that are not worksheets, and `ApplyWindowFolders` gives all of
them the folder path of their project tree entry.

---

## Long name and comment of a window

**Where:** the marker `80 01 FC C0 FF 02 00 39 8C 01 48 8B 04 FF 7F 00 00` sits in the trailing area
of the window description, *inside* the declared length (`0x154` for `Biro`). The length byte that
follows the marker is the first byte *behind* the record (`2D` at `0x164`), so the text block
straddles the record boundary.

**Shape:** `<length> <long name>\r\n<comment>` terminated by `00 0A`. For `Biro`, `2D` = 45 bytes
hold `Ächt\r\nThis is the first window, a worksheet\0`. A window without a comment has no line break.

**Parser:** `ApplyWorksheetBookMetadata` searches the area of a window (up to the next window
candidate or the next indexed preview image) for that marker. A book receives the metadata only when
its name occurs exactly once among the books and once among the windows, and when the area contains
exactly one decodable long name.

**General form:** the long marker above ends with the six bytes `8B 04 FF 7F 00 00`, and those six bytes
are the *end of the window description itself* — the trailing block always closes with a record
`<0x8B|0x9B> 04 FF 7F 00 00`. Of the 253 window descriptions of the corpus, 224 end that way (221 with
tag `0x8B`, 3 with tag `0x9B`), and 220 of them are followed by a decodable long name block. The 33
descriptions that match neither rule belong to windows without a long name (e.g. `Book1` of
`OneWorksheet.opju`, `MBook1` of `Matrix137x179.opju`). The long name of a matrix or graph window is
therefore read from the block directly behind its description, behind the closing record of the trailing
block; the marker in front of it is not the same for every window type (`8C 01 48 8B 04 FF 7F 00 00` for
a worksheet, `9B 04 FF 7F 00 00` for the matrix `Aachen`, `8B 01 60 8B 04 FF 7F 00 00` for the graph
`Valencia`), which is why only the six closing bytes and the block behind the record are matched there.

---

## Sheet name marker of a sheet without columns

**Where:** in the sheet description of a worksheet. A sheet *with* columns announces its column count
in `89 01 <columns>` followed by `80 01 F7`; a sheet *without* columns merges both records into the
single record `8D 01 F7`. Both markers are directly followed by the sheet name block.

**Shape of the name block:** `C0 FF` plus an `8E` record plus a record holding the name, closed by
`0A 00 10 01`. Two framings occur (of 227 raw marker sites in the corpus, 225 use the usual framing,
one uses the short framing, and one is a coincidental byte sequence inside a graph window):

```
usual:  C0 FF 8E 04 FF 01 00 04 A4 <len> <name> 0A 00 10 01     (226 sites)
short:  C0 FF 8E 02 FF 01          A6 <len> <name> 0A 00 10 01  (Quark in
                                                       OneEmptyWorksheet_OneMatrixOneWorksheet.opju)
```

**Parser:** `TryReadWorksheetSheetName` accepts both sheet markers and parses the framing instead of
matching nine fixed bytes: the `8E` record length selects the tag byte of the name record, and the
name record tag may be `0xA4` or `0xA6`. `AddWindowOnlyBooks` creates a book for a window only when
the window area holds a complete, numbered sequence of such sheet names whose column count is zero,
which is the only way a column-less worksheet becomes visible at all: books are otherwise derived
from the dataset names (`Book1_A`) of their columns.

---

## Project tree: folder entries and their file entries

**Where:** the project explorer tree block starts after the last indexed preview image, introduced by
the marker `80 06 04 DE 36 10 03 89` with a preceding little-endian `uint32` native length. It is the
only place that stores the folder of a window.

In a project that holds no preview image at all, the block is searched for from the `PrvwOPJU` marker
record on, because there is no last image to start behind. In `NumberOfWindows\OneNote.opju` the marker
record ends at `0x53` and the tree block starts at `0x21F` with the native length 279, in
`TwoNotes.opju` it starts at `0x31F` with the native length 427; both files hold only notes windows.

**Shape of a folder entry:** `80 <header length>` `TLV` whose payload begins with a variable type
prefix (`01 04 8C 10`, `03 00 00 01 8A 10` or `8D 10`) followed by the parent GUID and the folder's
own GUID; then `00`, `uint32` name length, `0A`, the NUL-terminated name, the folder marker
`0A 04 07 01 47 C0 11 01 01 9C 0A`, a `<properties length>` block that must be followed by `0A`, and
the entries marker `04 A1 01 64 80 0A 02 75 62 0A`.

**File entries:** the byte behind the entries marker is `fileCount / 2`, then `fileCount` entries,
then `folderCount / 2` and the sub folders. Every entry has the same framing — a zero byte, one value
record, a terminating zero byte — while the value differs:

| Value record | Meaning |
|---|---|
| `80 01 85` | The window with ordinal 0 (short form). |
| `80 04 81 01 <ordinal> 80` | The window with the given ordinal. |
| `80 05 03 00 00 10 82` | A *notes* window. Behind the first byte is the 24 bit little-endian value `00 00 10`, i.e. the file type `0x100000` that the legacy `.opj` project tree uses for notes windows; the leading byte (here `03`) is an object id whose meaning is not established. |
| `80 07 05 00 00 10 00 01 80` | A notes window of the same form with two additional bytes (here the id is `05`). |

The ordinals refer to the window candidates of `FindWindowCandidates` in file order, and — because a
notes window lies between them in the file — to the notes windows as well. Corpus statistics over the
212 files whose tree parses: 210 entries of the short form, 15 entries with an explicit ordinal
(`81 01 01 80` 9x, `81 01 02 80` 5x, `81 01 03 80` 1x) and 5 notes entries; no other form occurs.

A notes entry contributes no ordinal of its own. The ordinal is recovered by elimination: the ordinals
that no other file entry uses are exactly the positions the notes windows have in the file order of all
windows, and the tree is only accepted when both sets are equal (see the notes section below).

**Notes entries and notes windows correspond one to one:** the corpus holds 5 notes entries in 4 files
and exactly 5 notes windows, and the files are the same. `NumberOfWindows\OneEmptyWorksheet_OneMatrix.opju`
shows what happens to the tree entry when a notes window is deleted: the `_README.txt` of that fixture
reports the deleted notes window `Lenja`, and neither the window nor a notes entry is left - the tree of
that file holds only the two entries `81 01 01 80` in `Mömmern\Döra` and `85` in `München`, and no byte of
the name `Lenja`, the long name `Kassel` or the note text occurs anywhere in the file, neither in UTF-8
nor in UTF-16. A notes entry can therefore be read as evidence that a notes window exists.

**Parser:** `TryReadFolder` parses the generic entry framing, so an entry of an unknown form is skipped
rather than rejecting the whole tree; only the entries of a known form contribute an ordinal or a notes
path. `TryReadProjectTree` locates the block and accepts it only when it parses without leftovers and
lists every window exactly once - a notes entry counts as a listed window only when a notes window was
actually recognized, and the tree is rejected as a whole when the free ordinals are not exactly the
positions of the notes windows. As a safety net for a notes window that escapes recognition, the block is
then read a second time with the notes entries ignored, which keeps the folders of the other windows
readable; no file of the corpus needs that second pass. `ApplyWindowFolders` applies a path to the book,
matrix or graph whose name is unique among the window candidates and among the modeled windows.

---

## Notes windows use a different description

**Where:** a notes window description does not carry its name in a name field. In
`NumberOfWindows\OneNote.opju` the description of `Lenja` starts at `0xFB` with `80 3F` (length 63) and
its payload begins `01 1A 80 01 1A` instead of `<field length> 00 00 <name>`, so `TryReadWindowRecord`
does not recognize it and the description stays inside an unknown region. The name is carried by the
record `93 06 "Lenja\0"` at `0x13B`, whose tag byte is the last byte inside the declared length of the
description — the declared length is one byte too long, comparable to the known `PrvwOPJU` short-length
special case.

**Shape:** behind the name record follows `0A <length> <text> 00 0A`, where the length counts the text
bytes and the terminating NUL. The text holds the long name, the comment and the content of the note:
`Kassel\r\nThis is the note window L_e_n_j_a\nThis is content inside the notes window:\r\nHere a second
line.`. Long name and comment are separated by `\r\n` as in every other window type; the content starts
behind a line feed that is *not* preceded by a carriage return. The name record tag is `0x93` in three
fixtures and `0x96` in `OneEmptyWorksheet_OneMatrixOneWorksheetOneNote.opju`.

**Recognition:** the sequence `<93|96> <len> <printable name> 00 0A <len> <text> 00 0A` was searched for
throughout the corpus: it matches exactly the five notes windows that exist there and nothing else. The
description itself is not used for recognition, because its child run is not stable — the record holding
the modification date is a plain `TLV` in some fixtures (`80 10 <creation> <modification>`, `80 11 ...`)
and starts with the non-tag bytes `C0 55` in others, which cuts the child run short and would make a
child-count rule unusable.

**Window ordinal:** the project tree lists the notes window without an ordinal (see the section above).
The ordinal is recovered by elimination: the ordinals that no other file entry uses must be exactly the
positions the notes windows have in the file order of all windows, and the tree is accepted only when
both sets are equal. In `OneEmptyWorksheet_OneMatrixOneWorksheetOneNote.opju` the explicit entries use
0, 1 and 2 for `Aachen`, `Mira` and `Biro`, leaving 3 for `Lenja`, which is the fourth window in the file.
The notes entries of the tree are paired with the notes windows in the order the tree lists them and the
file holds them; in `TwoNotes.opju` that is `Lenja` before `Lissabon`, which is the file order.

**A notes window leaves its tree entry behind when it is deleted:** the tree of
`OneEmptyWorksheet_OneMatrix.opju` holds no notes entry at all, although the fixture was made by deleting
the notes window `Lenja` (see the project tree section above). The 5 notes entries of the corpus therefore
belong to the 5 notes windows one to one, which is what makes the elimination rule and the pairing by
order reliable.

**Parser:** `FindNoteWindowCandidates` collects the notes windows by the signature above and decodes
name, long name, comment and content; `ApplyNoteWindows` builds a `Note` for every one of them, and
`ApplyWindowFolders` gives them the folder path of their tree entry. Notes windows are not added to
`FindWindowCandidates`, because the passes that use the record index of a window candidate (sheet names,
column widths, book metadata) cannot work with a description that is not a parsed record.

---

## Graph windows

**Where:** the child records of the window description. Every window description of the corpus (253 of
them) holds a child that carries the ASCII text `ORIGIN`, the name of the application that wrote the
project.

**Shape:** for a worksheet or a matrix that child is
`83 0D 05 00 04 00 02 11 11 4F 52 49 47 49 4E` — tag `0x83`, length `0x0D`, the text behind a seven byte
prefix. For a graph it is `81 06 4F 52 49 47 49 4E` — tag `0x81`, length 6, the text alone. 251 of the
253 descriptions use the first form; the two that use the second are the graph windows `Valencia` in
`NumberOfWindows\OneGraph.opju` and `NumberOfWindows\OneEmptyWorksheet_OneGraph.opju`. A graph
description differs in two further children: its third child is `82 04 20 19 3B 13` where a worksheet or
matrix has `81 01 20`, and the record that holds the two dates is `85 11 <creation> <modification>`
where the others have `86 10 <creation> <modification>`.

**Parser:** `IsGraphWindowRecord` requires the `ORIGIN` child to have tag `0x81` and payload exactly
`ORIGIN`, and rejects the description when any child of tag `0x83` ends with `ORIGIN`. The first child
tag of a description cannot be used for this: it ranges over `0x89`..`0x95` for worksheets and matrices
and is `0x8E` for the graph, but `0x8E` also occurs for the matrix `MComplex`.

---

## Matrix windows and the name of a matrix sheet

**Where:** a matrix window is announced by its matrix dataset — the name record that is followed by the
matrix type marker `90 02 CA 50` or `CA 51` (see `FindMatrixDatasetCandidates`). The tag of that name
record is `0x95`, `0x96` or `0x97`; `0x96` was not accepted before and occurs twice in the corpus, both
times for the matrix `Aachen` of the `NumberOfWindows` fixtures.

**Shape of a matrix sheet description:** the sheet of the three `NumberOfWindows` matrices ends with

```
81 01 06 | 90 08 <8 bytes> | 89 01 25 | 80 01 F7 | C0 FF 8E 04 FF 01 00 04 A4 09 <name> 0A 00 10 01
```

which is the sheet-with-columns signature of a worksheet (`TryReadWorksheetSheetName`) with the ordinal
marker `84 01 07` replaced by `81 01 06` and the `88 08 <8 bytes>` record replaced by
`90 08 <8 bytes>`. The name block framing is identical. The signature occurs three times in the corpus,
in the three matrix fixtures of `NumberOfWindows`, and always names the sheet `Maßricht`; the value `25`
behind `89 01` is not interpreted. The matrix sheets of the other matrix fixtures (`MBook1`, `MReal`,
...) are not described this way — their names are only visible through the `94 FF FF 01 00` sheet-name
signature — so a matrix window without such a sheet description keeps an empty sheet list.

**Parser:** `TryReadMatrixSheetName` reads the signature above from the records of the window area of a
matrix window; `ApplyMatrixAndGraphWindows` creates a `Matrix` for a window whose name matches exactly one
matrix dataset candidate, and gives it the sheets found in its window area. A matrix window is not turned
into a `Book`, because it has no columns: `AddWindowOnlyBooks` only accepts the sheet-without-columns
signature of a worksheet, which a matrix sheet description does not match.

---

## The preview image table pointer can be null

**Where:** the payload of the `PrvwOPJU` record, behind the eight marker bytes.

**Shape:** the marker record is `81 0A` followed by `PrvwOPJU` and an 8-byte little-endian offset of the
preview image table, so its declared length `0x0A` is six bytes short of the 16-byte payload. In
`OneNote.opju` and `TwoNotes.opju` those eight bytes are all zero: the project contains no preview image
at all, therefore it has neither an image table nor a single `89 50 4E 47` PNG signature anywhere. Both
files hold only notes windows, which matches the observation that every fixture with a preview image
contains at least one worksheet, matrix or graph window.

**Parser:** `ParseIntoRecords` only calls `FindIndexedPngs` when the pointer is non-zero. A zero pointer is
a missing image table, not a truncated one, so it must not be reported as an invalid table; scanning simply
continues behind the marker record.

---

## End of a text column value block

**Where:** behind the last string of the value block of a text column.

**Shape:** the block ends with `02 01 00 0A`, or with a longer run of zero bytes closed by `0A 00`
directly in front of the window description of the sheet. That window description is a record of its
own, so its bytes must not be read as part of the value block.

**Parser:** `TryDecodeTextDataset` verifies the seven bytes `00 00 00 00 00 0A 00` plus a following
candidate tag (`0x80`..`0x9F`) against the raw project bytes instead of reading them through the
records of the block.

---

## The byte count of a value block is stated by the record in front of it

**Where:** the payload of the record with tag `0x9B` or `0x9D` that sits directly behind the worksheet
dataset descriptor, i.e. the record the parser calls the envelope of the value block.

**Shape:** the payload of that envelope is always 24 bytes. Its first eight bytes are the constant
`18 00 00 00 00 00 00 00`, and the next eight bytes are a little-endian 64-bit count of the encoded
value bytes that follow. In `OneBookOneSheetOneDoubleColumnWith7Numbers.opju` the envelope at `0x119`
announces `41 00 00 00 00 00 00 00`, which is 65: the block starts at `0x134` with the kind byte `05`
and ends at `0x175`, where the byte `02` begins the region behind the block. The eight bytes behind the
count are not the length and their meaning is still unknown.

Between the envelope and the block there is exactly one byte, always `0A`. The block itself starts with
its kind byte `04` or `05`, and the byte directly behind a block of the announced length is `00` or `02`.

The length field is only filled in for the element types that announce their element width in front of
the envelope. The envelopes of text columns and of single precision columns hold a filler instead, so
the blocks of those columns cannot be bounded this way and stay unknown regions.

**Parser:** `ScanRecordStream` consumes the block while it scans. Where `TryReadValueBlockBehindEnvelope`
confirms an announced length — the envelope must sit directly behind a worksheet dataset descriptor, the
separating byte must be `0A`, the first byte of the block must be `04` or `05`, and the byte behind the block
must be `00` or `02` — the scan emits the envelope, a one-byte unknown region for the separating `0A`, and a
single `ValueBlockRecord`, and continues at the end of the block. Doing this during the scan is necessary and
not merely cosmetic: a block of value bytes is full of coincidental candidate records, and a resynchronization
loop inside a block steps over the record behind it, which is the parent description of the next dataset.

`MergeValueBlocks` still runs after the record scan as a safety net for the blocks the scan did not bound. It
applies the same checks (`TryGetValueBlockSpan`) and additionally rejects an announced range that holds an
image, an image table, a window description or the header of another dataset; where it matches, the candidate
records and gaps the scanner reported inside the block are replaced by one `ValueBlockRecord`. That record has
a boundary of its own, so the byte reconstruction in `TryCopyRecordBytes` re-adds no TLV framing for it, and
the scanner no longer invents TLV records out of value bytes: the spurious `TLV Tag=86` that used to start in
the middle of the block above is gone.
