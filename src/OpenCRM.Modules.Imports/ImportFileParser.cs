using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports;

/// <summary>Разбирает табличные файлы для подготовки импорта данных.</summary>
public sealed class ImportFileParser
{
    private readonly int _maxFileSize;
    private readonly int _maxExpandedSize;
    private readonly int _maxRows;
    private readonly bool _allowZip;
    private const int MaxColumns = 100;
    private const int MaxCellLength = 10_000;

    private ImportFileParser(bool large)
    {
        this._allowZip = large;
        this._maxFileSize = (large ? 50 : 10) * 1024 * 1024;
        this._maxExpandedSize = (large ? 200 : 30) * 1024 * 1024;
        this._maxRows = large ? 100_000 : 5000;
    }

    /// <summary> Разбирает файл с отдельными пределами для фоновой обработки. </summary>
    public static List<ImportSourceTable> Parse(byte[] content, string fileName, bool large = false) =>
        new ImportFileParser(large).ParseCore(content, fileName);

    /// <summary>Разбирает CSV или XLSX и возвращает исходные таблицы.</summary>
    private List<ImportSourceTable> ParseCore(byte[] content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (content.Length == 0 || content.Length > this._maxFileSize)
        {
            throw new InvalidDataException("Размер файла превышает разрешённый предел или равен нулю.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".csv" => this.ParseCsv(content, Path.GetFileNameWithoutExtension(fileName)),
            ".xlsx" => this.ParseXlsx(content),
            ".zip" when this._allowZip => this.ParseCsvArchive(content),
            _ => throw new InvalidDataException("Поддерживаются файлы CSV и XLSX.")
        };
    }

    private List<ImportSourceTable> ParseCsvArchive(byte[] content)
    {
        using var input = new MemoryStream(content, writable: false);
        using var archive = this.OpenZipArchive(input);
        var archiveEntries = this.ReadZipEntries(archive);
        if (archiveEntries.Count is 0 or > 200)
        {
            throw new InvalidDataException("ZIP должен содержать до 100 CSV-файлов и не более 200 записей с учётом папок.");
        }

        var entries = archiveEntries.Where(entry => !entry.FullName.EndsWith('/')).OrderBy(entry => entry.FullName, StringComparer.Ordinal).ToList();
        if (entries.Count is 0 or > 100 || entries.Any(entry =>
            !Path.GetExtension(entry.Name).Equals(".csv", StringComparison.OrdinalIgnoreCase) || entry.Length is 0 ||
            entry.Length > this._maxFileSize || entry.FullName.Length > 260 || entry.FullName.Contains('\\') ||
            entry.FullName.StartsWith('/') || entry.FullName.Split('/').Any(part => part is ".." or ".")))
        {
            throw new InvalidDataException("ZIP должен содержать только непустые CSV-файлы до 50 МБ с обычными именами.");
        }

        if (entries.Sum(entry => entry.Length) > this._maxExpandedSize)
        {
            throw new InvalidDataException("Распакованные CSV-файлы превышают 200 МБ.");
        }

        var result = new List<ImportSourceTable>();
        var rowCount = 0;
        foreach (var entry in entries)
        {
            var bytes = this.ReadZipEntry(entry);

            var table = this.ParseCsv(bytes, entry.FullName).Single();
            rowCount += table.Rows.Count;
            this.EnsureRowLimit(rowCount);
            table.Id = $"table{result.Count + 1}";
            result.Add(table);
        }

        return result;
    }

    private List<ZipArchiveEntry> ReadZipEntries(ZipArchive archive)
    {
        try
        {
            return archive.Entries.ToList();
        }
        catch (Exception exception) when (ImportFileParser.IsZipReadException(exception))
        {
            throw ImportFileParser.CreateInvalidZipException(exception);
        }
    }

    private byte[] ReadZipEntry(ZipArchiveEntry entry)
    {
        byte[] content;
        bool hasExtraData;
        try
        {
            using var stream = entry.Open();
            content = new byte[checked((int)entry.Length)];
            stream.ReadExactly(content);
            hasExtraData = stream.ReadByte() != -1;
        }
        catch (Exception exception) when (ImportFileParser.IsZipReadException(exception))
        {
            throw ImportFileParser.CreateInvalidZipException(exception);
        }

        if (hasExtraData)
        {
            throw new InvalidDataException("Размер файла в ZIP не совпадает с его описанием.");
        }

        return content;
    }

    private static bool IsZipReadException(Exception exception) =>
        exception is InvalidDataException or IOException or NotSupportedException;

    private static InvalidDataException CreateInvalidZipException(Exception exception) =>
        new("ZIP-файл повреждён или содержит недопустимое сжатие.", exception);

    private ZipArchive OpenZipArchive(Stream input)
    {
        try
        {
            return new ZipArchive(input, ZipArchiveMode.Read);
        }
        catch (Exception exception) when (ImportFileParser.IsZipReadException(exception))
        {
            throw ImportFileParser.CreateInvalidZipException(exception);
        }
    }

    private List<ImportSourceTable> ParseCsv(byte[] content, string tableName)
    {
        var text = this.DecodeCsv(content);
        var delimiter = this.DetectDelimiter(text);
        var records = this.ReadCsvRecords(text, delimiter);
        var headerIndex = records.FindIndex(record => record.Values.Any(value => !string.IsNullOrWhiteSpace(value)));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("Файл не содержит заголовков и строк данных.");
        }

        var headers = this.CreateHeaders(records[headerIndex].Values);
        var table = new ImportSourceTable
        {
            Id = "table1",
            Name = tableName,
            Columns = headers
        };

        foreach (var record in records.Skip(headerIndex + 1))
        {
            if (record.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            this.EnsureColumnLimit(record.Values.Count);
            this.ExtendHeaders(headers, record.Values.Count);
            table.Rows.Add(new ImportSourceRow
            {
                Number = record.Number,
                Values = this.PadValues(record.Values, headers.Count)
            });
            this.EnsureRowLimit(table.Rows.Count);
        }

        if (table.Rows.Count == 0)
        {
            throw new InvalidDataException("Файл не содержит строк данных.");
        }

        return [table];
    }

    private string DecodeCsv(byte[] content)
    {
        if (content.AsSpan().StartsWith(Encoding.UTF32.Preamble) || content.AsSpan().StartsWith(new byte[] { 0, 0, 254, 255 }))
        {
            throw new InvalidDataException("CSV в UTF-32 не поддерживается. Сохраните файл в UTF-8.");
        }

        if (content.AsSpan().StartsWith(Encoding.Unicode.Preamble) || content.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            var bigEndian = content[0] == 254;
            try
            {
                return new UnicodeEncoding(bigEndian, false, true).GetString(content, 2, content.Length - 2);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("CSV содержит повреждённый текст UTF-16.", exception);
            }
        }

        try
        {
            var offset = content.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;

            return new UTF8Encoding(false, true).GetString(content, offset, content.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding(1251).GetString(content);
        }
    }

    private List<ImportSourceTable> ParseXlsx(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            long expandedSize = 0;
            foreach (var entry in archive.Entries)
            {
                expandedSize = checked(expandedSize + entry.Length);
                if (expandedSize > this._maxExpandedSize)
                {
                    throw new InvalidDataException("Распакованный файл Excel превышает допустимый размер.");
                }
            }

            var sharedStrings = this.ReadSharedStrings(archive);
            var sheets = this.ReadWorkbookSheets(archive);
            var tables = new List<ImportSourceTable>();
            var totalRows = 0;
            for (var index = 0; index < sheets.Count; index++)
            {
                var sheet = sheets[index];
                var entry = archive.GetEntry(sheet.Path);
                if (entry is null)
                {
                    throw new InvalidDataException("В XLSX не найден лист из описания книги.");
                }

                using var sheetStream = entry.Open();
                var document = this.LoadXml(sheetStream);
                var table = this.ParseWorksheet(document, sheet.Name, index + 1, sharedStrings);
                if (table is null)
                {
                    continue;
                }

                totalRows += table.Rows.Count;
                this.EnsureRowLimit(totalRows);
                tables.Add(table);
            }

            if (tables.Count == 0)
            {
                throw new InvalidDataException("Книга Excel не содержит строк данных.");
            }

            return tables;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or IOException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("Не удалось прочитать файл XLSX.", exception);
        }
    }

    private List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = this.LoadXml(stream);
        return document.Descendants().Where(element => element.Name.LocalName == "si")
            .Select(element => string.Concat(element.Descendants().Where(child => child.Name.LocalName == "t").Select(child => child.Value)))
            .ToList();
    }

