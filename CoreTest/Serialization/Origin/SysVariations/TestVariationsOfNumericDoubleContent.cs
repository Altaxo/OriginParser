using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfNumericDoubleContent
    {
        [Theory]
        [InlineData("OneBookOneSheetOneDoubleColumn.opju", new double[] { })]
        [InlineData("OneBookOneSheetOneDoubleColumn_1_3.opju", new double[] { 1, 3 })]
        [InlineData("OneBookOneSheetOneDoubleColumn_2_1.opju", new double[] { 2, 1 })]
        [InlineData("OneBookOneSheetOneDoubleColumn_2_2_3.opju", new double[] { 2, 2, 3 })]
        [InlineData("OneBookOneSheetOneDoubleColumnAlternatingValues.opju", new double[] { 1, 2, 1, 2 })]
        [InlineData("OneBookOneSheetOneDoubleColumnDifferentExponent.opju", new double[] { 1, 2 })]
        [InlineData("OneBookOneSheetOneDoubleColumnFourOne.opju", new double[] { 1, 1, 1, 1 })]
        [InlineData("OneBookOneSheetOneDoubleColumnOneBlankTwo.opju", new double[] { 1, double.NaN, 2 })]
        [InlineData("OneBookOneSheetOneDoubleColumnOneOne.opju", new double[] { 1 })]
        [InlineData("OneBookOneSheetOneDoubleColumnOneOneBitDifference.opju", new double[] { 1, 1.0000000000000002 })]
        [InlineData("OneBookOneSheetOneDoubleColumnSameExponent.opju", new double[] { 1, 1.5 })]
        [InlineData("OneBookOneSheetOneDoubleColumnSignChange.opju", new double[] { 1, -1 })]
        [InlineData("OneBookOneSheetOneDoubleColumnThreeOne.opju", new double[] { 1, 1, 1 })]
        [InlineData("OneBookOneSheetOneDoubleColumnTwoOne.opju", new double[] { 1, 1 })]
        [InlineData("OneBookOneSheetOneDoubleColumnTwoRepeatedRuns.opju", new double[] { 1, 1, 2, 2 })]
        [InlineData("OneBookOneSheetOneDoubleColumnTwoRepeatedRuns8.opju", new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2 })]
        [InlineData("OneBookOneSheetOneDoubleColumnTwoRepeatedRuns16.opju", new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 })]
        [InlineData("OneBookOneSheetOneDoubleColumnWith4Numbers.opju", new double[] { 2.137, 3.983, 1.111, 7.787 })]
        [InlineData("OneBookOneSheetOneDoubleColumnWith4Numbers_3EqualNumbers.opju", new double[] { 2.137, 2.137, 2.137, 7.787 })]
        [InlineData("OneBookOneSheetOneDoubleColumnWith4Numbers_AtRow10.opju", new double[] { double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 2.137, 3.983, 1.111, 7.787 })]
        [InlineData("OneBookOneSheetOneDoubleColumnWith4Numbers_PlusRunOf17Ones.opju", new double[] { 2.137, 3.983, 1.111, 7.787, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 })]
        [InlineData("OneBookOneSheetOneDoubleColumnWith4Numbers_With5EmptyRowsInbetween.opju", new double[] { 2.137, 3.983, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 1.111, 7.787 })]
        [InlineData("OneBookOneSheetOneDoubleColumnWith7Numbers.opju", new double[] { 2.137, 3.983, 1.111, 7.787, 1.23E-14, 7.465E29, -84578.0 })]
        [InlineData("OneBookOneSheetOneDoubleColumnZeroTransition.opju", new double[] { 0, 1, 0 })]
        public void ExpectContent(string shortFileName, double[] expectedContent)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumericDoubleContent", shortFileName);
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
            Assert.Equal(ValueType.Numeric, column.ValueType); // the expected column types are checked
            Assert.Equal(DeserializedDataType.Double, column.DeserializedDataType); // the expected column types are checked
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
                var expected = row < expectedContent.Length ? expectedContent[row] : double.NaN;
                if (row < expectedContent.Length && !double.IsNaN(expected))
                {
                    Assert.True(actual.IsDouble, $"Row {row}: expected a numeric value.");
                    AssertAreEqual(expected, actual.AsDouble(), 1e-14, 0);
                }
                else
                {
                    Assert.True(actual.IsCellEmpty, $"Row {row}: expected an empty cell.");
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
