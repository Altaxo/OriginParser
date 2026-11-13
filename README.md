# OriginParser

OriginParser is a extract from the Altaxo project (see https://github.com/Altaxo/Altaxo/tree/master/Altaxo/Core/Serialization/Origin). In Altaxo we have already a parser that parses the old-style Origin .opj project files in many different versions. But the new Origin versions don't support anymore the saving as .opj files. Instead, only the new .opju format is supported for save operations.

Because the format of the new .opju file seems to be completely different from the old .opj format, the aim of this project is to develop a parser that can parse .opju files. The goal is to parse the data, i.e. the data contained in worksheets and matrices.
What I will not strive for is to parse Excel sheets and all kind of graphs.

Finally, if the .opju parser is sufficiently stable, it will be integrated back in the Altaxo project.



# Licenses

The test files with systematic variation of the content (CoreTest/Serialization/Origin/SysVariations) may be useful for other projects as well. They contain the .opju files, a .txt file with the description, and for each folder a \_README.txt with the description of the variation.
They are released under the permissive CC BY-SA license (C) Dr. Dirk Lellinger 2026.



All other test files, and the code of this project is released under the [GPL-3](https://www.gnu.org/licenses/gpl-3.0.html) license, the same license used in [Altaxo](https://github.com/Altaxo/Altaxo).

