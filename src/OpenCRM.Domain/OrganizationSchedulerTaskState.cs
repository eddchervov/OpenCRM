using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Состояние автоматической задачи планировщика организации. </summary>
public class OrganizationSchedulerTaskState
{
    /// <summary> Идентификатор состояния задачи планировщика. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, к которой относится состояние задачи. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Техника, к которой относится состояние задачи. </summary>
    public Vehicle Vehicle { get; set; } = null!;

    /// <summary> Код источника, создавшего состояние задачи. </summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary> Идентификатор сущности-источника. </summary>
    public Guid? SourceEntityId { get; set; }

    /// <summary> Ключ триггера, создавшего состояние задачи. </summary>
    public string TriggerKey { get; set; } = string.Empty;

    /// <summary> Код статуса состояния задачи. </summary>
    public string StatusCode { get; set; } = OrganizationSchedulerTaskStatusCodes.New;

    /// <summary> Код процесса обработки состояния задачи. </summary>
    public string ProcessCode { get; set; } = OrganizationTypeCodes.ServiceStation;

    /// <summary> Код этапа workflow состояния задачи. </summary>
    public string WorkflowStageCode { get; set; } = "intake";

    /// <summary> Время, до которого отложена обработка состояния задачи. </summary>
    public DateTime? DeferredUntil { get; set; }

    /// <summary> Идентификатор записи обслуживания. </summary>
    public Guid? MaintenanceId { get; set; }

    /// <summary> Запись обслуживания, связанная с состоянием задачи. </summary>
    public Maintenance? Maintenance { get; set; }

    /// <summary> Идентификатор пользователя, связанного с состоянием задачи. </summary>
    public string UserId { get; set; } = null!;

    /// <summary> Пользователь, связанный с состоянием задачи. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Дата и время создания состояния задачи. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время последнего изменения состояния задачи. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время закрытия состояния задачи. </summary>
    public DateTime? ClosedAt { get; set; }
}
