namespace OpenCRM.Admin.Api.Models;

/// <summary> Набор разрешений административных ролей. </summary>
public sealed class AdminPermissions
{
    /// <summary> Разрешения системы. </summary>
    public IReadOnlyList<AdminPermission> Permissions { get; init; } = [];

    /// <summary> Роли с назначенными разрешениями. </summary>
    public IReadOnlyList<AdminRolePermissions> Roles { get; init; } = [];
}