    private List<(string Name, string Path)> ReadWorkbookSheets(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("Файл не является книгой XLSX: отсутствует описание книги.");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("В XLSX отсутствуют связи листов.");

        using var workbookStream = workbookEntry.Open();
        using var relationshipsStream = relationshipsEntry.Open();
        var workbook = this.LoadXml(workbookStream);
        var relationships = this.LoadXml(relationshipsStream).Descendants()
            .Where(element => element.Name.LocalName == "Relationship")
            .ToDictionary(
                element => (string?)element.Attribute("Id") ?? string.Empty,
                element => (string?)element.Attribute("Target") ?? string.Empty,
                StringComparer.Ordinal);

        var sheets = new List<(string Name, string Path)>();
        foreach (var element in workbook.Descendants().Where(element => element.Name.LocalName == "sheet"))
        {
            var relationshipId = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
            if (relationshipId is null || !relationships.TryGetValue(relationshipId, out var target))
            {
                throw new InvalidDataException("В XLSX обнаружен лист без корректной связи.");
            }

            var path = this.ResolveWorkbookTarget(target);
            if (archive.GetEntry(path) is null)
            {
                throw new InvalidDataException("В XLSX не найден лист из описания книги.");
            }

            sheets.Add(((string?)element.Attribute("name") ?? $"Лист {sheets.Count + 1}", path));
        }

        return sheets;
    }

