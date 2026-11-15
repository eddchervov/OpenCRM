namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO участника организации. </summary>
public sealed class AdminOrganizationMemberDto
{
    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Имя участника. </summary>
    public string User { get; init; } = string.Empty;

    /// <summary> Email участника. </summary>
    public string? Email { get; init; }

    /// <summary> Телефон участника. </summary>
    public string? Phone { get; init; }

    /// <summary> Код роли участника организации. </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary> Дата присоединения участника к организации. </summary>
    public DateTime JoinedAt { get; init; }
}

