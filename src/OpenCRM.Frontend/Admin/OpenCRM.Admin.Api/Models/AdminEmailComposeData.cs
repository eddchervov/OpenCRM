namespace OpenCRM.Admin.Api.Models;

public sealed class AdminEmailComposeData
{
    public IReadOnlyList<AdminEmailOrganizationType> OrganizationTypes { get; init; } = [];
    public IReadOnlyList<AdminEmailUser> Users { get; init; } = [];
}
