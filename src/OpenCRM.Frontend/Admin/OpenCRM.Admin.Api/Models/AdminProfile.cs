namespace OpenCRM.Admin.Api.Models;

/// <summary> Профиль администратора системы. </summary>
public sealed class AdminProfile
{
    /// <summary> Идентификатор профиля. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Email администратора. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Роли администратора. </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary> Права администратора. </summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary> Признак системного профиля. </summary>
    public bool IsSystem { get; init; }

    /// <summary> Признак включённого коммерческого режима платформы. </summary>
    public bool CommercialEnabled { get; init; }
}
