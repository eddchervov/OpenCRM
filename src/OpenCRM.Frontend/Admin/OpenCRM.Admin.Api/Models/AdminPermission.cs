namespace OpenCRM.Admin.Api.Models;

/// <summary> Разрешение административной панели. </summary>
public sealed class AdminPermission
{
    /// <summary> Код разрешения. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Название разрешения. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Группа разрешения. </summary>
    public string Group { get; init; } = string.Empty;
}
