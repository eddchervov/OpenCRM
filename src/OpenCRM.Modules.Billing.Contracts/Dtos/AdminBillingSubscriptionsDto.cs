namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Подписки пользователей и организаций для административного отчёта. </summary>
public sealed class AdminBillingSubscriptionsDto
{
    /// <summary> Список подписок пользователей. </summary>
    public IReadOnlyList<AdminBillingUserSubscriptionDto> Users { get; init; } = [];

    /// <summary> Список подписок организаций. </summary>
    public IReadOnlyList<AdminBillingOrganizationSubscriptionDto> Organizations { get; init; } = [];
}

