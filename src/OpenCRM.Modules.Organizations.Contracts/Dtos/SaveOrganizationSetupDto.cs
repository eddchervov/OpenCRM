using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Данные для сохранения шага настройки организации. </summary>
public sealed class SaveOrganizationSetupDto
{
    /// <summary> Шаг настройки. </summary>
    [Range(0, 3)]
    public int Step { get; set; }

    /// <summary> Номер версии для проверки параллельных изменений. </summary>
    public int Revision { get; set; }

    /// <summary> Признак пропуска шага. </summary>
    public bool Skip { get; set; }

    /// <summary> Элементы настройки: услуги и рабочие посты. </summary>
    [MaxLength(100)]
    public List<OrganizationSetupItemDto> Items { get; set; } = [];

    /// <summary> Время начала рабочего дня. </summary>
    public TimeOnly? WorkdayStart { get; set; }

    /// <summary> Время окончания рабочего дня. </summary>
    public TimeOnly? WorkdayEnd { get; set; }

    /// <summary> Номера рабочих дней недели. </summary>
    public int[] WorkingDays { get; set; } = [];

    /// <summary> Недельное расписание, если задано. </summary>
    public List<OrganizationWorkingDayDto>? WeeklySchedule { get; set; }
}
