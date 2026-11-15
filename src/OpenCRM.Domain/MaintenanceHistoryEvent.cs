using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Событие истории обслуживания. </summary>
public class MaintenanceHistoryEvent
{
    /// <summary> Идентификатор события. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор обслуживания. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Обслуживание. </summary>
    public Maintenance Maintenance { get; set; } = default!;

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Идентификатор пользователя, выполнившего действие. </summary>
    public string UserId { get; set; } = default!;

    /// <summary> Пользователь, выполнивший действие. </summary>
    public ApplicationUser User { get; set; } = default!;

    /// <summary> Отображаемое имя пользователя на момент события. </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary> Тип события. </summary>
    public string EventType { get; set; } = default!;

    /// <summary> Заголовок события. </summary>
    public string Title { get; set; } = default!;

    /// <summary> Подробности события. </summary>
    public string? Description { get; set; }

    /// <summary> Машиночитаемые детали события. </summary>
    public string? PayloadJson { get; set; }

    /// <summary> Дата создания события. </summary>
    public DateTime CreatedAt { get; set; }
}