    private ImportSourceTable? ParseWorksheet(
        XDocument document,
        string name,
        int tableNumber,
        IReadOnlyList<string> sharedStrings)
    {
        var columns = 0;
        var rows = document.Descendants().Where(element => element.Name.LocalName == "row");
        var table = new ImportSourceTable { Id = $"table{tableNumber}", Name = name };
        var seenRowNumbers = new HashSet<int>();
        var hasHeaders = false;
        var rowIndex = 0;

        foreach (var row in rows)
        {
            rowIndex++;
            var rowNumber = this.ParseRowNumber(row.Attribute("r")?.Value, rowIndex);
            if (!seenRowNumbers.Add(rowNumber))
            {
                throw new InvalidDataException($"В книге Excel на листе «{name}» повторяется номер строки {rowNumber}.");
            }

            var cells = this.ReadWorksheetCells(row, sharedStrings);
            if (!hasHeaders)
            {
                if (cells.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                columns = cells.Keys.DefaultIfEmpty(0).Max();
                this.EnsureColumnLimit(columns);
                table.Columns = this.CreateHeaders(Enumerable.Range(1, columns).Select(index => cells.GetValueOrDefault(index, string.Empty)).ToList());
                hasHeaders = true;
                continue;
            }

            if (cells.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var lastColumn = cells.Keys.DefaultIfEmpty(0).Max();
            if (lastColumn > columns)
            {
                this.EnsureColumnLimit(lastColumn);
                this.ExtendHeaders(table.Columns, lastColumn);
                columns = lastColumn;
            }

            table.Rows.Add(new ImportSourceRow
            {
                Number = rowNumber,
                Values = Enumerable.Range(1, columns).Select(index => cells.GetValueOrDefault(index, string.Empty)).ToList()
            });
            this.EnsureRowLimit(table.Rows.Count);
        }

        return hasHeaders && table.Rows.Count > 0 ? table : null;
    }

    private Dictionary<int, string> ReadWorksheetCells(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<int, string>();
        foreach (var cell in row.Elements().Where(element => element.Name.LocalName == "c"))
        {
            if (cell.Elements().Any(element => element.Name.LocalName == "f"))
            {
                throw new InvalidDataException("В книге Excel обнаружена формула. Замените её результатом перед импортом.");
            }

            var reference = cell.Attribute("r")?.Value
                ?? throw new InvalidDataException("В книге Excel обнаружена ячейка без адреса.");
            var column = this.ParseColumnNumber(reference);
            this.EnsureColumnLimit(column);
            var type = (string?)cell.Attribute("t");
            if (type == "e")
            {
                throw new InvalidDataException("В книге Excel обнаружена ячейка с ошибкой. Исправьте её перед импортом.");
            }

            var value = type == "inlineStr"
                ? string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value))
                : cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? string.Empty;

            if (type == "s")
            {
                if (!int.TryParse(value, out var index) || index < 0 || index >= sharedStrings.Count)
                {
                    throw new InvalidDataException("В книге Excel обнаружена некорректная ссылка на текстовое значение.");
                }

                value = sharedStrings[index];
            }

            this.EnsureCellLength(value);
            cells[column] = value;
        }

        return cells;
    }

