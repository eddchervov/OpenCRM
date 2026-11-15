namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO набора разрешений и ролей для админ-панели. </summary>
public sealed class AdminPermissionsDto
{
    /// <summary> Список разрешений. </summary>
    public IReadOnlyList<AdminPermissionDto> Permissions { get; init; } = [];

    /// <summary> Список ролей с их разрешениями. </summary>
    public IReadOnlyList<AdminRolePermissionsDto> Roles { get; init; } = [];
}
