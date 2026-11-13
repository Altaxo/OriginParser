using System;
using System.IO;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class TestVariationsOfNumericSingleContent
    {
        [Theory]
        [InlineData("OneBookOneSheetOneRealColumn.opju", new float[] { })]
        [InlineData("OneBookOneSheetOneRealColumnAlternatingValues.opju", new float[] { 1, 2, 1, 2 })]
        [InlineData("OneBookOneSheetOneRealColumnDifferentExponent.opju", new float[] { 1, 2 })]
        [InlineData("OneBookOneSheetOneRealColumnFourOne.opju", new float[] { 1, 1, 1, 1 })]
        [InlineData("OneBookOneSheetOneRealColumnOneBlankTwo.opju", new float[] { 1, 0, 2 })]
        [InlineData("OneBookOneSheetOneRealColumnOneOne.opju", new float[] { 1 })]
        [InlineData("OneBookOneSheetOneRealColumnOneOneBitDifference.opju", new float[] { 1, 1.0000001f })]
        [InlineData("OneBookOneSheetOneRealColumnSameExponent.opju", new float[] { 1, 1.5f })]
        [InlineData("OneBookOneSheetOneRealColumnSignChange.opju", new float[] { 1, -1 })]
        [InlineData("OneBookOneSheetOneRealColumnThreeOne.opju", new float[] { 1, 1, 1 })]
        [InlineData("OneBookOneSheetOneRealColumnTwoOne.opju", new float[] { 1, 1 })]
        [InlineData("OneBookOneSheetOneRealColumnTwoRepeatedRuns.opju", new float[] { 1, 1, 2, 2 })]
        [InlineData("OneBookOneSheetOneRealColumnTwoRepeatedRuns8.opju", new float[] { 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2 })]
        [InlineData("OneBookOneSheetOneRealColumnTwoRepeatedRuns16.opju", new float[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 })]
        [InlineData("OneBookOneSheetOneRealColumnWith4Numbers.opju", new float[] { 2.137f, 3.983f, 1.111f, 7.787f })]
        [InlineData("OneBookOneSheetOneRealColumnWith4Numbers_3EqualNumbers.opju", new float[] { 2.137f, 2.137f, 2.137f, 7.787f })]
        [InlineData("OneBookOneSheetOneRealColumnWith4Numbers_AtRow10.opju", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2.137f, 3.983f, 1.111f, 7.787f })]
        [InlineData("OneBookOneSheetOneRealColumnWith4Numbers_PlusRunOf17Ones.opju", new float[] { 2.137f, 3.983f, 1.111f, 7.787f, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 })]
        [InlineData("OneBookOneSheetOneRealColumnWith4Numbers_With5EmptyRowsInbetween.opju", new float[] { 2.137f, 3.983f, 0, 0, 0, 0, 0, 1.111f, 7.787f })]
        [InlineData("OneBookOneSheetOneRealColumnWith7Numbers.opju", new float[] { 2.137f, 3.983f, 1.111f, 7.787f, 1.23E-14f, 7.465E29f, -84578.0f })]
        [InlineData("OneBookOneSheetOneRealColumnZeroTransition.opju", new float[] { 0, 1, 0 })]
        public void ExpectContent(string shortFileName, float[] expectedContent)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumericSingleContent", shortFileName);
            using var stream = File.OpenRead(path);
            var parser = new OriginOpjuParser(stream);

            Assert.Single(parser.Books); // there is only one Book
            Assert.Equal("Book1", parser.Books[0].Name); // named 'Book1'
            Assert.Single(parser.Books[0].Sheets); // with one sheet inside
            Assert.Single(parser.Books[0].Sheets[0].Columns); // with the expected number of columns
            var column = parser.Books[0].Sheets[0].Columns[0];
            Assert.Equal("A", column.Name); // the columns short names are expected to be A, B, C, D, E, F, G, H, I for the first 9 columns
            Assert.Equal(ValueType.Numeric, column.ValueType); // the expected column types are checked
            Assert.Equal(DeserializedDataType.Single, column.DeserializedDataType); // the expected column types are checked

            Assert.NotNull(column.Values);
            Assert.True(column.Values.Count >= expectedContent.Length, $"Expected at least {expectedContent.Length} decoded rows, got {column.Values.Count}.");
            for (int row = 0; row < column.Values.Count; row++)
            {
                var actual = column.Values[row];
                if (row < expectedContent.Length)
                {
                    Assert.True(actual.IsDouble, $"Row {row}: expected a numeric value.");
                    Assert.Equal((double)expectedContent[row], actual.AsDouble(), 1e-7);
                }
                else
                {
                    Assert.True(actual.IsEmpty || (actual.IsDouble && double.IsNaN(actual.AsDouble())),
                        $"Row {row}: expected an empty cell.");
                }
            }
        }
    }
}
