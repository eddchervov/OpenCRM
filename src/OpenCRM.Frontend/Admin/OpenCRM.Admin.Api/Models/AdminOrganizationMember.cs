namespace OpenCRM.Admin.Api.Models;

/// <summary> Участник организации в административной панели. </summary>
public sealed class AdminOrganizationMember
{
    public string UserId { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string RoleCode { get; init; } = string.Empty;
    public DateTime JoinedAt { get; init; }
}
