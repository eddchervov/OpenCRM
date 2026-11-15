using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Запись планировщика организации. </summary>
public sealed class OrganizationSchedulerItemDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid VehicleId { get; init; }

    /// <summary> Идентификатор задачи, если есть. </summary>
    public Guid? TaskId { get; init; }

    /// <summary> Идентификатор клиента организации, если есть. </summary>
    public Guid? OrganizationClientId { get; init; }

    /// <summary> Название клиента организации. </summary>
    public string? OrganizationClientName { get; init; }

    /// <summary> Идентификатор ответственного пользователя. </summary>
    public string? ResponsibleUserId { get; init; }

    /// <summary> Отображаемое имя ответственного. </summary>
    public string? ResponsibleDisplayName { get; init; }

    /// <summary> Идентификатор связанной сущности-источника. </summary>
    public Guid? SourceEntityId { get; init; }

    /// <summary> Отображаемое название транспортного средства. </summary>
    public string VehicleDisplayName { get; init; } = string.Empty;

    /// <summary> Дополнительная подпись транспортного средства. </summary>
    public string? VehicleSubtitle { get; init; }

    /// <summary> Заголовок записи. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Описание записи. </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary> Код статуса. </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary> Название статуса. </summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary> Код источника записи. </summary>
    public string SourceCode { get; init; } = string.Empty;

    /// <summary> Название источника записи. </summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary> Код типа задачи. </summary>
    public string TaskTypeCode { get; init; } = string.Empty;

    /// <summary> Название типа задачи. </summary>
    public string TaskTypeName { get; init; } = string.Empty;

    /// <summary> Код приоритета. </summary>
    public string PriorityCode { get; init; } = string.Empty;

    /// <summary> Название приоритета. </summary>
    public string PriorityName { get; init; } = string.Empty;

    /// <summary> Время начала. </summary>
    public DateTime? StartAt { get; init; }

    /// <summary> Время окончания. </summary>
    public DateTime? EndAt { get; init; }

    /// <summary> Идентификатор связанного дефекта, если есть. </summary>
    public Guid? LinkedDefectId { get; init; }

    /// <summary> Идентификатор вручную связанного обслуживания, если есть. </summary>
    public Guid? ManualLinkedMaintenanceId { get; init; }

    /// <summary> Код периодичности. </summary>
    public string RecurrenceCode { get; init; } = string.Empty;

    /// <summary> Название периодичности. </summary>
    public string RecurrenceName { get; init; } = string.Empty;

    /// <summary> Интервал периодичности. </summary>
    public int? RecurrenceInterval { get; init; }

    /// <summary> Дата окончания повторения. </summary>
    public DateTime? RecurrenceUntil { get; init; }

    /// <summary> Время напоминания. </summary>
    public DateTime? ReminderAt { get; init; }

    /// <summary> Время отправки напоминания. </summary>
    public DateTime? ReminderSentAt { get; init; }

    /// <summary> Срок по дате. </summary>
    public DateTime? DueDate { get; init; }

    /// <summary> Срок по пробегу. </summary>
    public int? DueMileage { get; init; }

    /// <summary> Текущий пробег транспортного средства. </summary>
    public int? CurrentMileage { get; init; }

    /// <summary> Единица измерения пробега. </summary>
    public VehicleMileageUnit MileageUnit { get; init; }

    /// <summary> Дополнительный текст записи. </summary>
    public string? MetaText { get; set; }

    /// <summary> Идентификатор связанного обслуживания, если есть. </summary>
    public Guid? LinkedMaintenanceId { get; set; }

    /// <summary> Ключ триггера записи. </summary>
    public string TriggerKey { get; init; } = string.Empty;

    /// <summary> Код процесса. </summary>
    public string ProcessCode { get; set; } = string.Empty;

    /// <summary> Код статуса workflow-процесса. </summary>
    public string WorkflowStatusCode { get; set; } = "new";

    /// <summary> Название статуса workflow-процесса. </summary>
    public string WorkflowStatusName { get; set; } = string.Empty;

    /// <summary> Код этапа workflow-процесса. </summary>
    public string WorkflowStageCode { get; set; } = string.Empty;

    /// <summary> Название этапа workflow-процесса. </summary>
    public string WorkflowStageName { get; set; } = string.Empty;

    /// <summary> Время, до которого workflow-процесс отложен. </summary>
    public DateTime? WorkflowDeferredUntil { get; set; }

    /// <summary> Признак возможности управления workflow-процессом. </summary>
    public bool CanManageWorkflow { get; set; }

    /// <summary> Адрес страницы с деталями записи. </summary>
    public string DetailUrl { get; init; } = string.Empty;

    /// <summary> Доступные действия для записи. </summary>
    public IReadOnlyList<OrganizationSchedulerActionDto> Actions { get; set; } = [];

    /// <summary> Адрес основного действия, если есть. </summary>
    public string? PrimaryActionUrl { get; init; }

    /// <summary> Код основного действия, если есть. </summary>
    public string? PrimaryActionCode { get; init; }

    /// <summary> Признак приглушения основного действия. </summary>
    public bool IsPrimaryActionMuted { get; init; }

    /// <summary> Порядок сортировки записи. </summary>
    public int SortRank { get; init; }
}
