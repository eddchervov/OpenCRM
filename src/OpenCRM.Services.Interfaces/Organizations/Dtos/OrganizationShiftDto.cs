namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Смена сотрудника организации. </summary>
public sealed class OrganizationShiftDto
{
    /// <summary> Идентификатор смены. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор сотрудника. </summary>
    public Guid EmployeeId { get; set; }

    /// <summary> Имя сотрудника. </summary>
    public string EmployeeName { get; set; } = string.Empty;

    /// <summary> Локальная дата начала смены. </summary>
    public DateOnly LocalStartDate { get; set; }

    /// <summary> Идентификатор часового пояса смены. </summary>
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary> Локальное время начала смены. </summary>
    public string StartLocal { get; set; } = string.Empty;

    /// <summary> Локальное время окончания смены. </summary>
    public string EndLocal { get; set; } = string.Empty;

    /// <summary> Плановое время начала смены в UTC. </summary>
    public DateTime PlanStartUtc { get; set; }

    /// <summary> Плановое время окончания смены в UTC. </summary>
    public DateTime PlanEndUtc { get; set; }

    /// <summary> Плановая продолжительность перерыва в минутах. </summary>
    public int PlannedBreakMinutes { get; set; }

    /// <summary> Признак опубликованной смены. </summary>
    public bool IsPublished { get; set; }

    /// <summary> Признак отменённой смены. </summary>
    public bool IsCancelled { get; set; }

    /// <summary> Версия смены для оптимистичной блокировки. </summary>
    public uint Version { get; set; }
}

/// <summary> Назначение смены на обслуживание. </summary>
public sealed class OrganizationShiftAssignmentDto
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Идентификатор сотрудника. </summary>
    public Guid EmployeeId { get; set; }

    /// <summary> Название транспортного средства. </summary>
    public string VehicleName { get; set; } = string.Empty;

    /// <summary> Локальная дата начала смены. </summary>
    public DateOnly LocalStartDate { get; set; }

    /// <summary> Количество услуг в назначении. </summary>
    public int ServiceCount { get; set; }

    /// <summary> Отрезки работы в назначении. </summary>
    public IReadOnlyList<OrganizationShiftAssignmentSegmentDto> Segments { get; set; } = [];
}

/// <summary> Отрезок работы в рамках назначения смены. </summary>
public sealed class OrganizationShiftAssignmentSegmentDto
{
    /// <summary> Локальное время начала отрезка. </summary>
    public string StartLocal { get; set; } = string.Empty;

    /// <summary> Локальное время окончания отрезка. </summary>
    public string EndLocal { get; set; } = string.Empty;

    /// <summary> Признак работы вне смены. </summary>
    public bool IsOutsideShift { get; set; }
}

/// <summary> Данные недели смен организации. </summary>
public sealed class OrganizationShiftWeekDto
{
    /// <summary> Идентификатор часового пояса, если задан. </summary>
    public string? TimeZoneId { get; set; }

    /// <summary> Признак возможности управления сменами. </summary>
    public bool CanManage { get; set; }

    /// <summary> Признак возможности просмотра назначений. </summary>
    public bool CanViewAssignments { get; set; }

    /// <summary> Список сотрудников для выбора. </summary>
    public IReadOnlyList<OrganizationEmployeeOptionDto> Employees { get; set; } = [];

    /// <summary> Список смен за неделю. </summary>
    public IReadOnlyList<OrganizationShiftDto> Shifts { get; set; } = [];

    /// <summary> Список назначений на обслуживания за неделю. </summary>
    public IReadOnlyList<OrganizationShiftAssignmentDto> Assignments { get; set; } = [];
}

/// <summary> Вариант выбора сотрудника организации. </summary>
public sealed class OrganizationEmployeeOptionDto
{
    /// <summary> Признак активного сотрудника. </summary>
    public bool IsActive { get; set; }

    /// <summary> Идентификатор сотрудника. </summary>
    public Guid Id { get; set; }

    /// <summary> Имя сотрудника. </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary> Данные для сохранения смены организации. </summary>
public sealed class SaveOrganizationShiftDto
{
    /// <summary> Идентификатор сотрудника. </summary>
    public Guid EmployeeId { get; set; }

    /// <summary> Локальное время начала смены. </summary>
    public DateTime StartLocal { get; set; }

    /// <summary> Локальное время окончания смены. </summary>
    public DateTime EndLocal { get; set; }

    /// <summary> Плановая продолжительность перерыва в минутах. </summary>
    public int PlannedBreakMinutes { get; set; }

    /// <summary> Версия смены для оптимистичной блокировки, если есть. </summary>
    public uint? Version { get; set; }

    /// <summary> Причина изменения смены, если указана. </summary>
    public string? Reason { get; set; }
}

