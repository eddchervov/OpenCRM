namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Описание разрешения организации для отображения в списке. </summary>
public class OrganizationPermissionDefinitionDto
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
}
