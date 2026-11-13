# OPJU worksheet fixture findings

## Scope

The initial observations compare the double four-value baseline, seven-double and single-precision fixtures, and the `AisY`, `3EqualNumbers`, `59Rows`, `AtRow10`, and `With5EmptyRowsInbetween` variations. The later analysis also compares the empty-column, controlled numeric, repeated-run, sparse, and two-sheet fixtures with their descriptions and record dumps.

Each describes a book with short name `Book1` and label `Ößberg`, a sheet named `Äalesund`, and a column `A`. The baseline has 39 allocated rows and four values: `2.137`, `3.983`, `1.111`, and `7.787`. The seven-value fixture adds `1.23E-14`, `7.465E29`, and `-84578`; the other variations change type, plot kind, allocated rows, values, or their positions.

Offsets below are hexadecimal and relative to the explicitly defined candidate record payload or block start, not to the start of the file. The layouts and interpretations are heuristic unless otherwise stated. The scanner's records are candidates, so neither their boundaries nor the relative offsets are general format guarantees.

## Candidate dataset record sequence

In the four-value double baseline, the scanner reports the following consecutive candidate TLVs before the data. `Content` is the payload, excluding the tag and length bytes:

| Candidate tag | Payload | Observation |
|---|---|---|
| `80` | `FA` | Preceding candidate; its meaning is unknown. |
| `80` | `A0 0F 01` | Preceding candidate; its meaning is unknown. |
| `81` | `10` | Preceding candidate; its meaning is unknown. |
| `81` | `01 50 03 27` | Payload byte `+0x01` is a plot-kind candidate; byte `+0x03` is a row-related candidate. |
| `84` | `04` | Payload byte `+0x00` is a second row-related candidate. |
| `86` | `F0 3F` | Meaning unknown. |
| `83` | `F0 3F` | Meaning unknown. |
| `85` | `08` | Payload byte `+0x00` matches the double element width. |
| `97` | `42 6F 6F 6B 31 5F 41` | Payload at `+0x00` is the dataset name `Book1_A`. |
| `8F` | `C8 10 48 03` | Immediately precedes the candidate envelope; meaning unknown. |
| `9B` | `18 00 00 00 00 00 00 00 27 00 00 00 00 00 00 00 46 D7 F6 A2 F7 60 EC 75` | Candidate envelope, followed by bytes containing the encoded data. |

This is an observed scanner sequence, not a native grammar. In the corresponding float fixture, the `81` payload begins with `03` instead of `01`, the `85` payload is `04`, and the `9B` payload differs. When no values are present, the scanner reports an `8E` candidate after the four-byte `81` payload instead of the `84`/`86` pair. Other dumps show resynchronization swallowing an earlier candidate into a preceding TLV. Do not locate a dataset by tag numbers alone.

### X and Y column designation

In all ten `IndexOfXColumn` fixtures, payload byte `+0x01` of the four-byte candidate `01 50 03 nn` is `50` for the designated X column, and `60` for each Y column. The candidate is reported as tag `81` for the first dataset and `86` for subsequent datasets. It precedes the consecutive `F0 3F` candidates (tags `8E` or `86`, then `83`) and the width candidate `85 01 08`; populated baseline datasets may have an additional one-byte `84` candidate before that pair. The float baseline similarly uses `03 50 03 nn`. The dataset name follows the width, either directly in a `97` candidate or after a one-byte `91` candidate in an `82` candidate. The split-name `91` payload varies from `01` to `09` in these files, and `CA 10` appears in an `8F` candidate before the envelope. These are scanner-dependent patterns, not established native field boundaries.

### Additional column designations

In the `SpreadColumnType` fixtures, a later sequence of per-column metadata candidates provides explicit codes. In these files, a candidate `87` has payload `[type code, ASCII short name]`: `01` Ignore, `02` YErr, `03` X, `04` Label, `05` Z, `06` XErr, `07` Group, and `08` Subject. Y appears instead as candidate `88` with the one-byte column name and no explicit code in that candidate. These candidates follow a short or long column header containing a one-based ordinal and precede an `8D`, `8F`, or `8A` candidate. In particular, Group and Subject both have `60` in the earlier dataset header, so that byte alone does not distinguish them from Y. The parser applies these later designations only if a complete ordered name/ordinal sequence matches all datasets in a single inferred book and sheet; ambiguous ownership and other layouts remain unresolved.

The label fixture's third dataset is `Book1_C` with a split name candidate followed by `8F` payload `E8 10` and the usual 24-byte candidate envelope; the prior parser omitted it because it accepted only `C8 10` or `CA 10`. Its later column metadata identifies it with `87` payload `04 43`. This recognition does not establish how populated label values are encoded.

### Numeric deserialized data types

