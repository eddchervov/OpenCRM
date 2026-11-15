namespace OpenCRM.Admin.Api.Models;

/// <summary> Сводные данные биллинга для администрирования. </summary>
public sealed class AdminBilling
{
    /// <summary> Момент формирования сводки биллинга. </summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary> Ключевые показатели биллинга. </summary>
    public AdminBillingKpis Kpis { get; init; } = new();

    /// <summary> Данные для графиков биллинга. </summary>
    public AdminBillingCharts Charts { get; init; } = new();

    /// <summary> Разбивка платежей по исходным статусам. </summary>
    public IReadOnlyList<AdminBillingRawStatus> RawStatusBreakdown { get; init; } = [];

    /// <summary> Список платежей. </summary>
    public IReadOnlyList<AdminBillingPayment> Payments { get; init; } = [];

    /// <summary> Сведения о подписках пользователей и организаций. </summary>
    public AdminBillingSubscriptions Subscriptions { get; init; } = new();
}
