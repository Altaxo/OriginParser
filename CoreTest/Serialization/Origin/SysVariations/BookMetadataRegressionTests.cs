using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Altaxo.Serialization.Origin.SysVariations
{
    public class BookMetadataRegressionTests
    {
        private static byte[] ReadFixture()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Serialization", "Origin", "TestFiles", "SysVariations", "NumberOfBooks", "OneBookOneSheetTwoColumns_3MoreBooks.opju");
            return File.ReadAllBytes(path);
        }

        private static int FindLast(byte[] source, byte[] pattern)
        {
            int offset = source.AsSpan().LastIndexOf(pattern);
            Assert.True(offset >= 0);
            return offset;
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 0xff)]
        [InlineData(10, 0x7f)]
        public void InvalidBookLongNameDoesNotAffectOtherBooks(int relativeOffset, byte replacement)
        {
            var source = ReadFixture();
            int offset = FindLast(source, Encoding.UTF8.GetBytes("Bauerfeind"));
            source[offset + relativeOffset] = replacement;
            using var stream = new MemoryStream(source);
            var parser = new OriginOpjuParser(stream);
            Assert.Equal(new[] { "", "Köln", "Español", "✨Sparkle✨" }, parser.Books.Select(book => book.LongName));
            Assert.Equal(new[] { "Überlingen", "Überlingen", "Überlingen\\Grüezi", "" }, parser.Books.Select(book => book.FolderName));
        }

        [Fact]
        public void FolderEntriesReferToWindowOrdinalsNotBookOrder()
        {
            var source = ReadFixture();
            int nested = FindLast(source, new byte[] { 0x80, 0x04, 0x81, 0x01, 0x02, 0x80, 0x00 });
            int root = FindLast(source, new byte[] { 0x80, 0x04, 0x81, 0x01, 0x03, 0x80, 0x00 });
            source[nested + 4] = 3;
            source[root + 4] = 2;
            using var stream = new MemoryStream(source);
            var parser = new OriginOpjuParser(stream);
            Assert.Equal(new[] { "Überlingen", "Überlingen", "", "Überlingen\\Grüezi" }, parser.Books.Select(book => book.FolderName));
            Assert.Equal(4, parser.Records.OfType<OriginOpjuParser.PngRecord>().Count());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void InvalidProjectTreeLeavesFolderOwnershipUnresolved(int corruption)
        {
            var source = ReadFixture();
            if (corruption == 0)
            {
                int entry = FindLast(source, new byte[] { 0x80, 0x04, 0x81, 0x01, 0x02, 0x80, 0x00 });
                source[entry + 4] = 1;
            }
            else if (corruption == 1)
            {
                int name = FindLast(source, Encoding.UTF8.GetBytes("Grüezi"));
                source[name + 2] = 0xff;
            }
            else
            {
                int marker = FindLast(source, new byte[] { 0x80, 0x06, 0x04, 0xde, 0x36, 0x10, 0x03, 0x89 });
                source[marker - 5] = 8;
                source[marker - 4] = 0;
            }
            using var stream = new MemoryStream(source);
            var parser = new OriginOpjuParser(stream);
            Assert.All(parser.Books, book => Assert.Empty(book.FolderName));
            Assert.Equal(new[] { "Bauerfeind", "Köln", "Español", "✨Sparkle✨" }, parser.Books.Select(book => book.LongName));
        }
    }
}
