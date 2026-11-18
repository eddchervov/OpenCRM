namespace OpenCRM.Modules.Maintenance.Contracts.Imports;

/// <summary>Отдельная причина проверки строки импорта.</summary>
public sealed class ImportRowMessageDetail
{
    /// <summary>Сообщение для старых клиентов и неизвестного кода.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Стабильный код перевода системного сообщения.</summary>
    public string? MessageCode { get; set; }

    /// <summary>Параметры системного сообщения для перевода.</summary>
    public Dictionary<string, string>? MessageParameters { get; set; }
}