In the `DeserializedDataType` fixtures, let **D** be the payload of the four-byte candidate `81` or `86` preceding the two `F0 3F` candidates and width candidate `85`. The following table compares D `+0x00`, D `+0x01`, and the `85` payload. All examples have `03` at D `+0x02`; the last byte is row-related. The table shows Y-column codes; the parser also recognizes the corresponding observed X-column header values (`50`, `58`, `52` in place of `60`, `68`, `62`).

| Type | D `+0x00` | D `+0x01` | `85` payload |
|---|---|---|---|
| Double | `01` | `60` | `08` |
| Single | `03` | `60` | `04` |
| Complex | `01` | `62` | `10` |
| Int32 / UInt32 | `01` | `68` | `04` / `04 00 08` |
| Int16 / UInt16 | `03` | `68` | `02` / `02 00 08` |
| SByte / Byte | `21` | `68` | `01` / `01 00 08` |

The extra `00 08` accompanies unsigned types in these fixtures; the scanner then emits the dataset name as a `95` candidate rather than `97`. In a later ordered worksheet metadata sequence, non-double numeric columns also have `8B 01` followed by a storage-type marker: `01` Single, `02` Int16, `03` Int32, `04` SByte, `0A` Complex, `82` UInt16, `83` UInt32, `84` Byte. A candidate `80` with two-byte payload follows; the parser associates the logical Numeric value type only when the complete name and ordinal sequence matches. Type inference is limited to the validated earlier header/width combinations, independently of worksheet metadata ownership; unknown combinations remain unresolved. These observations identify the deserialized type, not the encoding of the actual numeric values.

In the populated `NumericSingleContent` fixtures, the later ordinal header is followed by `81 01 nn`, then `82 02 03 41` for column A, `8B 01 01`, and `80 02 01 50`. The empty fixture instead has `87 02 03 41` without the `81` marker. The populated arrangement is accepted for Numeric ValueType when the marker, Single descriptor, following candidate, ordinal, and name match; its plot designation remains derived as before from the dataset header. Other populated numeric metadata arrangements remain unproven.

### Column value types

In the `ValueType` fixtures, let **N** be the tag byte of the later short-name candidate (`87 02 03 41` for X column A, or `88 01 42` for Y column B). Immediately after the entire name candidate, the scanner reports `88 01` followed by a type byte: `01` Text, `02` Time, `03` Date, `04` Month, `05` Day, and `09` TextNumeric. Binary and Color instead have `88 09` followed by `0E` and `0F`, respectively, and eight further bytes. Numeric has no such `88` candidate: a descriptor candidate `8D`, `8F`, or `8A` follows the name directly. The preceding one-based ordinal and complete ordered names are checked as described above; other codes and ambiguous ownership remain unresolved. These are fixture observations, not proof that every `88` candidate or absent code has this meaning in arbitrary projects.

Text datasets in these fixtures have width candidate `85 01 32` and an `E8 10` descriptor; the first column's descriptor also has `48 03` after it. TextNumeric uses `85 01 0A` with `CA 10`. Binary and Color have a three-byte `85` payload (`01 00 08` and `04 00 08`), followed by an `8F` marker and `82` name; Binary additionally has `8D 02 01 02` before an `8B` envelope, while Color has a `9D` envelope. TextNumeric, Binary and Color values and their data lengths remain undecoded.

### Text content

In the six `TextContent` fixtures, an `85 01 32` candidate and dataset-name candidate precede an `8F` candidate beginning `E8 10` and a 24-byte `9B` or `9D` envelope. Its first eight payload bytes are `18 00 00 00 00 00 00 00`, followed by sixteen `22` bytes. Let **T** be the byte following the `0A` immediately after this envelope. At T `+0x00` is `05`, at T `+0x01` a one-byte candidate dataset extent, and from T `+0x02` each row is represented by a one-byte UTF-8 byte length followed by that many bytes; a zero length represents a blank. For example `07 4D C3 BC 6C 6C 65 72` encodes `Müller`, and `06 C3 96 68 72 69 67` encodes `Öhrig`. The sparse and shifted fixtures contain one zero byte per blank row. After the candidate extent entries, the observed sequences begin either `02 01 00 0A` or nine zero bytes and `0A 00 80 77`; their general meaning is unproven.

The first dataset's extent is 39 in these files, but the second dataset's extent is 30, 66, or 68 even though the worksheet descriptions specify 39 rows. Only the encoded dataset extent is decoded; it is not the sheet row count. The current decoder accepts only the observed short lengths and single-byte extents, validates UTF-8 and the observed trailing signatures, and leaves other layouts unresolved. It preserves the encoded bytes separately from the decoded strings. Scanner boundaries can fall inside the text block.

