namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Строка файла импорта CRM с данными клиента и техники. </summary>
public sealed class CrmImportRow
{
    /// <summary> Номер строки в файле импорта. </summary>
    public int Number { get; set; }

    /// <summary> Тип записи из файла. </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary> Имя клиента. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Телефон из строки импорта. </summary>
    public string? Phone { get; set; }

    /// <summary> Телефон клиента. </summary>
    public string? ClientPhone { get; set; }

    /// <summary> Марка техники. </summary>
    public string? Brand { get; set; }

    /// <summary> Модель техники. </summary>
    public string? Model { get; set; }

    /// <summary> VIN техники. </summary>
    public string? Vin { get; set; }

    /// <summary> Госномер техники. </summary>
    public string? Plate { get; set; }

    /// <summary> Код страны регистрации техники; пустое значение сохраняет российские правила. </summary>
    public string? RegistrationCountryCode { get; set; }

    /// <summary> Год выпуска техники. </summary>
    public int? Year { get; set; }

    /// <summary> Год выпуска в текстовом виде. </summary>
    public string? YearText { get; set; }

    /// <summary> Статус обработки строки. </summary>
    public string Status { get; set; } = "create";

    /// <summary> Сообщение о результате обработки строки. </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary> Код системного сообщения обработки строки. </summary>
    public string? MessageCode { get; set; }

    /// <summary> Параметры системного сообщения обработки строки. </summary>
    public Dictionary<string, string>? MessageParameters { get; set; }
}
