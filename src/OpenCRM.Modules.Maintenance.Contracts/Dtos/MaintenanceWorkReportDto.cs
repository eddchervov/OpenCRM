namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные отчёта по работе обслуживания. </summary>
public sealed class MaintenanceWorkReportDto
{
    /// <summary> Идентификатор отчёта. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор обслуживания. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Идентификатор строки работы. </summary>
    public Guid DetailId { get; set; }

    /// <summary> Тип отчёта. </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary> Текст отчёта. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Имя сотрудника, создавшего отчёт. </summary>
    public string EmployeeName { get; set; } = string.Empty;

    /// <summary> Дата создания отчёта. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Решение по отчёту, если принято. </summary>
    public string? Decision { get; set; }

    /// <summary> Причина решения. </summary>
    public string? DecisionReason { get; set; }

    /// <summary> Дата принятия решения. </summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary> Согласованная цена работы. </summary>
    public decimal? ApprovedPrice { get; set; }

    /// <summary> Корректировка времени в минутах. </summary>
    public int? TimeAdjustmentMinutes { get; set; }
}
