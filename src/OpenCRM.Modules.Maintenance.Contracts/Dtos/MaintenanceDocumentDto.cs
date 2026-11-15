namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Сформированный документ обслуживания. </summary>
public class MaintenanceDocumentDto
{
    /// <summary> Идентификатор документа. </summary>
    public Guid Id { get; set; }

    /// <summary> Тип документа. </summary>
    public string Kind { get; set; } = default!;

    /// <summary> Формат документа. </summary>
    public string Format { get; set; } = default!;

    /// <summary> Признак включения фотографий. </summary>
    public bool IncludePhotos { get; set; }

    /// <summary> Заголовок документа. </summary>
    public string Title { get; set; } = default!;

    /// <summary> Имя файла. </summary>
    public string FileName { get; set; } = default!;

    /// <summary> MIME-тип файла. </summary>
    public string ContentType { get; set; } = default!;

    /// <summary> Размер файла в байтах. </summary>
    public long Size { get; set; }

    /// <summary> Версия документа. </summary>
    public int Version { get; set; }

    /// <summary> Пользователь, сформировавший документ. </summary>
    public string? CreatedByDisplayName { get; set; }

    /// <summary> Дата формирования. </summary>
    public DateTime CreatedAt { get; set; }
}
