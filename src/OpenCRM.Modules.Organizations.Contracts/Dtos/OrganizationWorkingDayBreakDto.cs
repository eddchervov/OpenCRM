namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> DTO перерыва в рабочем дне. </summary>
public sealed class OrganizationWorkingDayBreakDto
{
    /// <summary> Время начала перерыва. </summary>
    public TimeOnly StartTime { get; set; }

    /// <summary> Время окончания перерыва. </summary>
    public TimeOnly EndTime { get; set; }
}
