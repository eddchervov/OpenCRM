namespace OpenCRM.Domain;

/// <summary> Смена сотрудника организации. </summary>
public class OrganizationEmployeeShift
{
    /// <summary> Идентификатор смены. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, к которой относится смена. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Идентификатор сотрудника. </summary>
    public Guid EmployeeId { get; set; }

    /// <summary> Сотрудник, для которого задана смена. </summary>
    public OrganizationEmployee Employee { get; set; } = null!;

    /// <summary> Локальная дата начала смены. </summary>
    public DateOnly LocalStartDate { get; set; }

    /// <summary> Плановое начало смены в UTC. </summary>
    public DateTime PlanStartUtc { get; set; }

    /// <summary> Плановое окончание смены в UTC. </summary>
    public DateTime PlanEndUtc { get; set; }

    /// <summary> Плановая продолжительность перерыва в минутах. </summary>
    public int PlannedBreakMinutes { get; set; }

    /// <summary> Идентификатор часового пояса смены. </summary>
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary> Признак опубликованной смены. </summary>
    public bool IsPublished { get; set; }

    /// <summary> Признак отменённой смены. </summary>
    public bool IsCancelled { get; set; }

    /// <summary> Версия записи смены для управления параллельными изменениями. </summary>
    public uint Version { get; set; }

    /// <summary> Дата и время создания смены. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время последнего обновления смены. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

