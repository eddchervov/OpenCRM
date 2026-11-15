namespace OpenCRM.Admin.Api.Models;

public sealed class AdminSmsHistoryPage
{
    public int TotalCount { get; init; }
    public IReadOnlyList<AdminSmsHistoryItem> Histories { get; init; } = [];
}
