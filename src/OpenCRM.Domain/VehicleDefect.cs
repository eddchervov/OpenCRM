using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Неисправность техники. </summary>
public class VehicleDefect
{
    /// <summary> Идентификатор неисправности. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, к которой относится неисправность. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Техника, к которой относится неисправность. </summary>
    public Vehicle Vehicle { get; set; } = null!;

    /// <summary> Идентификатор пользователя, создавшего неисправность. </summary>
    public string UserId { get; set; } = null!;

    /// <summary> Пользователь, создавший неисправность. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Заголовок неисправности. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание неисправности. </summary>
    public string? Description { get; set; }

    /// <summary> Код серьёзности неисправности. </summary>
    public string SeverityCode { get; set; } = VehicleDefectSeverityCodes.Medium;

    /// <summary> Код статуса неисправности. </summary>
    public string StatusCode { get; set; } = VehicleDefectStatusCodes.New;

    /// <summary> Признак пометки неисправности удалённой. </summary>
    public bool IsDeleted { get; set; }

    /// <summary> Дата пометки неисправности удалённой. </summary>
    public DateTime? DeletionDate { get; set; }

    /// <summary> Дата и время создания неисправности. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время последнего обновления неисправности. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время закрытия неисправности. </summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary> Связи неисправности с записями об обслуживании. </summary>
    public ICollection<MaintenanceDefect> MaintenanceLinks { get; set; } = new HashSet<MaintenanceDefect>();

    /// <summary> Помечает неисправность удалённой с указанной датой. </summary>
    public void Delete(DateTime deletionDate)
    {
        IsDeleted = true;
        DeletionDate = deletionDate;
        UpdatedAt = deletionDate;
    }
}
