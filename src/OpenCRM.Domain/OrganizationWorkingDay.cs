namespace OpenCRM.Domain;

/// <summary> Рабочий день организации. </summary>
public class OrganizationWorkingDay
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> День недели рабочего дня. </summary>
    public int DayOfWeek { get; set; }

    /// <summary> Время начала рабочего дня. </summary>
    public TimeOnly StartTime { get; set; }

    /// <summary> Время окончания рабочего дня. </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary> Организация, к которой относится рабочий день. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Перерывы рабочего дня. </summary>
    public ICollection<OrganizationWorkingDayBreak> Breaks { get; set; } = new HashSet<OrganizationWorkingDayBreak>();
}
