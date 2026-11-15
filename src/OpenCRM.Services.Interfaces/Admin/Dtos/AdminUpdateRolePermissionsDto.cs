namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO обновления разрешений роли. </summary>
public sealed class AdminUpdateRolePermissionsDto
{
    /// <summary> Список кодов разрешений роли. </summary>
    public IReadOnlyList<string> PermissionCodes { get; init; } = [];
}
