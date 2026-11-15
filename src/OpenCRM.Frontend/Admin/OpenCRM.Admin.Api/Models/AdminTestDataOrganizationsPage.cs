namespace OpenCRM.Admin.Api.Models;

public sealed class AdminTestDataOrganizationsPage
{
    public IReadOnlyList<AdminTestDataOrganization> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public bool IsLegacyResponse { get; init; }
}
