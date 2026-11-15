namespace OpenCRM.Services.Interfaces.Documents.Dtos;

/// <summary> Возможный исход экспорта документа. </summary>
public enum DocumentExportOutcome
{
    /// <summary> Файл документа сформирован. </summary>
    File,

    /// <summary> Отказано в доступе к документу. </summary>
    AccessDenied,

    /// <summary> Документ не найден. </summary>
    NotFound,

    /// <summary> Некорректный запрос экспорта. </summary>
    InvalidRequest
}
