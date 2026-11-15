namespace OpenCRM.Admin.Api.Models;

/// <summary> Страница журнала активности пользователей. </summary>
public sealed record AdminUserActivityPage(
    bool Enabled,
    int RetentionDays,
    IReadOnlyList<AdminUserActivityItem> Items,
    long? NextBeforeId);
