namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные о ходе выполнения работы. </summary>
public sealed class MaintenanceWorkProgressDto
{
    /// <summary> Идентификатор строки работы. </summary>
    public Guid DetailId { get; init; }

    /// <summary> Наименование работы. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Имя сотрудника-исполнителя. </summary>
    public string? EmployeeName { get; init; }

    /// <summary> Признак наличия назначенного сотрудника. </summary>
    public bool HasAssignedEmployee { get; init; }

    /// <summary> Имя администратора, завершившего работу за сотрудника. </summary>
    public string? CompletedBy { get; init; }

    /// <summary> Причина административного завершения работы. </summary>
    public string? CompletionReason { get; init; }

    /// <summary> Статус работы. </summary>
    public int Status { get; init; }

    /// <summary> Версия записи для контроля конкурентности. </summary>
    public uint Version { get; init; }

    /// <summary> Активное время работы. </summary>
    public TimeSpan ActiveTime { get; init; }

    /// <summary> Причина паузы, если задана. </summary>
    public string? PauseReason { get; init; }
}
