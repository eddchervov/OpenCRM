namespace OpenCRM.Admin.Api.Models;

/// <summary> Страница списка платежей с постраничной разбивкой. </summary>
public sealed class AdminBillingPaymentsPage
{
    /// <summary> Платежи текущей страницы. </summary>
    public IReadOnlyList<AdminBillingPayment> Items { get; init; } = [];

    /// <summary> Общее количество платежей. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
