namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Предпросмотр импорта CRM перед подтверждением. </summary>
public sealed class CrmImportPreview
{
    /// <summary> Идентификатор пакета импорта. </summary>
    public Guid Id { get; set; }

    /// <summary> Имя загруженного файла. </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary> Признак того, что файл уже импортировался. </summary>
    public bool AlreadyImported { get; set; }

    /// <summary> Строки импорта из файла. </summary>
    public IReadOnlyList<CrmImportRow> Rows { get; set; } = [];

    /// <summary> Сводка по строкам импорта. </summary>
    public CrmImportSummary Summary { get; set; } = new();
}