For populated text columns, the later column metadata differs from the empty case: after the ordinal header, a candidate `81 01 nn` precedes `82 02 03 41` for A or `83 01 42` for B; the following `88 01 01` identifies Text as above. The meaning of `nn` is not established; complete names and ordinals must match.

For the listed four- and seven-value examples, the `81` payload byte `+0x03` is `27` (39), and the `84` payload byte `+0x00` is `04` or `07`. In the sparse fixtures the latter is `0E` or `09`, despite only four populated values, so it is not a populated-value count. In the 59-row fixture it is `3B`; it is not universally just the last populated row index plus one either. Both fields' general meanings remain unproven.

## Candidate data envelope and block anchor

For the examples below, let **H** be the first payload byte of the candidate envelope reported as `9B` in the baseline sequence. Let **B** be the first byte after the envelope's 24-byte payload and the following `0A` byte: in these examples, `B = H + 0x19`. This is a fixture-specific anchor, not a proven native block boundary. In the two-sheet fixture, similar envelopes are reported as `9D`, not `9B`.

| Position relative to H | Observed bytes or interpretation |
|---|---|
| `+0x00` | Eight bytes `18 00 00 00 00 00 00 00` in the listed examples; their meaning is unknown. |
| `+0x08` | Eight bytes interpreted as a little-endian candidate encoded-length value for the double examples. |
| `+0x10` | Eight further payload bytes; their role is unknown. In the empty-column fixture these bytes contain two copies of the four-byte sequence `05 27 4E 65`. |
| `+0x18` | Byte `0A` after the candidate envelope's payload in the listed examples. |
| `+0x19` | B: first byte of the data-like sequence following that `0A`. |

The candidate TLV `8F` immediately precedes the `9B` envelope in the baseline, but the two-sheet examples instead have `8D` immediately before `9D`. Name length and scanner segmentation affect the surrounding records; these exact tags and the `B = H + 0x19` relationship must be validated in context, not applied as general rules.

### Row and value-block variations

| Double fixture | First row candidate | Second row candidate | UInt64 at H `+0x08` | Bytes beginning at B |
|---|---:|---:|---:|---|
| Baseline | `27` | `04` | `27` | `05 27 07 0C E7 ... 46 65` |
| Three equal values | `27` | `04` | `17` | `05 27 07 0C 87 ... 46 65` |
| 59 rows | `3B` | `3B` | `27` | `05 3B 07 0C E7 ... 6E 65` |
| Values at rows 10–13 | `27` | `0E` | `29` | `05 27 14 65 07 0C E7 ... 32 65` |
| Five internal blank rows | `27` | `09` | `3D` | `05 27 05 0C E7 ... 08 65 ... 3A 65` |
| Seven values | `27` | `07` | `41` | `05 27 0D 0C E7 ... D6 86 65` |
| Y plot kind | `27` | `04` | `27` | Same value block as baseline |

The first row candidate and byte B `+0x01` track 39 (`0x27`) versus 59 (`0x3B`) worksheet rows in these samples, but are not established as general worksheet row counts.

Four additional row-only copies give B prefixes `05 28`, `05 29`, `05 64`, and `05 80 02` for 40, 41, 100, and 256 rows. After the same four-value sequence, their trailing bytes are respectively `48 65`, `4A 65`, `C0 01 65`, and `F8 03 65`. These agree with unsigned base-128 variable-length integers for the dataset extent and twice the trailing blank count. The two-sheet example still shows that dataset extent is not necessarily the worksheet row count. The additional fixtures were found only in the build output directory, not the source fixture directory.

For the six four-value double variations in the table, the little-endian value at H `+0x08` equals the byte count from B through the apparent final `65`, inclusive. The seven-value fixture is an exception: the value is `0x41`, while its apparent final `65` is at B `+0x3F`, giving `0x40` bytes through that byte; the following byte is `02`. Thus the field is a length candidate, but its exact endpoints or semantics are not established. The float fixture instead has repeated `22` bytes at H `+0x08`, not a usable 64-bit length under this interpretation.

## Numeric values

In the float fixture, the four values are consecutive little-endian IEEE 754 single-precision numbers beginning at B `+0x03`, after `05 27 0D`.

### Controlled Single variations

In the Single-content fixtures, the dataset width candidate `85 01 04` follows a four-byte type header and two `F0 3F` records. A one-byte `84` candidate between the type header and those records equals the last populated row index plus one; it is absent in the empty fixture. The candidate envelope at H contains sixteen `22` bytes at H `+0x08`, rather than the double length candidate. B remains H `+0x19`.

