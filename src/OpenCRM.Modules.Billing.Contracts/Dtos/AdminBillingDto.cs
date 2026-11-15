namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Сводные данные раздела биллинга админки. </summary>
public sealed class AdminBillingDto
{
    /// <summary> Дата и время формирования сводки. </summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary> Ключевые показатели биллинга. </summary>
    public AdminBillingKpisDto Kpis { get; init; } = new();

    /// <summary> Данные диаграмм биллинга. </summary>
    public AdminBillingChartsDto Charts { get; init; } = new();

    /// <summary> Распределение платежей по исходным статусам. </summary>
    public IReadOnlyList<AdminBillingRawStatusDto> RawStatusBreakdown { get; init; } = [];

    /// <summary> Список платежей. </summary>
    public IReadOnlyList<AdminBillingPaymentDto> Payments { get; init; } = [];

    /// <summary> Данные о подписках. </summary>
    public AdminBillingSubscriptionsDto Subscriptions { get; init; } = new();
}

