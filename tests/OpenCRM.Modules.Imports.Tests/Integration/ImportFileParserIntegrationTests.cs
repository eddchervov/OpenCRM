using System.IO.Compression;
using System.Text;
using OpenCRM.Modules.Imports;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed class ImportFileParserIntegrationTests
{
    [Fact]
    public void ParseXlsxReadsNamedSheetsSparseCellsAndTextValues()
    {
        var content = CreateWorkbook(
            "<workbook xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Клиенты\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Автомобили\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>",
            "<Relationships><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Target=\"worksheets/sheet2.xml\"/></Relationships>",
            "<worksheet><sheetData><row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>Имя</t></is></c><c r=\"C3\" t=\"inlineStr\"><is><t>Номер</t></is></c></row><row r=\"8\"><c r=\"A8\" t=\"inlineStr\"><is><t>Иван</t></is></c><c r=\"C8\" t=\"inlineStr\"><is><t>0012</t></is></c></row></sheetData></worksheet>",
            "<worksheet><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Телефон</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>+7999</t></is></c></row></sheetData></worksheet>");

        var tables = ImportFileParser.Parse(content, "export.xlsx");

        Assert.Equal(2, tables.Count);
        Assert.Equal("Клиенты", tables[0].Name);
        Assert.Equal(["Имя", "Столбец 2", "Номер"], tables[0].Columns);
        Assert.Equal(8, Assert.Single(tables[0].Rows).Number);
        Assert.Equal("0012", tables[0].Rows[0].Values[2]);
        Assert.Equal("Автомобили", tables[1].Name);
    }

    [Fact]
    public void ParseXlsxRejectsFormulaCells()
    {
        var content = CreateWorkbook(
            "<workbook xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Лист1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            "<Relationships><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
            "<worksheet><sheetData><row r=\"1\"><c r=\"A1\"><f>1+1</f><v>2</v></c></row></sheetData></worksheet>");

        var exception = Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(content, "export.xlsx"));

        Assert.Contains("формула", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseXlsxRejectsDuplicatePhysicalRowNumbers()
    {
        var content = CreateWorkbook(
            "<workbook xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Клиенты\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            "<Relationships><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
            "<worksheet><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Имя</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>Иван</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>Анна</t></is></c></row></sheetData></worksheet>");

        var exception = Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(content, "export.xlsx"));

        Assert.Contains("повторяется номер строки 2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseXlsxRejectsErrorCells()
    {
        var content = CreateWorkbook(
            "<workbook xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Клиенты\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            "<Relationships><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
            "<worksheet><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Имя</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"e\"><v>#VALUE!</v></c></row></sheetData></worksheet>");

        var exception = Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(content, "export.xlsx"));

        Assert.Contains("ячейка с ошибкой", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateWorkbook(string workbook, string relationships, params string[] sheets)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", workbook);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", relationships);
            for (var index = 0; index < sheets.Length; index++)
            {
                WriteEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", sheets[index]);
            }
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
