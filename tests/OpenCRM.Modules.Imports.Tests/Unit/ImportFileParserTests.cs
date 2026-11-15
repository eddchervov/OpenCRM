using System.IO.Compression;
using System.Text;
using OpenCRM.Modules.Imports;

namespace OpenCRM.Modules.Imports.Tests.Unit;

public sealed class ImportFileParserTests
{
    [Fact]
    public void ParseCsvPreservesPhysicalLineNumbersAndQuotedMultilineValues()
    {
        var content = Encoding.UTF8.GetBytes("\uFEFFИмя;Телефон;Номер\r\n\r\n\"Иван; Петров\";\"+7 900\n123\";0012\r\nАнна;555;0003");

        var tables = ImportFileParser.Parse(content, "clients.csv");

        var table = Assert.Single(tables);
        Assert.Equal("clients", table.Name);
        Assert.Equal(["Имя", "Телефон", "Номер"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(3, table.Rows[0].Number);
        Assert.Equal("Иван; Петров", table.Rows[0].Values[0]);
        Assert.Equal("+7 900\n123", table.Rows[0].Values[1]);
        Assert.Equal("0012", table.Rows[0].Values[2]);
        Assert.Equal(5, table.Rows[1].Number);
    }

    [Fact]
    public void ParseCsvGeneratesUniqueNamesForBlankAndDuplicateHeaders()
    {
        var tables = ImportFileParser.Parse(Encoding.UTF8.GetBytes("Имя,,Имя\nА,Б,В"), "data.csv");

        Assert.Equal(["Имя", "Столбец 2", "Имя (2)"], Assert.Single(tables).Columns);
    }

    [Fact]
    public void ParseCsvDetectsDelimiterAfterLeadingBlankLines()
    {
        var content = Encoding.UTF8.GetBytes("\r\n \t \r\nИмя,Телефон\r\nИван,123");

        var table = Assert.Single(ImportFileParser.Parse(content, "data.csv"));

        Assert.Equal(["Имя", "Телефон"], table.Columns);
        Assert.Equal(["Иван", "123"], Assert.Single(table.Rows).Values);
    }

    [Fact]
    public void ParseCsvUsesWindows1251WhenUtf8IsInvalid()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var content = Encoding.GetEncoding(1251).GetBytes("Имя;Телефон\nИван;123");

        var tables = ImportFileParser.Parse(content, "data.csv");

        Assert.Equal("Иван", Assert.Single(Assert.Single(tables).Rows).Values[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseCsvRecognizesUtf16BomWithoutCorruptingCyrillic(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, true, true);
        var content = encoding.GetPreamble().Concat(encoding.GetBytes("Имя\tТелефон\nАнна\t00123")).ToArray();

        var table = Assert.Single(ImportFileParser.Parse(content, "clients.csv"));

        Assert.Equal("Имя", table.Columns[0]);
        Assert.Equal("Анна", Assert.Single(table.Rows).Values[0]);
        Assert.Equal("00123", table.Rows[0].Values[1]);
    }

    [Fact]
    public void ParseCsvRejectsNullCharactersBeforeSavingJsonb()
    {
        Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(Encoding.UTF8.GetBytes("Имя\nАн\0на"), "clients.csv"));
    }

    [Theory]
    [InlineData("Имя;Номер\n\"Иван;123", "Не закрыты кавычки")]
    [InlineData("Имя;Номер\n\"Иван\"x;123", "лишний символ")]
    public void ParseCsvRejectsMalformedQuotes(string csv, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(Encoding.UTF8.GetBytes(csv), "data.csv"));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseRejectsUnsupportedFormat()
    {
        var exception = Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse([1, 2, 3], "backup.sql"));

        Assert.Contains("CSV и XLSX", exception.Message);
    }

    [Fact]
    public void ParseCsvArchiveReportsCorruptZipInRussianAndKeepsOriginalException()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            ImportFileParser.Parse([0x50, 0x4B, 0x03, 0x04], "broken.zip", large: true));

        Assert.Contains("ZIP-файл повреждён", exception.Message);
        Assert.IsType<InvalidDataException>(exception.InnerException);
        Assert.DoesNotContain("Central Directory", exception.Message);
    }

    [Fact]
    public void ParseCsvArchiveReportsUnsupportedEntryCompressionInRussianAndKeepsOriginalException()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        using (var entryStream = archive.CreateEntry("clients.csv", CompressionLevel.Optimal).Open())
        {
            entryStream.Write(Encoding.UTF8.GetBytes("Имя;Телефон\nАнна;123"));
        }

        var content = zipStream.ToArray();
        var localHeader = FindSignature(content, [0x50, 0x4B, 0x03, 0x04]);
        var centralHeader = FindSignature(content, [0x50, 0x4B, 0x01, 0x02]);
        content[localHeader + 8] = 99;
        content[centralHeader + 10] = 99;

        var exception = Assert.Throws<InvalidDataException>(() =>
            ImportFileParser.Parse(content, "clients.zip", large: true));

        Assert.Contains("ZIP-файл повреждён", exception.Message);
        Assert.NotNull(exception.InnerException);
        Assert.DoesNotContain("compression method", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static int FindSignature(byte[] content, byte[] signature)
    {
        for (var index = 0; index <= content.Length - signature.Length; index++)
        {
            if (content.AsSpan(index, signature.Length).SequenceEqual(signature))
            {
                return index;
            }
        }

        throw new InvalidOperationException("В тестовом ZIP не найдена сигнатура заголовка.");
    }
}
