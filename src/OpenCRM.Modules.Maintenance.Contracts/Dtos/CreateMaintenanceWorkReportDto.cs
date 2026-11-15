namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для создания отчёта по работе обслуживания. </summary>
public sealed class CreateMaintenanceWorkReportDto
{
    /// <summary> Идентификатор команды создания отчёта. </summary>
    public Guid CommandId { get; set; }

    /// <summary> Тип отчёта. </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary> Текст отчёта. </summary>
    public string Text { get; set; } = string.Empty;
}
