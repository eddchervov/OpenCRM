namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Страница списка платежей в админке биллинга. </summary>
public sealed class AdminBillingPaymentsPageDto
{
    /// <summary> Записи на странице. </summary>
    public IReadOnlyList<AdminBillingPaymentDto> Items { get; init; } = [];

    /// <summary> Общее количество платежей. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