At B, `05` is followed by an unsigned base-128 dataset extent. The observed value blocks then use unsigned base-128 controls: an odd control `2*n-1` precedes `n` literal little-endian IEEE 754 Single values, and an even control `2*n` precedes one Single value repeated `n` times. For example, `0D` precedes seven literals in the four-value fixture, while `10 00 00 80 3F` encodes eight ones in the two-runs-of-eight fixture. The runs total the dataset extent and are followed by `02 01 00 0A` in these examples.

The recorded extent includes zero padding beyond the `84` candidate. The controlled zero-transition and leading/intermediate-zero fixtures show that zeros within the populated extent are numeric values, including some encoded as negative zero; therefore the padding cannot be classified by value alone. The decoder uses this row candidate only for the observed header layout and requires zero bits for all padding. The candidate's general meaning, larger row markers, other trailers, and non-finite Single values remain unproven.

### Controlled TextNumeric variations

In the all-comments-set fixture, column A's dataset-header designation is not recovered by the scanner, but its validated worksheet name candidate `82 02 03 41` still identifies A as X. When the initial dataset-header inference returns Ignore, worksheet metadata supplies the designation even for populated numeric columns; recognized numeric dataset designations retain their existing precedence.

The populated five-column fixtures retain worksheet designation codes in the name candidates: `82 02 05 42` identifies B as Z, `83 01 43` identifies C as Y, and `82 02 02 44` identifies D as YErr. After validating the complete ordered worksheet metadata list, these designations apply to TextNumeric columns too; retaining the dataset-header fallback instead incorrectly leaves them as Ignore. The dedicated SpreadColumnType fixtures contain only empty Double columns and do not exercise this populated layout.

In the populated five-column `ColumnLongname`, `ColumnUnit`, and `ColumnComment` fixtures, column D (TextNumeric, YErr) has a worksheet metadata candidate `88` with payload `09 00 03 00 00 03 00 30 61`, rather than the one-byte `09` payload of columns B and C. The parser recognizes this complete observed sequence as TextNumeric in both header validation and type classification. Rejecting this candidate previously prevented assignment of every column's ValueType because the complete ordered metadata list is required. The dedicated ValueType variations contain empty columns and do not exercise this populated layout.

The numeric-only fixtures have a type header `21 51 03 27`, two `F0 3F` records, width candidate `85 02 0A 16`, name candidate `96`, and descriptor `8F C8 10 ...` before the `9B` envelope. Fixtures with isolated blanks use `85 01 0A`, name candidate `97`, and a `9B` envelope; the string-bearing examples insert `82 01 02` before an envelope reported as `95`. In the populated worksheet metadata, `82 02 03 41` is followed by `88 01 09` and `83 02 21 51`. These are scanner candidates, not proven native TLV boundaries.

At B, `05` (numeric examples) or `04` (string examples) precedes the base-128 row extent. Segments beginning `FF FF` contain a row count and a type byte. Type `00` uses the Double run/literal encoding described below. Type `01` can contain a blank-looking run such as `0A 01 14 00` for ten visually blank rows, or the observed string-reference sequence `04 01 07 01 09 0F 18`. In the latter, the offsets 1, 9, 15, and 24 point into the NUL-terminated UTF-8 bytes of the immediately following `_MSER_STRINGS_PSET` structure. The current decoder only accepts the observed four-string pool framing and leaves other variants undecoded.

The fixtures establish only that the indicated rows appear blank; they do not prove whether the underlying cell type is numeric, null, or text. The partial model uses an empty `Variant` for these visually blank encodings and retains candidate bytes separately; this is not a claim about their source representation. In the numeric sub-block, the existing sentinel convention also produces an empty `Variant` for TextNumeric rather than asserting the original cell type.

Double values are not uniformly stored as a contiguous array. Three observed transitions support XOR-based compression:

- In the four-value baseline, B begins `05 27 07 0C E7`; the first value, `2.137`, is an ordinary little-endian double at B `+0x05`. The seven bytes at B `+0x0D` equal the low seven bytes of `bits(2.137) XOR bits(3.983)`; the eighth XOR byte is zero. Reconstructing that byte yields `3.983`.
- In the seven-value fixture, the eight bytes at B `+0x37` equal `bits(previous stored value) XOR bits(-84578)`. The scanner starts a candidate TLV inside those eight bytes, so reading only one unknown-region payload would truncate this sequence.
- In the three-equal-values fixture, B `+0x05` contains `2.137`, followed at B `+0x0D` by `68` and at B `+0x0E` by `A6 A4 4B 27 70 3D 1E`. These are the low seven bytes of `bits(2.137) XOR bits(7.787)`, whose high XOR byte is zero. The block is 16 bytes shorter than the baseline, consistent with compact representation of the repeated values, but the control byte and run-length rules remain unproven.

