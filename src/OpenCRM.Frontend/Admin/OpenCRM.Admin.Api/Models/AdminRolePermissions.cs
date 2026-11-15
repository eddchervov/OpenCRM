namespace OpenCRM.Admin.Api.Models;

/// <summary> Разрешения конкретной роли. </summary>
public sealed class AdminRolePermissions
{
    /// <summary> Идентификатор роли. </summary>
    public string RoleId { get; init; } = string.Empty;

    /// <summary> Название роли. </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary> Признак заблокированной роли. </summary>
    public bool IsLocked { get; init; }

    /// <summary> Коды разрешений роли. </summary>
    public IReadOnlyList<string> PermissionCodes { get; init; } = [];
}
