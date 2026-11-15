namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO приглашения в обзоре пользователя. </summary>
public sealed class AdminUserOverviewInvitationDto
{
    /// <summary> Идентификатор приглашения. </summary>
    public Guid Id { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Название организации. </summary>
    public string OrganizationName { get; init; } = string.Empty;

    /// <summary> Email приглашённого. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Код роли приглашения. </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary> Название роли приглашения. </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary> Код статуса приглашения. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Дата создания приглашения. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Дата принятия приглашения. </summary>
    public DateTime? AcceptedAt { get; init; }

    /// <summary> Дата отзыва приглашения. </summary>
    public DateTime? RevokedAt { get; init; }

    /// <summary> Идентификатор пригласившего пользователя. </summary>
    public string InvitedBy { get; init; } = string.Empty;
}
