namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Параметры запроса страницы раздела биллинга. </summary>
public sealed class AdminBillingPageQuery
{
    /// <summary> Строка поиска. </summary>
    public string? Search { get; init; }

    /// <summary> Статус для фильтрации. </summary>
    public string Status { get; init; } = "all";

    /// <summary> Период в днях для фильтрации. </summary>
    public int PeriodDays { get; init; } = 30;

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; } = 1;

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; } = 50;

    /// <summary> Поле сортировки. </summary>
    public string Sort { get; init; } = "date";

    /// <summary> Признак сортировки по убыванию. </summary>
    public bool Descending { get; init; }
}