Other values appear directly as doubles, and intervening bytes have control semantics that are not established. The stored value near `7.465E29` differs from the nearest double to that decimal by one representable step. A decoder must not assume that every double is raw or that all XOR transitions use the same byte count.

### Controlled double variations

- The observed block controls after `05` and the base-128 row extent follow the Single-style count convention: odd `2*n-1` starts `n` literals, even `2*n` starts a run of `n` values. Literal blocks begin with `0C` and pack two low-nibble-first codes per control byte; each literal's residual bytes follow in order before the next control byte. The low three code bits specify the residual byte count (0, 1, 2, 3, 5, 6, 7, or 8), and the high bit selects a differential rather than value-context predictor.
- A pair of zero-initialized 4096-entry context tables and 12-bit hashes reproduces the controlled literals: XOR the little-endian residual with either the value table entry or the previous bit pattern plus the differential table entry, then update the value hash with `(hash << 6) XOR (bits >> 48)` and the differential hash with `(hash << 2) XOR (delta >> 40)`. Table indices are masked to 12 bits. Each observed literal block starts with fresh state. These hash parameters and reset boundaries are a working hypothesis; the short fixtures do not establish table size or all collision behavior.
- The new `1_3` fixture begins at B `+0x02` with `05 0C F7`, followed by the raw bits of `1.0` and residual `00 00 00 00 00 00 E8 3F`; the differential prediction gives `3.0`. The `2_1` fixture has `05 0C 77`, with `00 00 00 00 00 00 F0 7F` in the second payload position; the initial value-context prediction is zero. The `2_2_3` fixture starts `05 0C 07`, then the raw `2.0` bits and `06 00 00 00 00 00 00 08`, matching a value-context prediction of `2.0` for the final literal.
- In `TwoRepeatedRuns`, the second pair is `E7` followed by the raw bytes of `2.0` and seven bytes `00 00 00 00 00 00 10`. The differential predictor gives `4.0` at that position, and XORing its bits with the residual produces the described `2.0`; XOR against the preceding value alone would incorrectly produce `4.0`.
- In `ThreeOneThenChange`, the values are `1.0, 1.0, 1.0, 1.5`. The bytes `68 00 00 00 00 00 00 08` begin at B `+0x15`, after the first two apparent literal values. The seven bytes after `68` are the low seven bytes of `bits(1.0) XOR bits(1.5)`, with a zero high byte. This independently supports the XOR hypothesis, but does not establish the full meaning of `68` or how the repeated value is framed.
- In `DifferentExponent`, the values are `1.0, 2.0`. After the literal `1.0`, the next eight bytes at B `+0x0D` are `00 00 00 00 00 00 E0 3F`. They are neither the IEEE 754 representation of `2.0` nor `bits(1.0) XOR bits(2.0)`. With `0C F7`, the differential-context prediction is the preceding bit pattern plus the preceding delta, and XOR with the residual gives `2.0`.
- In `FourOne`, B `+0x02` begins `08 1A F0 3F` for four consecutive `1.0` values. At the same relative position in `TwoRepeatedRuns8` and `TwoRepeatedRuns16`, the corresponding sequences start with `10 1A F0 3F` and `20 1A F0 3F` for runs of eight and sixteen. These examples are consistent with a count equal to twice the run length, but do not establish the meaning of `1A`, the complete encoding, or its limits. At B `+0x06`, the same fixtures contain `10 11 40` and `20 11 40` for the following runs of `2.0`.
- After the four-value baseline sequence, the seven-one fixture has `0E 1A F0 3F 38 65`: `0E` matches twice seven and `38` matches twice the 28 remaining blanks. The file's name uses `SevenOnes` while its description uses `7Ones`. The now-readable 17-one fixture has `22 1A F0 3F 24 65` at B `+0x25`: `22` matches twice 17 and `24` matches twice the 18 remaining blanks. Its preceding candidate envelope is `9B`, with a candidate length at H `+0x08` of `2B` (43 bytes); the actual fixture confirms the previously constructed test for this particular run framing, not the general encoding rules.

### Blank rows

In the internal-blank fixture, the eight bytes `0E 2C 13 1C FE 74 AA 81` occur at B `+0x15` and B `+0x33`. As a little-endian double they are `-1.23456789E-300`, which the existing `Variant` implementation in `OriginObjects.cs` converts to `double.NaN`. This strongly suggests a missing-value sentinel in the encoded sequence, but its run framing and whether the same sentinel applies to every data type remain unverified.

The leading-blank fixture inserts `14 65` at B `+0x02` before the baseline's value sequence, consistent with a compact representation of ten initial blank rows. The baseline ends with `46 65` after four values, consistent with 35 trailing blanks; the entirely empty 39-row fixture contains `4E 65`. These cases support a candidate blank-run count of twice the number of rows. The internal-blank fixture instead contains an explicit sentinel pattern and different control bytes. The general framing and run limits remain unproven.

