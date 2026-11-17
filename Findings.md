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
