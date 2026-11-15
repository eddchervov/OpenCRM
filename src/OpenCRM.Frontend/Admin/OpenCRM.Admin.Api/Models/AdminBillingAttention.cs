namespace OpenCRM.Admin.Api.Models;

/// <summary> Проблемные зоны биллинга, требующие внимания. </summary>
public sealed class AdminBillingAttention
{
    /// <summary> Период анализа в днях. </summary>
    public int? PeriodDays { get; init; }

    /// <summary> Количество платежей без чека. </summary>
    public int? MissingReceiptCount { get; init; }

    /// <summary> Количество просроченных чеков. </summary>
    public int OverdueReceiptCount { get; init; }

    /// <summary> Количество неудачных активаций подписок. </summary>
    public int FailedActivationCount { get; init; }
}
