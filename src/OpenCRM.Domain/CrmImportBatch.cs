namespace OpenCRM.Domain;

/// <summary> Пакет импорта данных CRM. </summary>
public sealed class CrmImportBatch
{
    /// <summary> Идентификатор пакета импорта (GUID). </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации, к которой относится импорт. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Идентификатор пользователя, инициировавшего импорт. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Имя импортируемого файла. </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary> Отпечаток импортируемого файла. </summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary> JSON-представление строк импортируемых данных. </summary>
    public string RowsJson { get; set; } = string.Empty;

    /// <summary> JSON-представление результата импорта. </summary>
    public string? ResultJson { get; set; }

    /// <summary> Дата и время создания пакета импорта. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время выполнения импорта. </summary>
    public DateTime? ImportedAt { get; set; }
}
