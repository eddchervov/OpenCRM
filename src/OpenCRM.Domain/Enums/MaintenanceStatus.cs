namespace OpenCRM.Domain.Enums;

/// <summary> Состояние обслуживания. </summary>
public enum MaintenanceStatus
{
    /// <summary> Обслуживание запланировано. </summary>
    Planned = 1,
    /// <summary> Обслуживание выполняется. </summary>
    InProgress = 2,
    /// <summary> Обслуживание завершено. </summary>
    Completed = 3,
    /// <summary> Обслуживание отменено. </summary>
    Cancelled = 4
}
