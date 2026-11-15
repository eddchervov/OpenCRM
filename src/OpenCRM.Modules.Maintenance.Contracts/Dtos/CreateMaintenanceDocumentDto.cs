namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для регистрации сформированного документа обслуживания. </summary>
public class CreateMaintenanceDocumentDto
{
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

    /// <summary> Путь файла в хранилище. </summary>
    public string StoragePath { get; set; } = default!;

    /// <summary> MIME-тип файла. </summary>
    public string ContentType { get; set; } = default!;

    /// <summary> Размер файла в байтах. </summary>
    public long Size { get; set; }
}
