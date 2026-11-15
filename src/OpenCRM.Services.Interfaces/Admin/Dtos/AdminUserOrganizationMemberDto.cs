namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO участника организации пользователя. </summary>
public sealed class AdminUserOrganizationMemberDto
{
    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Имя участника. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Название роли участника. </summary>
    public string RoleName { get; init; } = string.Empty;
}
