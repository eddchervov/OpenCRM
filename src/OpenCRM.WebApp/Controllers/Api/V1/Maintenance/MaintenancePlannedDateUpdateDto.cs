namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> DTO для обновления запланированных дат обслуживания. </summary>
public sealed class MaintenancePlannedDateUpdateDto
{
    /// <summary> Запланированная дата обслуживания. </summary>
    public required string PlannedDate { get; set; }

    /// <summary> Необязательное время начала плановых работ. </summary>
    public string? PlannedStartTime { get; set; }

    /// <summary> Необязательное время окончания плановых работ. </summary>
    public string? PlannedEndTime { get; set; }

    /// <summary> Необязательная дата окончания плановых работ. </summary>
    public string? PlannedEndDate { get; set; }
}