The 59-row fixture's bytes from B `+0x02` through B `+0x24` are identical to the baseline's four-value sequence. This supports the stated row-count-only change, but does not independently prove the row positions. The corrected description places the values at rows 0–3.

## Names and record boundaries

The book label `Ößberg` and sheet name `Äalesund` appear in UTF-8. The latter is preceded by byte length `09`, its UTF-8 byte length rather than its character count. The current printable-ASCII candidate logic cannot recover these names.

### Column Longname

Across the six `ColumnLongname` fixtures, five field markers appear in worksheet column order. A populated field begins with `9A 01 00 0A`, then an outer byte count, an inner UTF-8 byte count that includes the trailing NUL, the string bytes, and the `0A 00` terminator. For example, `Überdruck` is encoded as `0C 0B C3 9C 62 65 72 64 72 75 63 6B 00 0A 00`. An explicit empty field is `02 01 00 0A 00`; columns without a Longname also show the marker followed by an outer count of `00`. These boundaries and the one-field-per-column order are fixture observations, not established native field semantics. The parser applies names only when all five ordered column markers are found; original bytes remain in the record stream if ownership is incomplete.

### Column Unit

In the `ColumnUnit` fixtures, the length-prefixed UTF-8 value contains the Longname and, when present, `0D 0A` followed by the Unit text; the combined value ends with a NUL and `0A 00`. For example, column A's value contains `1 Langname 1 0D 0A Café au Lait 00 0A 00`. The `ColumnComment` fixtures add a second `0D 0A` and the Comment text before the final NUL; for example, column A contains `1 Langname 1 0D 0A Björnsson 0D 0A Résumé 00 0A 00`. Missing trailing components are omitted. The parser splits these values into Longname, Unit, and optional Comment, assigning them only when all ordered column fields are recovered. This combined-field layout is fixture-based evidence.

One fixture's candidate `8F` record spans the end of column C's encoded data and swallows column D's width candidate. The scanner can split this candidate only when a width record at its end is followed by the validated worksheet name, descriptor, and data-envelope pattern; otherwise it preserves the original candidate unchanged.

In the four-value fixtures, the scanner places the bytes beginning with the `0A` immediately preceding B in an `UnknownRegionRecord`. This is a scanner-created gap, not necessarily a native data-block boundary. Conversely, in the empty-column fixture, two copies of `05 27 4E 65` occur inside the candidate `9B` payload, and another begins at B. In the seven-value fixture, the scanner starts a candidate TLV within the apparent numeric block. The scanner's boundaries therefore do not reliably delimit native data blocks.

In `SecondSheetTwoDoubleColumns`, the dataset-name bytes appear in order as `Book1_A`, `Book1_A@2`, and `Book1_B@2`, each in a candidate descriptor followed by data-block-like bytes; book and sheet metadata appear later. The scanner emits `Book1_B@2` in a different candidate TLV arrangement from the other names. The suffix `@2` is evidence for an association with the second sheet, but the general ownership and naming rules remain unproven. The second-sheet description specifies 41 worksheet rows, whereas its two dataset headers and block prefixes contain `0x20` (32); these fields must not be assumed to be the sheet row count.

### Number of sheets

The four `NumberOfSheets` fixtures contain one through four sheets, each with datasets A and B. Dataset names `Book1_A` and `Book1_B` belong to sheet 1; suffixes `@2`, `@3`, and `@4` identify subsequent sheets. The two- and three-additional-sheet fixtures change some TextNumeric width candidates from `85 01 0A` to `85 02 0A 14`. The following name is then a `96` candidate, or a `90` one-byte ordinal marker followed by an `82` name candidate, rather than the previously supported `97` or `91`/`82` forms. These are observed scanner arrangements, not proof of the extra width byte's native meaning.

For example, in `OneBookOneSheetTwoColumns_3MoreSheets`, sheet 1 widths occur at `0x104` and `0x1B6`, and sheet 2 widths at `0x26C` and `0x2D0`, all with payload `0A 14`. Sheet 1 also has the `82 01 02` marker and a `95` data envelope followed by `0A 04` mixed-data bytes; sheet 2 instead uses `9B` envelopes followed by `0A 05`. The parser now recognizes these combinations under the same descriptor, envelope, and contiguity checks as the existing TextNumeric widths, recovering both columns on every sheet. Raw encoded data remains separate from decoded values; unsupported value encodings and sheet-name ownership remain unresolved.

