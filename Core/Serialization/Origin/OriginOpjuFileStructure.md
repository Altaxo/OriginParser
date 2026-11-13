# PNG offset table
In every fixture examined:
•	A little-endian 32-bit value at file offset 61 points to the table.
•	At that location is a 32-bit entry count. At table offset 12 is the byte size of the entries.
•	Starting at table offset 16, each 16-byte entry contains a little-endian 64-bit file offset followed by a 64-bit length.
All 30 entries across the 14 files point to complete PNG images. For example, Matrix137x179.opju points to a table at offset 13427; its entry identifies a PNG at offset 2389, length 10810.
These lengths are useful for skipping PNG data reliably. They are not the lengths of the current UnknownRegionRecords: those records are scanner-created gaps, which can include bytes before or after a PNG. In OneWorksheetWithTwoSheets.opju, one PNG even overlaps two gaps because the scanner resynchronizes inside the image.


# Other length fields
There is also a repeated length-prefixed structure near the beginning: offset 69 contains 123, followed by 0A at offset 73, 123 bytes of content, and another 0A at offset 197. Similar length-and-delimiter framing appears elsewhere, including around project and folder names.
The 32-bit field at offset 189 equals the whole file length in every fixture examined.

A reasonable guess is that the 123 bytes are project or application state, rather than a single opaque data block. Across the 14 fixtures:
File offset	Observation	Possible meaning
74–75	Signed 16-bit value, 0 or 2	Window state or mode
76–83	Four signed 16-bit values	Window rectangle: left, top, right, bottom
88–91	32-bit value 89, constant	Possibly a nested size or version field
101–108	Same 64-bit floating-point value, approximately 9.700188	Unknown; possibly version or configuration data
117–144	Seven changing 32-bit values	Unknown identifiers or state; several exceed the file size, so they are not all absolute file offsets
145–184	All zero	Possibly reserved space
185–188	32-bit value 96, constant	Unknown
189–192	32-bit value equal to file size	File length
The window-rectangle guess is particularly plausible: one fixture has (320, 95, 1512, 727), while others have (-8, -8, 1928, 1168) or (1912, -8, 3848, 1168). Those resemble normal and maximized windows on different monitors.
These are still hypotheses based on the bundled fixtures. Saving controlled copies after moving or resizing the Origin window would be a useful way to verify the geometry fields.
