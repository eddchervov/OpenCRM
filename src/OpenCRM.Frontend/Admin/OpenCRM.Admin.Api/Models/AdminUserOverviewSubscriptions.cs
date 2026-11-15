namespace OpenCRM.Admin.Api.Models;

/// <summary> Подписки в обзоре пользователя. </summary>
public sealed class AdminUserOverviewSubscriptions
{
    /// <summary> Подписки пользователя. </summary>
    public IReadOnlyList<AdminUserSubscription> Users { get; init; } = [];

    /// <summary> Подписки организаций пользователя. </summary>
    public IReadOnlyList<AdminOrganizationSubscription> Organizations { get; init; } = [];

    /// <summary> История подписок. </summary>
    public IReadOnlyList<AdminUserSubscriptionHistory> History { get; init; } = [];
}
