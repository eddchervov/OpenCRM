namespace OpenCRM.Admin.Api.Models;

public sealed class AdminEmailCampaignsPage
{
    public IReadOnlyList<AdminEmailCampaign> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
