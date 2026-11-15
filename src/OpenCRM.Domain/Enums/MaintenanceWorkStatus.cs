namespace OpenCRM.Domain.Enums;

/// <summary> Статус работы исполнителя по обслуживанию. </summary>
public enum MaintenanceWorkStatus
{
    /// <summary> Работа назначена исполнителю. </summary>
    Assigned = 1,
    /// <summary> Работа выполняется. </summary>
    InProgress = 2,
    /// <summary> Выполнение работы приостановлено. </summary>
    Paused = 3,
    /// <summary> Работа завершена. </summary>
    Completed = 4
}
