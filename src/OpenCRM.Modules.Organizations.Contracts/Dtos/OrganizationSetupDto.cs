namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> DTO состояния первоначальной настройки организации. </summary>
public sealed class OrganizationSetupDto
{
    /// <summary> Нужно ли автоматически открыть настройку. </summary>
    public bool ShouldAutoOpen { get; set; }

    /// <summary> Текущий шаг настройки. </summary>
    public int Step { get; set; }

    /// <summary> Номер версии состояния настройки. </summary>
    public int Revision { get; set; }

    /// <summary> Дата завершения настройки, если завершена. </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary> Может ли пользователь управлять настройкой. </summary>
    public bool CanManage { get; set; }

    /// <summary> Время начала рабочего дня. </summary>
    public TimeOnly? WorkdayStart { get; set; }

    /// <summary> Время окончания рабочего дня. </summary>
    public TimeOnly? WorkdayEnd { get; set; }

    /// <summary> Номера рабочих дней недели. </summary>
    public int[] WorkingDays { get; set; } = [];

    /// <summary> Недельное расписание работы организации. </summary>
    public List<OrganizationWorkingDayDto> WeeklySchedule { get; set; } = [];
}
