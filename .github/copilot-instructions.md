# Copilot Instructions

## Project Guidelines

* For the OPJU parser, use an internal partial Book → Sheet → column model that preserves unresolved ownership and raw encoded data separately from decoded row values.
* For OPJU worksheet datasets, infer the book and sheet from names such as Book1\_A for sheet 1 and Book1\_A@2 for sheet 2; retain other unproven metadata as unresolved.
* The SysVariations test files folder contains systematic variations of one property per subfolder. Each subfolder contains a \_README.txt description of the variation, and various .opju test files. Each .opju test file comes with a corresponding .txt file containing the description, and a corresponding .dump file containing the hex dump of the .opju file, already parsed into TLV records and binary blocks.
* After figuring out how to parse a property, update Findings.md with the new findings.
* When adding new code, please comment essential sections.
* 