The later sheets' combined column label fields use the shorter prefix `9A 00` rather than `9A 01 00 0A`. Both are preceded here by `84 01 08` and followed by outer/inner lengths and a NUL-terminated UTF-8 string with the `0A 00` trailer. The comment-only content is `0D 0A 0D 0A In sheet N`, leaving Longname and Unit empty. Recognizing only the longer prefix recovered two label fields instead of all fields, so the parser's conservative count check left every multi-sheet comment unresolved. The shorter form is now accepted only with the observed column-marker context; the ordered field count must still match the dataset count before assigning metadata.

The first sheet's mixed data contains standalone compact doubles: `00 00` followed by `11` and one high byte, `1A` and two high bytes, or `50` and a full eight-byte little-endian double. For example, `11 40` represents 2, `1A 08 40` represents 3, and `1A 10 40` represents 4. The remaining low bytes are zero for the compact forms. These same literal forms can occur in repeated numeric runs.

The three strings A/B/C or D/E/F use a different string-pool profile from the earlier four-string fixtures. Its block begins `41 01 #_MSER_STRINGS_PSET 05 01 28 00`, has `F0 13` at relative offset 29, and stores its NUL-terminated strings starting at relative offset 55. Four zero bytes terminate the pool. The earlier four-string profile has `F1 04` and strings at relative offset 54. The outer length and nested length fields bound both supported profiles; the raw three-string metadata words at offsets 39 through 54 remain validated as observed rather than assigned speculative semantics.

For sheet 1 A, the segment `FF FF 1D 01 05 01 03 05 34 00 CE FA` covers the remaining 29 rows: three string references at byte offsets 1, 3, and 5 within the string buffer, followed by 26 blank rows (`34` is twice 26) and the `CE FA` trailer. The earlier four-string fixtures use `07` before four references. The parser now decodes the combined string-and-blank segment, validates all references, lengths, and trailers, and preserves internal empty rows. `DataRowCount` retains the encoded 32-row capacity; `Values` ends at the last nonempty row, yielding six values on sheet 1 and one on each later sheet. Raw encoded bytes remain available independently, and unknown or malformed encodings remain undecoded.

### Worksheet sheet names and ownership

The `NumberOfSheets` files encode the primary sheet name after `C0 FF 8E 04 FF 01 00 04 A4`, followed by a one-byte UTF-8 **byte** length, the name bytes, and `0A 00 10 01`. In the four-sheet file the name starts are `0x6B6` (Änderung, 9 bytes), `0xD4A` (Revision, 8 bytes), `0x13C7` (Riskmanagement, 14 bytes), and `0x1A61` (Meßordner, 10 bytes). The single-sheet name starts at `0x455`; the two-sheet starts are `0x521` and `0xBC6`; the three-sheet starts are `0x5EE`, `0xC83`, and `0x1311`. The names appear a second time in later display metadata with different framing; the parser uses the primary header, not this repeated copy.

Immediately before the primary name framing is `89 01 NN`, the column count, then `80 01 F7`. A preceding eight-byte `88` candidate follows either `84 01 07` for the first sheet or `80 01 07` for later sheets. For later sheets this is preceded by `80 01 ii`, where `ii` is 1, 2, or 3 for dataset suffixes `@2`, `@3`, or `@4`. These controlled fixtures support a zero-based later-sheet ordinal. The parser checks contiguous framing and requires the ordered names, inferred sheet numbers, and column counts to agree before assigning any names.

Book ownership is bounded by the existing named-window framing: a length-prefixed book-name suffix precedes geometry and a five-byte marker. The multi-sheet fixtures use a 15-byte `91` geometry candidate and an `81` marker, whereas the baseline uses a 12-byte geometry candidate and an `84` marker. Names are applied only within a uniquely named book window; the next window bounds the search. Incomplete names, invalid UTF-8, inconsistent ordinals, mismatched column counts, and ambiguous ownership leave `Sheet.Name` unresolved (empty) while preserving columns and raw records. Older worksheet/matrix candidate signatures remain supported separately; other worksheet layouts and empty sheets without datasets are not established by this variation.

### Book long names and project folders

The four `NumberOfBooks` fixtures contain one to four worksheet windows. In the four-book file the window long-name bytes start at `0x56D`, `0x3187`, `0x6823`, and `0x9ECA`: `Bauerfeind`, `Köln`, `Español`, and `✨Sparkle✨`. They follow `80 01 FC C0 FF 02 00 39 8C 01 48 8B 04 FF 7F 00 00`, then a one-byte length including the NUL, UTF-8 bytes, NUL, and `0A`. These fields are associated only with a uniquely named worksheet window, before the next window or its embedded preview. Invalid UTF-8, bad framing, or multiple candidates leave the long name unresolved.

