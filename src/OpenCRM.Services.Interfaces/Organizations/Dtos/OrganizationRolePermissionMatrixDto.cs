namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Матрица разрешений роли организации. </summary>
public sealed class OrganizationRolePermissionMatrixDto
{
    /// <summary> Код роли. </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary> Название роли. </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary> Признак возможности редактирования прав роли. </summary>
    public bool CanEdit { get; init; }

    /// <summary> Список разрешений роли. </summary>
    public IReadOnlyList<OrganizationRolePermissionDto> Permissions { get; init; } = [];
}
