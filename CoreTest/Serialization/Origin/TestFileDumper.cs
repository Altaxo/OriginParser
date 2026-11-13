using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Altaxo.Serialization.Origin
{
    public class TestFileDumper
    {
        [Fact]
        public void DumpTestFiles()
        {
            var path = AppDomain.CurrentDomain.BaseDirectory;

            // Search each of the parent directories to find the directory containing a csproj file, which is assumed to be the root of the project.
            while (path != null && !Directory.GetFiles(path, "*.csproj").Any())
            {
                path = Directory.GetParent(path)?.FullName;
            }

            path = Path.Combine(path, "Serialization", "Origin", "TestFiles", "SysVariations");

            var dir = new DirectoryInfo(path);
            var files = dir.GetFiles("*.opju", SearchOption.AllDirectories);


            foreach (var file in files)
            {
                using var stream = file.OpenRead();
                var parser = new OriginOpjuParser(stream);

                var descriptionPath = Path.Combine(file.DirectoryName, Path.GetFileNameWithoutExtension(file.Name) + ".txt");

                Assert.True(File.Exists(descriptionPath), $"Description file '{descriptionPath}' does not exist.");

                var dumpPath = Path.Combine(file.DirectoryName, Path.GetFileNameWithoutExtension(file.Name) + ".dump");

                using var fileWriter = new StreamWriter(dumpPath, false, Encoding.UTF8);

                foreach (var record in parser.Records)
                {
                    record.Dump(fileWriter);
                }
            }
        }
    }
}
