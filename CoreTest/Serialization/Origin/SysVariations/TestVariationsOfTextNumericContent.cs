using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfTextNumericContent
    {
        [Theory]
        [InlineData("OneBookOneSheetOneColumn.opju", new object[] { })]
        [InlineData("OneBookOneSheetOneColumn_1_2_3_4_FourStrings.opju", new object[] { 1.0, 2.0, 3.0, 4.0, "Müller", "Meier", "Schultze", "Lampe" })]
        [InlineData("OneBookOneSheetOneColumn_1_3.opju", new object[] { 1.0, 3.0 })]
        [InlineData("OneBookOneSheetOneColumn_2_1.opju", new object[] { 2.0, 1.0 })]
        [InlineData("OneBookOneSheetOneColumn_2_2_3.opju", new object[] { 2.0, 2.0, 3.0 })]
        [InlineData("OneBookOneSheetOneColumn_FourStrings.opju", new object[] { "Müller", "Meier", "Schultze", "Lampe" })]
        [InlineData("OneBookOneSheetOneColumn_FourStrings_1_2_3_4.opju", new object[] { "Müller", "Meier", "Schultze", "Lampe", 1.0, 2.0, 3.0, 4.0 })]
        [InlineData("OneBookOneSheetOneColumnAlternatingValues.opju", new object[] { 1.0, 2.0, 1.0, 2.0 })]
        [InlineData("OneBookOneSheetOneColumnDifferentExponent.opju", new object[] { 1.0, 2.0 })]
        [InlineData("OneBookOneSheetOneColumnFourOne.opju", new object[] { 1.0, 1.0, 1.0, 1.0 })]
        [InlineData("OneBookOneSheetOneColumnOneBlankTwo.opju", new object[] { 1.0, null, 2.0 })]
        [InlineData("OneBookOneSheetOneColumnOneOne.opju", new object[] { 1.0 })]
        [InlineData("OneBookOneSheetOneColumnOneOneBitDifference.opju", new object[] { 1.0, 1.0000000000000002 })]
        [InlineData("OneBookOneSheetOneColumnSameExponent.opju", new object[] { 1.0, 1.5 })]
        [InlineData("OneBookOneSheetOneColumnSignChange.opju", new object[] { 1.0, -1.0 })]
        [InlineData("OneBookOneSheetOneColumnThreeOne.opju", new object[] { 1.0, 1.0, 1.0 })]
        [InlineData("OneBookOneSheetOneColumnTwoOne.opju", new object[] { 1.0, 1.0 })]
        [InlineData("OneBookOneSheetOneColumnTwoRepeatedRuns.opju", new object[] { 1.0, 1.0, 2.0, 2.0 })]
        [InlineData("OneBookOneSheetOneColumnTwoRepeatedRuns8.opju", new object[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0 })]
        [InlineData("OneBookOneSheetOneColumnTwoRepeatedRuns16.opju", new object[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0 })]
        [InlineData("OneBookOneSheetOneColumnWith4Numbers.opju", new object[] { 2.137, 3.983, 1.111, 7.787 })]
        [InlineData("OneBookOneSheetOneColumnWith4Numbers_3EqualNumbers.opju", new object[] { 2.137, 2.137, 2.137, 7.787 })]
        [InlineData("OneBookOneSheetOneColumnWith4Numbers_AtRow10.opju", new object[] { null, null, null, null, null, null, null, null, null, null, 2.137, 3.983, 1.111, 7.787 })]
        [InlineData("OneBookOneSheetOneColumnWith4Numbers_PlusRunOf17Ones.opju", new object[] { 2.137, 3.983, 1.111, 7.787, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 })]
        [InlineData("OneBookOneSheetOneColumnWith4Numbers_With5EmptyRowsInbetween.opju", new object[] { 2.137, 3.983, null, null, null, null, null, 1.111, 7.787 })]
        [InlineData("OneBookOneSheetOneColumnWith7Numbers.opju", new object[] { 2.137, 3.983, 1.111, 7.787, 1.23E-14, 7.465E29, -84578.0 })]
        [InlineData("OneBookOneSheetOneColumnZeroTransition.opju", new object[] { 0.0, 1.0, 0.0 })]
        public void ExpectContent(string shortFileName, object[] expectedContent)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "TextNumericContent", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books);
            var book = parser.Books[0];
            Assert.Equal("Book1", book.Name);
            Assert.Equal("Ößberg", book.LongName);
            Assert.Equal("Meßfolder", book.FolderName);

            Assert.Single(book.Sheets); // with one sheet inside
            var sheet = book.Sheets[0];
            Assert.Equal("Äalesund", sheet.Name);

            Assert.Single(sheet.Columns); // with the expected number of columns
            var column = sheet.Columns[0];

            Assert.Equal("A", column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
            Assert.Equal(ValueType.TextNumeric, column.ValueType); // the expected column types are checked
            Assert.Equal(SpreadColumnType.X, column.SpreadColumnType);
            Assert.Equal(6.7, column.Width, 1E-6); // the columns width is expected to be 6.7 for all columns
            Assert.Empty(column.LongName);
            Assert.Empty(column.Unit);
            Assert.Empty(column.Comment);

            Assert.NotNull(column.Values);
            Assert.True(column.Values.Count >= expectedContent.Length, $"Expected at least {expectedContent.Length} decoded rows, got {column.Values.Count}.");
            for (int row = 0; row < column.Values.Count; row++)
            {
                var actual = column.Values[row];
                var expected = row < expectedContent.Length ? expectedContent[row] : null;
                if (row < expectedContent.Length && expected is double && !double.IsNaN((double)expected))
                {
                    Assert.True(actual.IsDouble, $"Row {row}: expected a numeric value.");
                    AssertAreEqual((double)expected, actual.AsDouble(), 1e-14, 0);
                }
                else if (row < expectedContent.Length && expected is string)
                {
                    Assert.True(actual.IsString, $"Row {row}: expected a string value.");
                    Assert.Equal((string)expected, actual.AsString());
                }
                else
                {
                    Assert.True(actual.IsEmpty || (actual.IsDouble && double.IsNaN(actual.AsDouble())),
                        $"Row {row}: expected an empty cell.");
                }
            }
        }

        static void AssertAreEqual(double expected, double actual, double relativeAccuracy, double absoluteAccurcy)
        {
            var allowedDiff = Math.Abs(absoluteAccurcy) + Math.Abs(relativeAccuracy * expected);
            Assert.True(Math.Abs(expected - actual) <= allowedDiff, $"Expected {expected} but got {actual}. Allowed difference: {allowedDiff}");
        }
    }
}
