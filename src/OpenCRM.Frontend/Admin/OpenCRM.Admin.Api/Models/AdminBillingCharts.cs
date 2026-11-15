namespace OpenCRM.Admin.Api.Models;

/// <summary> Данные для графиков биллинга. </summary>
public sealed class AdminBillingCharts
{
    /// <summary> Ежедневные суммы платежей. </summary>
    public IReadOnlyList<AdminBillingDailyMoney> DailyPayments { get; init; } = [];

    /// <summary> Разбивка платежей по группам статусов. </summary>
    public IReadOnlyList<AdminBillingStatusGroup> StatusBreakdown { get; init; } = [];

    /// <summary> Выручка по тарифным планам. </summary>
    public IReadOnlyList<AdminBillingPlanRevenue> PlanRevenue { get; init; } = [];
}
