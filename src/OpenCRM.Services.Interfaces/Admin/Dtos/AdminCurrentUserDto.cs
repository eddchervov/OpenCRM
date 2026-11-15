namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO текущего администратора. </summary>
public sealed class AdminCurrentUserDto
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Роли пользователя. </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary> Разрешения пользователя. </summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary> Признак системной учётной записи. </summary>
    public bool IsSystem { get; init; }

    /// <summary> Признак включённого коммерческого модуля установки. </summary>
    public bool CommercialEnabled { get; init; }
}
