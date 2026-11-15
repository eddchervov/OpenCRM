namespace OpenCRM.Admin.Api.Models;

/// <summary> Приглашение пользователя в организацию. </summary>
public sealed class AdminUserOverviewInvitation
{
    /// <summary> Идентификатор приглашения. </summary>
    public Guid Id { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Название организации. </summary>
    public string OrganizationName { get; init; } = string.Empty;

    /// <summary> Email приглашённого пользователя. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Код роли приглашения. </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary> Название роли приглашения. </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary> Код статуса приглашения. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Время создания приглашения. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Время принятия приглашения. </summary>
    public DateTime? AcceptedAt { get; init; }

    /// <summary> Время отзыва приглашения. </summary>
    public DateTime? RevokedAt { get; init; }

    /// <summary> Кто пригласил пользователя. </summary>
    public string InvitedBy { get; init; } = string.Empty;
}
