namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> DTO рабочего дня недельного расписания. </summary>
public sealed class OrganizationWorkingDayDto
{
    /// <summary> Номер дня недели. </summary>
    public int DayOfWeek { get; set; }

    /// <summary> Признак рабочего дня. </summary>
    public bool IsWorking { get; set; }

    /// <summary> Время начала рабочего дня, если задано. </summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary> Время окончания рабочего дня, если задано. </summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary> Перерывы в течение дня. </summary>
    public List<OrganizationWorkingDayBreakDto> Breaks { get; set; } = [];
}