    private XDocument LoadXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = this._maxExpandedSize
        });

        return XDocument.Load(reader);
    }

    private string ResolveWorkbookTarget(string target)
    {
        var normalized = target.Replace('\\', '/');
        var path = normalized.StartsWith('/') ? normalized.TrimStart('/') : $"xl/{normalized}";
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    throw new InvalidDataException("В XLSX обнаружен недопустимый путь листа.");
                }

                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            if (part.Length > 0 && part != ".")
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }

    private List<CsvRecord> ReadCsvRecords(string text, char delimiter)
    {
        var records = new List<CsvRecord>();
        var values = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var afterQuote = false;
        var fieldStarted = false;
        var rowNumber = 1;
        var recordNumber = 1;
        var nonemptyRecordCount = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    inQuotes = false;
                    afterQuote = true;
                }
                else
                {
                    field.Append(character);
                    if (character == '\n')
                    {
                        rowNumber++;
                    }
                    else if (character == '\r' && (index + 1 >= text.Length || text[index + 1] != '\n'))
                    {
                        rowNumber++;
                    }
                }
            }
            else if (character == '"')
            {
                if (fieldStarted || field.Length > 0)
                {
                    throw new InvalidDataException($"Некорректные кавычки в строке {rowNumber} CSV.");
                }

                inQuotes = true;
                fieldStarted = true;
            }
            else if (character == delimiter)
            {
                this.AddCsvField(values, field);
                fieldStarted = false;
                afterQuote = false;
            }
            else if (character is '\r' or '\n')
            {
                this.AddCsvField(values, field);
                if (values.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    records.Add(new CsvRecord(recordNumber, values));
                    nonemptyRecordCount++;
                    if (nonemptyRecordCount > this._maxRows + 1)
                    {
                        throw new InvalidDataException("Файл превышает допустимое количество строк данных.");
                    }
                }

                values = [];
                field = new StringBuilder();
                fieldStarted = false;
                afterQuote = false;
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                rowNumber++;
                recordNumber = rowNumber;
            }
            else
            {
                if (afterQuote)
                {
                    throw new InvalidDataException($"После кавычки найден лишний символ в строке {rowNumber} CSV.");
                }

                field.Append(character);
            }

            if (field.Length > MaxCellLength)
            {
                throw new InvalidDataException("Значение ячейки превышает 10 000 символов.");
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException($"Не закрыты кавычки в строке {recordNumber} CSV.");
        }

        if (values.Count > 0 || field.Length > 0 || fieldStarted)
        {
            this.AddCsvField(values, field);
            if (values.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                records.Add(new CsvRecord(recordNumber, values));
            }
        }

        return records;
    }

    private char DetectDelimiter(string text)
    {
        var candidates = new[] { ';', ',', '\t' };
        var counts = candidates.ToDictionary(delimiter => delimiter, _ => 0);
        var inQuotes = false;
        var hasNonWhitespace = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (!inQuotes && character is '\r' or '\n')
            {
                if (hasNonWhitespace)
                {
                    break;
                }

                foreach (var delimiter in candidates)
                {
                    counts[delimiter] = 0;
                }

                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                hasNonWhitespace = false;
                continue;
            }

            hasNonWhitespace |= !char.IsWhiteSpace(character);
            if (character == '"')
            {
                if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (!inQuotes && counts.ContainsKey(character))
            {
                counts[character]++;
            }
        }

        return candidates.OrderByDescending(delimiter => counts[delimiter]).First();
    }

    private List<string> CreateHeaders(IReadOnlyList<string> values)
    {
        this.EnsureColumnLimit(values.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headers = new List<string>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var baseName = string.IsNullOrWhiteSpace(values[index]) ? $"Столбец {index + 1}" : values[index].Trim();
            var name = baseName;
            var suffix = 2;
            while (!used.Add(name))
            {
                name = $"{baseName} ({suffix++})";
            }

            headers.Add(name);
        }

        return headers;
    }

    private void ExtendHeaders(List<string> headers, int count)
    {
        while (headers.Count < count)
        {
            headers.Add($"Столбец {headers.Count + 1}");
        }
    }

    private List<string> PadValues(IReadOnlyList<string> values, int count)
    {
        var result = values.Take(count).ToList();
        while (result.Count < count)
        {
            result.Add(string.Empty);
        }

        return result;
    }

    private void AddCsvField(List<string> values, StringBuilder field)
    {
        this.EnsureCellLength(field.ToString());
        values.Add(field.ToString());
        field.Clear();
        if (values.Count > MaxColumns)
        {
            this.EnsureColumnLimit(values.Count);
        }
    }

    private int ParseColumnNumber(string reference)
    {
        var column = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter))
        {
            column = checked(column * 26 + char.ToUpperInvariant(character) - 'A' + 1);
        }

        if (column == 0)
        {
            throw new InvalidDataException("В книге Excel обнаружен некорректный адрес ячейки.");
        }

        return column;
    }

    private int ParseRowNumber(string? value, int fallback)
    {
        if (!int.TryParse(value, out var rowNumber) || rowNumber < 1)
        {
            return fallback;
        }

        return rowNumber;
    }

    private void EnsureCellLength(string value)
    {
        if (value.Contains('\0'))
        {
            throw new InvalidDataException("В ячейке найден нулевой символ. Проверьте кодировку CSV и сохраните файл в UTF-8.");
        }

        if (value.Length > MaxCellLength)
        {
            throw new InvalidDataException("Значение ячейки превышает 10 000 символов.");
        }
    }

    private void EnsureColumnLimit(int count)
    {
        if (count > MaxColumns)
        {
            throw new InvalidDataException("Файл содержит более 100 столбцов.");
        }
    }

    private void EnsureRowLimit(int count)
    {
        if (count > this._maxRows)
        {
            throw new InvalidDataException("Файл превышает допустимое количество строк данных.");
        }
    }

    private sealed record CsvRecord(int Number, List<string> Values);
}
