namespace OpenCRM.Admin.Api.Models;

/// <summary> Выручка по тарифному плану. </summary>
public sealed class AdminBillingPlanRevenue
{
    /// <summary> Идентификатор тарифного плана. </summary>
    public int PlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string Plan { get; init; } = string.Empty;

    /// <summary> Количество платежей. </summary>
    public int Count { get; init; }

    /// <summary> Сумма платежей. </summary>
    public decimal Amount { get; init; }
}
