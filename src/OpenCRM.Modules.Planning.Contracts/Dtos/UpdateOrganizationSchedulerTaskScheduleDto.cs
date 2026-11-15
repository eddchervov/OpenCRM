namespace OpenCRM.Modules.Planning.Contracts.Dtos;

/// <summary> Данные для обновления расписания задачи планировщика. </summary>
public sealed class UpdateOrganizationSchedulerTaskScheduleDto
{
    /// <summary> Время начала задачи, если задано. </summary>
    public DateTime? StartAt { get; set; }

    /// <summary> Время окончания задачи, если задано. </summary>
    public DateTime? EndAt { get; set; }
}
