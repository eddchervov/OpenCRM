namespace OpenCRM.Domain;

/// <summary> Переопределение разрешения роли в организации. </summary>
public class OrganizationRolePermissionOverride
{
    /// <summary> Идентификатор переопределения. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, к которой относится переопределение. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Код роли, для которой задано переопределение. </summary>
    public string RoleCode { get; set; } = string.Empty;

    /// <summary> Код переопределяемого разрешения. </summary>
    public string PermissionCode { get; set; } = string.Empty;

    /// <summary> Признак включения переопределённого разрешения роли. </summary>
    public bool IsEnabled { get; set; }

    /// <summary> Дата и время создания переопределения разрешения. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время последнего изменения переопределения. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Идентификатор пользователя, изменившего переопределение. </summary>
    public string? UpdatedByUserId { get; set; }
}
