namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO разрешений роли. </summary>
public sealed class AdminRolePermissionsDto
{
    /// <summary> Идентификатор роли. </summary>
    public string RoleId { get; init; } = string.Empty;

    /// <summary> Название роли. </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary> Признак заблокированной роли. </summary>
    public bool IsLocked { get; init; }

    /// <summary> Список кодов разрешений роли. </summary>
    public IReadOnlyList<string> PermissionCodes { get; init; } = [];
}
