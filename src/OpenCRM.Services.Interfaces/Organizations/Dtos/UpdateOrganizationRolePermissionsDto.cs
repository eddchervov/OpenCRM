namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Данные для обновления разрешений роли организации. </summary>
public sealed class UpdateOrganizationRolePermissionsDto
{
    /// <summary> Код роли. </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary> Коды включаемых разрешений. </summary>
    public IReadOnlyCollection<string> EnabledPermissionCodes { get; init; } = [];
}
