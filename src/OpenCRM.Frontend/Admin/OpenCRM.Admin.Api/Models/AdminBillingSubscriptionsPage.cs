namespace OpenCRM.Admin.Api.Models;

/// <summary> Страница списка подписок организаций с постраничной разбивкой. </summary>
public sealed class AdminBillingSubscriptionsPage
{
    /// <summary> Подписки организаций текущей страницы. </summary>
    public IReadOnlyList<AdminBillingOrganizationSubscription> Items { get; init; } = [];

    /// <summary> Общее количество подписок. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
