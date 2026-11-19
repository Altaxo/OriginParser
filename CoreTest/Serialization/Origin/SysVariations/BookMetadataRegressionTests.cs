using System;
using System.IO;
using System.Linq;
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


    }
}
