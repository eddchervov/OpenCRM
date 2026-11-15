using System.IO.Compression;
using System.Text;
using OpenCRM.Modules.Imports;

namespace OpenCRM.Modules.Imports.Tests.Unit;

public sealed class ImportArchiveTests
{
    [Fact]
    public void ZipCombinesCsvTablesWithoutExtractingFilesAndPreservesLeadingZeroes()
    {
        var archive = CreateArchive(("clients.csv", "Id;Имя\n001;Анна"), ("export/cars.csv", "Id;Номер\n002;001AA"));
        var tables = ImportFileParser.Parse(archive, "export.zip", large: true);

        Assert.Equal(2, tables.Count);
        Assert.Equal(["table1", "table2"], tables.Select(table => table.Id));
        Assert.Equal("001", tables[0].Rows.Single().Values[0]);
        Assert.Equal("002", tables[1].Rows.Single().Values[0]);
        Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(archive, "export.zip"));
    }

    [Theory]
    [InlineData("../clients.csv")]
    [InlineData("/clients.csv")]
    [InlineData("folder\\clients.csv")]
    [InlineData("dump.sql")]
    [InlineData("tool.exe")]
    public void ZipRejectsUnexpectedEntries(string name)
    {
        var archive = CreateArchive((name, "Имя\nАнна"));

        Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(archive, "export.zip", large: true));
    }

    [Fact]
    public void ZipEnforcesCombinedRowLimitAndEntryCount()
    {
        var csv = "Имя\n" + string.Concat(Enumerable.Repeat("Анна\n", 50_001));
        var archive = CreateArchive(("first.csv", csv), ("second.csv", csv));
        Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(archive, "export.zip", large: true));
        var manyFiles = CreateArchive(Enumerable.Range(1, 101).Select(index => ($"{index}.csv", "Имя\nАнна")).ToArray());
        Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(manyFiles, "export.zip", large: true));
    }

    private static byte[] CreateArchive(params (string Name, string Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var stream = archive.CreateEntry(entry.Name).Open();
                stream.Write(Encoding.UTF8.GetBytes(entry.Content));
            }
        }

        return output.ToArray();
    }
}
