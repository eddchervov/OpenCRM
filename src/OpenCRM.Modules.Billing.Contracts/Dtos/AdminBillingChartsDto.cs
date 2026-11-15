namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Данные диаграмм раздела биллинга админки. </summary>
public sealed class AdminBillingChartsDto
{
    /// <summary> Данные платежей по дням. </summary>
    public IReadOnlyList<AdminBillingDailyMoneyDto> DailyPayments { get; init; } = [];

    /// <summary> Распределение платежей по группам статусов. </summary>
    public IReadOnlyList<AdminBillingStatusGroupDto> StatusBreakdown { get; init; } = [];

    /// <summary> Выручка по тарифным планам. </summary>
    public IReadOnlyList<AdminBillingPlanRevenueDto> PlanRevenue { get; init; } = [];
}

