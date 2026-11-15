using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary>
/// Ручная CRM-задача планировщика организации: звонок, работа, проверка, документ или другое действие.
/// </summary>
public class OrganizationSchedulerTask
{
    /// <summary>Идентификатор задачи.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Идентификатор организации, которой принадлежит задача.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Организация-владелец задачи.</summary>
    public Organization Organization { get; set; } = null!;

    /// <summary>Техника, с которой связана задача.</summary>
    public Guid? VehicleId { get; set; }

    /// <summary> Техника, к которой относится задача планировщика. </summary>
    public Vehicle? Vehicle { get; set; }

    /// <summary>Клиент организации, с которым связана задача.</summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Клиент организации, связанный с задачей планировщика. </summary>
    public OrganizationClient? OrganizationClient { get; set; }

    /// <summary>Ответственный сотрудник организации.</summary>
    public string? ResponsibleUserId { get; set; }

    /// <summary> Пользователь, ответственный за выполнение задачи. </summary>
    public ApplicationUser? ResponsibleUser { get; set; }

    /// <summary>Пользователь, создавший задачу.</summary>
    public string CreatedByUserId { get; set; } = string.Empty;

    /// <summary> Пользователь, создавший задачу планировщика. </summary>
    public ApplicationUser CreatedByUser { get; set; } = null!;

    /// <summary>Короткий заголовок задачи.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Комментарий или детали задачи.</summary>
    public string? Comment { get; set; }

    /// <summary>Тип задачи: обслуживание, звонок, проверка, дефект, документ или другое.</summary>
    public string TypeCode { get; set; } = OrganizationSchedulerTaskTypeCodes.Other;

    /// <summary>Приоритет задачи.</summary>
    public string PriorityCode { get; set; } = OrganizationSchedulerTaskPriorityCodes.Normal;

    /// <summary>Текущий статус задачи.</summary>
    public string StatusCode { get; set; } = OrganizationSchedulerTaskStatusCodes.New;

    /// <summary>Код рабочего процесса под тип организации.</summary>
    public string ProcessCode { get; set; } = string.Empty;

    /// <summary>Код этапа workflow внутри процесса организации.</summary>
    public string WorkflowStageCode { get; set; } = string.Empty;

    /// <summary>Связанная неисправность, если задача создана вокруг дефекта.</summary>
    public Guid? LinkedDefectId { get; set; }

    /// <summary> Дефект техники, связанный с задачей планировщика. </summary>
    public VehicleDefect? LinkedDefect { get; set; }

    /// <summary>Связанное обслуживание, если задача ведет к конкретной работе.</summary>
    public Guid? LinkedMaintenanceId { get; set; }

    /// <summary> Запись обслуживания, связанная с задачей планировщика. </summary>
    public Maintenance? LinkedMaintenance { get; set; }

    /// <summary>Плановое начало задачи.</summary>
    public DateTime? StartAt { get; set; }

    /// <summary>Плановое окончание задачи.</summary>
    public DateTime? EndAt { get; set; }

    /// <summary>Код повторения задачи.</summary>
    public string RecurrenceCode { get; set; } = OrganizationSchedulerTaskRecurrenceCodes.None;

    /// <summary>Шаблон повторяющейся задачи, если эта запись создана автоматически как экземпляр повтора.</summary>
    public Guid? RecurrenceParentTaskId { get; set; }

    /// <summary> Родительская задача для повторяющейся задачи планировщика. </summary>
    public OrganizationSchedulerTask? RecurrenceParentTask { get; set; }

    /// <summary>Автоматически созданные будущие экземпляры этой повторяющейся задачи.</summary>
    public ICollection<OrganizationSchedulerTask> RecurrenceChildren { get; set; } = [];

    /// <summary>Интервал повторения, если выбран повторяемый сценарий.</summary>
    public int? RecurrenceInterval { get; set; }

    /// <summary>Дата окончания повторений.</summary>
    public DateTime? RecurrenceUntil { get; set; }

    /// <summary>Дата, до которой уже созданы будущие экземпляры повторяющейся задачи.</summary>
    public DateTime? RecurrenceGeneratedUntil { get; set; }

    /// <summary>Дата и время напоминания ответственному.</summary>
    public DateTime? ReminderAt { get; set; }

    /// <summary>Дата фактической отправки напоминания.</summary>
    public DateTime? ReminderSentAt { get; set; }

    /// <summary>Признак мягкого удаления задачи.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Дата мягкого удаления задачи.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Дата создания задачи.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Дата последнего изменения задачи.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Дата закрытия задачи.</summary>
    public DateTime? ClosedAt { get; set; }
}
