namespace OpenCRM.Admin.Api.Models;

/// <summary> Приглашение в организацию в административной панели. </summary>
public sealed class AdminOrganizationInvitation
{
    public string Email { get; init; } = string.Empty;
    public string RoleCode { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string InvitedBy { get; init; } = string.Empty;
}
