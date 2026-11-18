using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports.Crm;

/// <summary> Разбор файлов импорта CRM в форматах CSV, JSON, XML и XLSX. </summary>
internal static class CrmImportParser
{
    /// <summary> Разбирает содержимое файла и возвращает строки импорта. </summary>
    public static IReadOnlyList<CrmImportRow> Parse(byte[] content, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var rows = extension switch
        {
            ".csv" => ParseCsv(content),
            ".xlsx" => ParseXlsx(content),
            ".json" => ParseJson(content),
            ".xml" => ParseXml(content),
            _ => throw new InvalidDataException("Поддерживаются файлы CSV, XLSX, JSON и XML.")
        };

        if (rows.Count == 0)
        {
            throw new InvalidDataException("Файл не содержит клиентов или автомобилей.");
        }

        if (rows.Count > 5000)
        {
            throw new InvalidDataException("За один раз можно проверить не более 5000 строк.");
        }

        return rows;
    }

    /// <summary> Разбирает содержимое CSV-файла в строки импорта. </summary>
    private static List<CrmImportRow> ParseCsv(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var firstLine = reader.ReadLine() ?? string.Empty;
        stream.Position = 0;
        reader.DiscardBufferedData();

        using var parser = new TextFieldParser(reader);
        parser.SetDelimiters(firstLine.Count(character => character == ';') > firstLine.Count(character => character == ',') ? ";" : ",");
        parser.HasFieldsEnclosedInQuotes = true;
        var headers = parser.ReadFields() ?? [];
        var rows = new List<CrmImportRow>();

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(CreateRow(ToValues(headers, fields), rows.Count + 1, null));
        }

