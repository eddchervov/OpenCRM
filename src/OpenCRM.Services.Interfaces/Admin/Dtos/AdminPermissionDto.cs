namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO разрешения для админ-панели. </summary>
public sealed class AdminPermissionDto
{
    /// <summary> Код разрешения. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Название разрешения. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Группа разрешений. </summary>
    public string Group { get; init; } = string.Empty;
}
