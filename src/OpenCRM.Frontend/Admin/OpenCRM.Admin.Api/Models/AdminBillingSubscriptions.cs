namespace OpenCRM.Admin.Api.Models;

/// <summary> Списки подписок пользователей и организаций. </summary>
public sealed class AdminBillingSubscriptions
{
    /// <summary> Подписки пользователей. </summary>
    public IReadOnlyList<AdminBillingUserSubscription> Users { get; init; } = [];

    /// <summary> Подписки организаций. </summary>
    public IReadOnlyList<AdminBillingOrganizationSubscription> Organizations { get; init; } = [];
}
