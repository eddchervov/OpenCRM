namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Результат подтверждения импорта CRM. </summary>
public sealed class CrmImportResult
{
    /// <summary> Количество созданных клиентов. </summary>
    public int CreatedClients { get; set; }

    /// <summary> Количество созданных записей техники. </summary>
    public int CreatedVehicles { get; set; }

    /// <summary> Количество пропущенных дубликатов. </summary>
    public int SkippedDuplicates { get; set; }

    /// <summary> Количество ошибок при импорте. </summary>
    public int Errors { get; set; }

    /// <summary> Идентификатор отчёта об импорте. </summary>
    public Guid ReportId { get; set; }
}
