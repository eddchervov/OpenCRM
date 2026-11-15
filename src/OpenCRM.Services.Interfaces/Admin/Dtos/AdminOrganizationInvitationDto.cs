namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO приглашения в организацию. </summary>
public sealed class AdminOrganizationInvitationDto
{
    /// <summary> Email приглашённого. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Код роли приглашения. </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary> Время создания приглашения. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Имя пригласившего. </summary>
    public string InvitedBy { get; init; } = string.Empty;
}

