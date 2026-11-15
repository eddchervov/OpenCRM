namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для обновления плановых дат обслуживания. </summary>
public sealed class UpdateMaintenancePlannedDateDto
{
    /// <summary> Новая плановая дата обслуживания. </summary>
    public DateTime PlannedDate { get; set; }

    /// <summary> Новая плановая дата окончания. </summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановое время окончания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }
}
