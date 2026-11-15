namespace OpenCRM.Domain;

/// <summary> Связь роли администратора с кодом разрешения. </summary>
public class AdminRolePermission
{
    /// <summary> Идентификатор роли администратора. </summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary> Код разрешения для роли администратора. </summary>
    public string PermissionCode { get; set; } = string.Empty;

    /// <summary> Дата и время создания записи. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Идентификатор пользователя, создавшего запись. </summary>
    public string? CreatedByUserId { get; set; }
}
