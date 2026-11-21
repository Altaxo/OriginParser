The user's findings about record parsing refer to the file "F:\\Altaxo\\Utilities\\OriginParser\\CoreTest\\Serialization\\Origin\\TestFiles\\SysVariations\\ColumnComment\\OneBookOneSheetFiveColumns\_A\_B\_C\_D\_E.opju"

All adresses and bytes are in hexadecimal format.

Findings:

* TLV records start at address 000018
* the TLV record at 000038 with content prvwOPJU and a length byte of 0A should have a length of 10, since it contains the 8-byte offset to the image table, which is described elsewhere. The image table must be processed, and the span of the image blocks must be parsed into records and reserved, so that those addresses  can not be parsed twice.
* After this, at 00004A, a block in the format <4 byte length> 0A ... content of this length .... 0A follows
* After this, at 0000E0, an empty block follows in the format 00 00 00 00 0A 
* At 0000E5 there is a parent TLV record with length=33 which ends at 000119. This record contains the 4 bytes 03 00 00 01 and then 0A child TLV records. The first byte 03 is possibly the length of the next arbitrary bytes.
* at 000119, there is a TLV record with tag 9B and a length of 18
* at 000133 there is a value block that starts with 0A.  How the end of the block is determined is unclear. Hypotesis1: since 15A-133=27, the length is coded in byte 4 of the block. Hypotesis 2: the block ends at the byte sequence 00 0A 00, and then some zero bytes until the next 80 marks the next TLV record.
* at 00015A there is a parent TLV record with tag 80 and length 33 which ends at 00018E . It contains the 4 bytes 03 00 00 01 and then 0A child TLV records.
* at 00018E there is a TLV record with tag=9B and length=18
* at 0001A8 there is a value block that starts with 0A. Its length of 33 seems nowhere to be coded, so hypotesis2 may hold that it ends with 00 0A 00 00 00
* at 0001DB there is a parent TLV record with tag 80 and length 33 that ends at 00020F. It contains the 4 bytes 03 00 00 01 and then 0A child TLV records.
* at 00020F there is a TLV recprd with tag 9B and length=18
* at 000229 there is a value block that starts with 0A. Its length of 26 seems nowhere to be coded, so hypotesis2 may hold that the end is marked with 00 0A 00 00 00. 
* at 00024F there is a parent TLV record with length 34 that ends at 000284. It contains the 4 bytes 03 00 00 01 and then child TLV records.
* at 000284 there is a TLV record with tag 9B and length=18
* at 00029E there is a value block that starts with 0A. Its length of 33 seems nowhere to be coded, so hypotesis2 may hold that the end is marked with 00 0A 00 00 00.
* at 0002D1 there is a parent TLV record with tag 80 and length 33 that ends at 000305. It contains the 4 bytes 03 00 00 01 and then 0A child TLV records.
* at 000305, there is a TLV record with tag 9B and length=18
* at 00031F, there is a value block that starts with 0A and ends at the bytes 00 0A 00 (hypotesis2)? Then some fill bytes and 00 0A 00 again until the next 80 wich marks a new TLV record.
* at 000362 there is a WINDOW TLV record with length=77 with 8 children
* at 0003DB there is a UNKNOWN block that starts with 09. Currently, I have no idea how to get to the next TLV record. The block seems to contain some arbitrary bytes, then script text, and the end is marked with the ASCII chars 'ORIGIN, followed by some 00 and 0A.
* the next TLV records start at 4B4, although there is a slip in the TLV record at 0004CB, which should be length=0B instead of 0A
* inbetween the TLV records there are unknown blocks, where I have no idea how to get the length of those blocks. The ends are not marked with 0A. Are you able to figure out how these blocks can be parsed?
* at 0005BB there is a unknown block that starts with A9. Its end seems to be marked with 00 00 00 00 0A.
* at 000AAC there is a TLV record with tag=80 and length=2F which ends at 000ADC. It contains the two bytes 01 10 and then child TLV records
* at 000ADC there is the byte sequence 9A 01 00 0A, followed by 2B at 000AE0. This seems to be the length of the block since 000AE0+2B=000B0B, which points to a 00 0A 00 sequence at that address
* at 000B0E there is a parent TLV record with tag=80 and length=32 which ends at 000B41.  It contains the two bytes 01 10 and then child TLV records
* at 000B41 there is the byte sequence 9A 01 00 0A, followed by 25 at 000B45. This seems to be the length of the block since 000B45+25=000B6A, which points to a 00 0A 00 sequence at that address
* at B6D there is a parent TLV record with tag=80 and length=31 which ends at 000B9F.  It contains the two bytes 01 10 and then child TLV records. Again, the first byte 01 is the lenght of the next arbitrary bytes.
* at 000B9F there is the byte sequence 9A 01 00 0A, followed by 28 at 000BA3. This seems to be the length of the block since 000BA3+28=000BCB, which points to a 00 0A 00 sequence at that address
* at 000BCE there is a parent TLV record with tag=80 and length=34 which ends at 000C03.  It contains the two bytes 01 10 and then child TLV records
* at 000C03 there is the byte sequence 9A 01 00 0A, followed by 27 at 000C07. This seems to be the length of the block since 000C03+27=000C2E, which points to a 00 0A 00 sequence at that address
* at 000C31 there is a parent TLV record with tag=80 and length=31 which ends at 000C63.  It contains the two bytes 01 10 and then child TLV records
* 000C03 there is the byte sequence 9A 01 00 0A, followed by 27 at 000C67. This seems to be the length of the block since 000C63+27=000C8E, which points to a 00 0A 00 sequence at that address
* at 000C91 there a some sequences of 00 00 00 00 00 0A, followed by a block at 000CB6 which end at CF8 because there is a PNGIMAGE at CF8
* it follows an unknown block
* at 004310 there is the image table
* then some unknown block and some TLV records



