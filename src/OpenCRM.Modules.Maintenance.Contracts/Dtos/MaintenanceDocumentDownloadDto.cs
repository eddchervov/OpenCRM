namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные сохраненного документа обслуживания для скачивания. </summary>
public class MaintenanceDocumentDownloadDto
{
    /// <summary> Идентификатор документа. </summary>
    public Guid Id { get; set; }

    /// <summary> Имя файла. </summary>
    public string FileName { get; set; } = default!;

    /// <summary> Путь файла в хранилище. </summary>
    public string StoragePath { get; set; } = default!;

    /// <summary> MIME-тип файла. </summary>
    public string ContentType { get; set; } = default!;
}