        return rows;
    }

    /// <summary> Разбирает содержимое JSON-файла в строки импорта. </summary>
    private static List<CrmImportRow> ParseJson(byte[] content)
    {
        using var document = JsonDocument.Parse(content);
        var rows = new List<CrmImportRow>();
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            AddJsonArray(root, null, rows);
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    AddJsonArray(property.Value, property.Name, rows);
                }
            }
        }

        return rows;
    }

    /// <summary> Добавляет строки импорта из JSON-массива. </summary>
    private static void AddJsonArray(JsonElement array, string? type, List<CrmImportRow> rows)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var values = item.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            rows.Add(CreateRow(values, rows.Count + 1, type));
        }
    }

    /// <summary> Разбирает содержимое XML-файла в строки импорта. </summary>
    private static List<CrmImportRow> ParseXml(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 10_000_000
        });
        var document = XDocument.Load(reader);
        var rows = new List<CrmImportRow>();

        foreach (var element in document.Descendants())
        {
            var type = element.Name.LocalName;
            if (!IsRecordName(type))
            {
                continue;
            }

            var values = element.Attributes().ToDictionary(
                attribute => attribute.Name.LocalName,
                attribute => attribute.Value,
                StringComparer.OrdinalIgnoreCase);
            foreach (var child in element.Elements())
            {
                if (!child.HasElements)
                {
                    values[child.Name.LocalName] = child.Value;
                }
            }

            rows.Add(CreateRow(values, rows.Count + 1, type));
        }

        return rows;
    }

    /// <summary> Разбирает содержимое XLSX-файла в строки импорта. </summary>
    private static List<CrmImportRow> ParseXlsx(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Any(entry => entry.Length > 20 * 1024 * 1024) ||
            archive.Entries.Sum(entry => entry.Length) > 30 * 1024 * 1024)
        {
            throw new InvalidDataException("Распакованный файл Excel слишком велик.");
        }
        var sharedStrings = new List<string>();
        var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry != null)
        {
            using var sharedStream = sharedEntry.Open();
            var sharedDocument = LoadXml(sharedStream);
            sharedStrings = sharedDocument.Descendants()
                .Where(element => element.Name.LocalName == "si")
                .Select(element => string.Concat(element.Descendants()
                    .Where(child => child.Name.LocalName == "t")
                    .Select(child => child.Value)))
                .ToList();
        }

        var rows = new List<CrmImportRow>();
        foreach (var entry in archive.Entries.Where(item =>
                     item.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                     item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var sheetStream = entry.Open();
            var sheet = LoadXml(sheetStream);
            var sheetRows = sheet.Descendants().Where(element => element.Name.LocalName == "row").ToList();
            if (sheetRows.Count < 2)
            {
                continue;
            }

            var headers = GetCells(sheetRows[0], sharedStrings);
            foreach (var sheetRow in sheetRows.Skip(1))
            {
                var cells = GetCells(sheetRow, sharedStrings);
                if (cells.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var values = headers.ToDictionary(
                    header => header.Value,
                    header => cells.GetValueOrDefault(header.Key, string.Empty),
                    StringComparer.OrdinalIgnoreCase);
                rows.Add(CreateRow(values, rows.Count + 1, null));
            }
        }

        return rows;
    }

    /// <summary> Загружает XML-документ из потока. </summary>
    private static XDocument LoadXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 20_000_000
        });

        return XDocument.Load(reader);
    }

    /// <summary> Собирает ячейки строки XLSX с учётом общих строковых значений. </summary>
    private static Dictionary<int, string> GetCells(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<int, string>();
        foreach (var cell in row.Elements().Where(element => element.Name.LocalName == "c"))
        {
            var reference = cell.Attribute("r")?.Value ?? string.Empty;
            var column = 0;
            foreach (var character in reference.TakeWhile(char.IsLetter))
            {
                column = column * 26 + char.ToUpperInvariant(character) - 'A' + 1;
            }

            var value = cell.Descendants().FirstOrDefault(element => element.Name.LocalName == "v")?.Value
                ?? string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));
            if (cell.Attribute("t")?.Value == "s" && int.TryParse(value, out var index) && index < sharedStrings.Count)
            {
                value = sharedStrings[index];
            }

            cells[column] = value;
        }

        return cells;
    }

    /// <summary> Сопоставляет заголовки и значения полей строки. </summary>
    private static Dictionary<string, string> ToValues(string[] headers, string[] fields)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            values[headers[index].Trim()] = index < fields.Length ? fields[index].Trim() : string.Empty;
        }

        return values;
    }

    /// <summary> Создаёт строку импорта из словаря значений. </summary>
    private static CrmImportRow CreateRow(Dictionary<string, string> values, int number, string? sourceType)
    {
        var type = Get(values, "type", "тип", "entity", "сущность") ?? sourceType;
        var isVehicle = type is not null && (type.Contains("vehicle", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("car", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("авто", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("машин", StringComparison.OrdinalIgnoreCase));
        if (type is null)
        {
            isVehicle = Get(values, "vin", "вин", "plate", "госномер", "brand", "марка") != null;
        }

        return new CrmImportRow
        {
            Number = number,
            Type = isVehicle ? "vehicle" : "client",
            Name = Get(values, "name", "имя", "фио", "клиент", "название") ?? string.Empty,
            Phone = Get(values, "phone", "телефон", "номер телефона"),
            ClientPhone = Get(values, "clientPhone", "client_phone", "телефон клиента"),
            Brand = Get(values, "brand", "марка"),
            Model = Get(values, "model", "модель"),
            Vin = Get(values, "vin", "вин"),
            Plate = Get(values, "plate", "registrationPlate", "госномер", "гос номер"),
            RegistrationCountryCode = Get(values, "registrationCountryCode", "registration_country_code", "registrationCountry",
                "registration_country", "registration country code", "countryCode", "country_code", "country code", "country",
                "страна регистрации", "код страны", "страна"),
            Year = int.TryParse(Get(values, "year", "год", "год выпуска"), out var year) ? year : null,
            YearText = Get(values, "year", "год", "год выпуска")
        };
    }

    /// <summary> Возвращает первое найденное значение по возможным именам поля. </summary>
    private static string? Get(Dictionary<string, string> values, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary> Определяет, является ли имя узла записью импорта. </summary>
    private static bool IsRecordName(string name) => name.Equals("client", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("customer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("vehicle", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("car", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("row", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("клиент", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("автомобиль", StringComparison.OrdinalIgnoreCase);
}