The project tree follows the final preview. Its observed block begins with a 32-bit little-endian length and `0A 80 06 04 DE 36 10 03 89`; the length includes its own four bytes. Folder headers contain a length-prefixed header and a 32-bit byte length for a NUL-terminated UTF-8 name. The root name is the project name and is excluded from folder paths. After folder properties, the observed entry section starts `04 A1 01 64 80 0A 02 75 62 0A`. File and subfolder counts in these fixtures are twice the count; file entries use `80 01 85 00 00` for window ordinal 0, or `80 04 81 01 ii 80 00` for later zero-based window ordinals. Children follow their parent's file entries. In the four-book tree, root ordinal 3 is Cacao, Überlingen has ordinals 0 and 1, and its Grüezi child has ordinal 2. Thus folder ownership must use the window ordinal, not dataset order or traversal order. The parser validates the bounded tree, folder UTF-8 and a complete set of unique in-range ordinals before applying paths; unsupported layouts remain unresolved. Empty `FolderName` represents both the root and unresolved ownership in the current public model.

These fixtures also expose scanner variants: the `PrvwOPJU` index marker appears under tag `81` rather than `84`, dataset descriptors appear under `8E` and `90`, and the numeric width can be `08 14` followed by a `90` ordinal/name pair. The preview tag is a scanner interpretation rather than a reliable native type. Indexing the previews prevents compressed image bytes from swallowing subsequent window headers. The dataset variants are accepted with the same validated descriptor and envelope context; unsupported row-value encodings remain separate from the recovered metadata.

## Proposed parser strategy

1. Provide an ordered, bounded byte cursor over the already parsed records, including TLV tag and length bytes when reconstructing their ranges. Allow reads across contiguous scanner-created boundaries, but respect indexed image boundaries and validate all ranges.
2. Identify worksheet dataset descriptors from their surrounding record sequence and validated signatures, rather than a tag or printable name alone. Associate each descriptor with its following candidate data envelope without assuming that either a TLV or an unknown region is a native block.
3. Decode row-related fields, width, and dataset name conservatively. Keep worksheet row count, dataset capacity, populated extent, and missing values separate; neither row-related field is established as the worksheet row count in general.
4. Decode little-endian single-precision values only when the descriptor and framing support that interpretation. The float fixture's different envelope and missing-value handling require separate validation.
5. Validate the candidate double-block length and determine control-byte, repeat-run, blank-run, and block-boundary rules before implementing a general XOR decoder. Bound decoded row counts and consumed bytes; preserve or reject unsupported encodings instead of silently returning incorrect values.
6. Decode length-delimited names as validated UTF-8, and associate datasets with later book and sheet metadata through validated structure rather than name matching alone.
7. Test values and row positions, row counts, names, type, plot kind, and ownership across the controlled variations, as well as unsupported, truncated, and malformed blocks.

## Proposed additional fixtures

Save each as a copy of the same baseline with one property changed:

1. **Double encoding:** extend the existing literal, exponent, sign, bit-difference, and repeat-run fixtures with runs around encoding boundaries and transitions chosen to distinguish possible control-byte and XOR rules.
2. **Missing values:** vary leading, internal, and trailing blank-run lengths independently, especially around encoding boundaries. Repeat with single precision to check sentinel and run handling.
3. **Row count:** change only the worksheet from 39 to 40 and 100 rows, verify the value positions, and compare the row-related fields. Vary worksheet row count independently of data extent and dataset capacity.
4. **Ownership:** add a second column and separately a second sheet, using distinct names and populated values in each column; vary worksheet row counts independently.
5. **Names:** change only the book label, sheet name, or column name in separate files, including non-ASCII and longer names.
6. **Plot kind:** retain the same double data and save additional X and Y variants to check the `0x50`/`0x60` observation.

## Description corrections

The previously reported contradictions in the seven-value, `AisY`, and `59Rows` descriptions have been corrected. The descriptions now specify seven populated rows, `Double(8)` for `AisY`, and values at rows 0–3 for `59Rows`.

## Column widths

In the `ColumnWidth` fixtures, each column's display width is represented by a `TLV` tag `8D` immediately following a one-byte `89` record with value `01` and a one-byte `82` record. The `8D` payload is one or two bytes, interpreted as an unsigned little-endian integer in tenths: for example, `36 01` is 310 (width 31), `AA` is 170 (width 17), and `0E 01` is 270 (width 27). The parser applies these widths in serialized column order only when the book window is unique and the number of validated width records equals the number of parsed columns; otherwise widths remain unresolved.

The populated Numeric column B also has a worksheet metadata candidate `83 01 42 8D 04 01 00 01 60` after its row marker. The scanner groups its type bytes into the `8D` payload, unlike previously recognized populated numeric headers. In the complete, ordinal- and name-matched column sequence this candidate identifies B as Numeric; the `8D` here is distinct from the later width candidate.
