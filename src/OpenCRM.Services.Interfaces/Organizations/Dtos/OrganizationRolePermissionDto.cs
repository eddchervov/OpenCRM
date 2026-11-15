namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Разрешение роли в матрице прав организации. </summary>
public sealed class OrganizationRolePermissionDto
{
    /// <summary> Код разрешения. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Название разрешения. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Название группы разрешений. </summary>
    public string GroupName { get; init; } = string.Empty;

    /// <summary> Описание разрешения. </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary> Признак разрешения на изменение данных. </summary>
    public bool IsWritePermission { get; init; }

    /// <summary> Признак включения разрешения по умолчанию. </summary>
    public bool IsDefaultEnabled { get; init; }

    /// <summary> Признак включения разрешения для роли. </summary>
    public bool IsEnabled { get; init; }
}
