namespace OpenCRM.Admin.Api.Models;

public sealed class AdminSiteNotificationHistoryPage
{
    public IReadOnlyList<AdminSiteNotification> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool IsLegacyResponse { get; init; }
}
