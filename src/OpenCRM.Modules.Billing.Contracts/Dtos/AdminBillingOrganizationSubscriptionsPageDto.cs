namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Страница списка подписок организаций в админке биллинга. </summary>
public sealed class AdminBillingOrganizationSubscriptionsPageDto
{
    /// <summary> Записи на странице. </summary>
    public IReadOnlyList<AdminBillingOrganizationSubscriptionDto> Items { get; init; } = [];

    /// <summary> Общее количество подписок. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
