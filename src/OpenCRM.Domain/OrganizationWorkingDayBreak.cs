namespace OpenCRM.Domain;

/// <summary> Перерыв в рабочем дне организации. </summary>
public class OrganizationWorkingDayBreak
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> День недели, к которому относится перерыв. </summary>
    public int DayOfWeek { get; set; }

    /// <summary> Время начала перерыва. </summary>
    public TimeOnly StartTime { get; set; }

    /// <summary> Время окончания перерыва. </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary> Рабочий день, содержащий перерыв. </summary>
    public OrganizationWorkingDay WorkingDay { get; set; } = null!;
}
